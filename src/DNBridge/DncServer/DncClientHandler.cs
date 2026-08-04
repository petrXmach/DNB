using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using DNBridge.Commands;
using DNBridge.Events;
using DNBridge.Tlv;

namespace DNBridge.DncServer;

/// <summary>
/// Handles a single DNC client TCP connection.
/// Runs an async receive loop that reads TLV-framed messages from the stream.
/// </summary>
public class DncClientHandler : IDncClientHandler, IAsyncDisposable
{
    private const int TlvHeaderSize = 8; // uint32 Tag + uint32 Length
    private const int MaxPayloadSize = 128 * 1024; // 128 KB, matches C++ RX_BUFF_LEN

    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private volatile bool _isConnected;
    private int _disposed;

    private CommandExecutor? _executor;
    private DncSession? _session;

    public int SessionId { get; }
    public string RemoteAddress { get; }
    public bool IsConnected => _isConnected;

    /// <summary>Raised when this client disconnects (for any reason).</summary>
    public event EventHandler<DncConnectionEventArgs>? Disconnected;

    /// <summary>Raised when a complete TLV frame is received.</summary>
    public event EventHandler<CommandReceivedEventArgs>? FrameReceived;

    /// <summary>Raised for log messages.</summary>
    public event EventHandler<LogEventArgs>? LogMessage;

    public DncClientHandler(TcpClient tcpClient, int sessionId)
    {
        _tcpClient = tcpClient;
        _stream = tcpClient.GetStream();
        _isConnected = true;
        SessionId = sessionId;

        var ep = tcpClient.Client.RemoteEndPoint as IPEndPoint;
        RemoteAddress = ep?.ToString() ?? "unknown";

        // Enable TCP keep-alive to detect dead connections (~11s detection time)
        var socket = tcpClient.Client;
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 5);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 2);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);
    }

    /// <summary>
    /// Set the command executor and session for this handler.
    /// Must be called before RunAsync.
    /// </summary>
    public void SetExecutor(CommandExecutor executor, DncSession session)
    {
        _executor = executor;
        _session = session;
    }

    /// <summary>
    /// Main receive loop. Reads TLV-framed messages until cancellation or disconnect.
    /// Call this as a fire-and-forget task from DncTcpServer.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var headerBuffer = new byte[TlvHeaderSize];

        try
        {
            while (!ct.IsCancellationRequested && _isConnected)
            {
				// 1. Read TLV header (8 bytes); outer tag must be 0 for the envelope
				if (!await ReadExactAsync(headerBuffer, 0, TlvHeaderSize, ct))
                    break; // connection closed

                uint tag = BinaryPrimitives.ReadUInt32LittleEndian(headerBuffer.AsSpan(0, 4));
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(headerBuffer.AsSpan(4, 4));

                // 2. Validate payload length
                if (length > MaxPayloadSize || tag!=0)
                {
                    OnLog($"Session {SessionId}: payload too large ({length} bytes) or wrong tag ({tag}), disconnecting in 5s", DnbLogLevel.Error);
                    try { await Task.Delay(5000, ct); } catch (OperationCanceledException) { }
                    break;
                }

				// 3. Read payload - inner TLV envelope containing the actual command and data
				byte[] payload = new byte[length];
                if (length > 0 && !await ReadExactAsync(payload, 0, (int)length, ct))
                    break; // connection closed mid-payload

                // 4. Unwrap outer envelope (tag=0) and extract the inner command tag
                if (length >= TlvHeaderSize)
                {
                    uint innerTag = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(0, 4));
                    uint innerLength = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(4, 4));
                    OnFrameReceived(innerTag, innerLength, payload);
                }
                else
                {
                    OnLog($"Session {SessionId}: envelope payload too short ({length} bytes)", DnbLogLevel.Warning);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (IOException ex)
        {
            OnLog($"Session {SessionId}: IO error: {ex.Message}", DnbLogLevel.Warning);
        }
        catch (SocketException ex)
        {
            OnLog($"Session {SessionId}: socket error: {ex.Message}", DnbLogLevel.Warning);
        }
        finally
        {
            _isConnected = false;
            OnLog($"Session {SessionId}: disconnected ({RemoteAddress})", DnbLogLevel.Info);
            Disconnected?.Invoke(this, new DncConnectionEventArgs(false, RemoteAddress));
        }
    }

    public async Task SendAsync(byte[] data, CancellationToken ct)
    {
        if (!_isConnected)
            return;

        await _sendLock.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(data, ct);
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            OnLog($"Session {SessionId}: send failed: {ex.Message}", DnbLogLevel.Warning);
            _isConnected = false;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public Task DisconnectAsync()
    {
        _isConnected = false;
        _tcpClient.Close();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        // Idempotent: both DncTcpServer.StopAsync and RunClientAsync's finally may
        // dispose the same handler when shutdown races client teardown.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _isConnected = false;
        _sendLock.Dispose();
        _stream.Dispose();
        _tcpClient.Dispose();
        await Task.CompletedTask;
    }

    /// <summary>
    /// Reads exactly <paramref name="count"/> bytes into buffer, handling partial TCP reads.
    /// Returns false if the connection was closed before all bytes were read.
    /// </summary>
    private async Task<bool> ReadExactAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int bytesRead = await _stream.ReadAsync(
                buffer.AsMemory(offset + totalRead, count - totalRead), ct);

            if (bytesRead == 0)
                return false; // connection closed

            totalRead += bytesRead;
        }
        return true;
    }

    private void OnFrameReceived(uint innerTag, uint innerLength, byte[] payload)
    {
        string tagName = TlvTags.GetName(innerTag);

        // Attempt TLV deserialization
        DnbCommand? command = null;
        try
        {
            var deserialized = TlvReader.Deserialize(payload);
            command = deserialized as DnbCommand;
        }
        catch (Exception ex)
        {
            OnLog($"Session {SessionId}: deserialization failed for {tagName}: {ex.Message}", DnbLogLevel.Warning);
        }

        string fallback = $"Tag=0x{innerTag:X8}, Length={innerLength} bytes";
        // Poke frames carry only a raw element id; enrich the summary with the element's
        // decimal IOA and a friendly label (Main104 name / decimal setpoint id) using the
        // live session mapping. Everything else uses the command's own self-labeled summary.
        bool enrichPoke = command is PokeCommand && _session != null;
        string summary = enrichPoke
            ? PokeTrafficFormatter.FormatPokeSummary((PokeCommand)command!, _session!)
            : command?.GetSummary() ?? fallback;
        string detail = enrichPoke ? summary : command?.GetDetails() ?? fallback;

        FrameReceived?.Invoke(this, new CommandReceivedEventArgs(
            tagName, summary, detail, innerTag, payload, command));

        // Execute command and send answer
        if (command != null && _executor != null && _session != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _executor.ExecuteAsync(command, _session, CancellationToken.None);

                    if (command.Answer != null)
                    {
                        var writer = new TlvWriter();
                        writer.SerializeObject(command.Answer);
                        byte[] responseData = writer.ToEnvelope();
                        await SendAsync(responseData, CancellationToken.None);
                        // The answer itself is shown in the DNC Traffic tab (outbound row);
                        // keep only a Debug-level echo in the log.
                        OnLog($"Session {SessionId}: sent {command.Answer.GetSummary()}", DnbLogLevel.Debug);

                        // Mirror the answer into the DNC traffic view (outbound direction).
                        FrameReceived?.Invoke(this, new CommandReceivedEventArgs(
                            TlvTags.GetName(command.Answer.ObjectTag),
                            command.Answer.GetSummary(),
                            command.Answer.GetDetails(),
                            command.Answer.ObjectTag,
                            responseData,
                            command.Answer) { IsOutbound = true });
                    }
                }
                catch (Exception ex)
                {
                    OnLog($"Session {SessionId}: command execution failed: {ex.Message}", DnbLogLevel.Error);
                }
            });
        }
    }

    private void OnLog(string message, DnbLogLevel level)
    {
        LogMessage?.Invoke(this, new LogEventArgs(message, level));
    }
}
