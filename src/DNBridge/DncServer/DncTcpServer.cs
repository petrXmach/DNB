using System.Net;
using System.Net.Sockets;
using DNBridge.Events;

namespace DNBridge.DncServer;

/// <summary>
/// TCP server that listens for a single DNC client connection.
/// If a client is already connected, additional connections are rejected.
/// When the client disconnects, the server automatically accepts the next connection.
/// </summary>
public class DncTcpServer : IAsyncDisposable
{
    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;
    private int _nextSessionId;

    private DncClientHandler? _currentClient;

    public bool IsListening { get; private set; }

    /// <summary>Raised when a DNC client connects.</summary>
    public event EventHandler<DncConnectionEventArgs>? ClientConnected;

    /// <summary>Raised when a DNC client disconnects.</summary>
    public event EventHandler<DncConnectionEventArgs>? ClientDisconnected;

    /// <summary>Raised when a complete TLV frame is received from the client.</summary>
    public event EventHandler<CommandReceivedEventArgs>? FrameReceived;

    /// <summary>Raised for log messages.</summary>
    public event EventHandler<LogEventArgs>? LogMessage;

    /// <summary>
    /// Raised after a DncClientHandler is created but before RunAsync.
    /// Use to inject CommandExecutor and DncSession via handler.SetExecutor().
    /// </summary>
    public event Action<DncClientHandler>? ClientHandlerCreated;

    public DncTcpServer(int port)
    {
        _port = port;
    }

    /// <summary>
    /// Starts listening on the configured port. Returns immediately;
    /// the accept loop runs as a background task.
    /// </summary>
    public Task StartAsync(CancellationToken ct)
    {
        if (IsListening)
            return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        IsListening = true;

        OnLog($"TCP server listening on port {_port}", DnbLogLevel.Info);

        _acceptLoopTask = AcceptLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsListening)
            return;

        _cts?.Cancel();
        _listener?.Stop();
        IsListening = false;

        // Wait for accept loop to finish
        if (_acceptLoopTask != null)
        {
            try { await _acceptLoopTask; }
            catch (OperationCanceledException) { }
        }

        // Disconnect and dispose active client. Capture atomically: RunClientAsync's
        // finally may null _currentClient concurrently as cancellation unwinds it,
        // which would otherwise NRE between the null-check and the dereference.
        var client = Interlocked.Exchange(ref _currentClient, null);
        if (client != null)
        {
            await client.DisconnectAsync();
            await client.DisposeAsync();
        }

        OnLog("TCP server stopped", DnbLogLevel.Info);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var tcpClient = await _listener!.AcceptTcpClientAsync(ct);

                // Reject if a client exists; we only support one client at a time
                if (_currentClient != null)
                {
                    var ep = tcpClient.Client.RemoteEndPoint as IPEndPoint;
                    OnLog($"Rejected connection from {ep} — a client is already connected", DnbLogLevel.Warning);
                    tcpClient.Close();
                    continue;
                }

                int sessionId = Interlocked.Increment(ref _nextSessionId);
                var handler = new DncClientHandler(tcpClient, sessionId);

                handler.Disconnected += OnClientDisconnected;
                handler.FrameReceived += (s, e) => FrameReceived?.Invoke(s, e);
                handler.LogMessage += (s, e) => LogMessage?.Invoke(s, e);

                _currentClient = handler;

                // Allow engine to inject executor before handler starts
                ClientHandlerCreated?.Invoke(handler);

                OnLog($"DNC client connected: {handler.RemoteAddress} (session {sessionId})", DnbLogLevel.Info);
                ClientConnected?.Invoke(this, new DncConnectionEventArgs(true, handler.RemoteAddress));

                // Run client handler in background; we don't await it here because we want to continue accepting new connections
                _ = RunClientAsync(handler, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    OnLog($"Accept error: {ex.Message}", DnbLogLevel.Error);
                    // Brief pause before retrying to avoid tight error loop
                    try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
                }
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private async Task RunClientAsync(DncClientHandler handler, CancellationToken ct)
    {
        try
        {
            await handler.RunAsync(ct);
        }
        catch (Exception ex)
        {
            OnLog($"Client crashed: {ex}", DnbLogLevel.Error);
        }
        finally
        {
            // Only relinquish the slot if it still points at THIS handler. A fast DNC
            // reconnect after an abrupt death could already have installed a new client;
            // an unconditional null here would evict that live client and corrupt the
            // single-client tracking.
            Interlocked.CompareExchange(ref _currentClient, null, handler);
            await handler.DisposeAsync();
            OnLog("Ready for new connection", DnbLogLevel.Info);
        }
    }

    private void OnClientDisconnected(object? sender, DncConnectionEventArgs e)
    {
        ClientDisconnected?.Invoke(this, e);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }

    private void OnLog(string message, DnbLogLevel level)
    {
        LogMessage?.Invoke(this, new LogEventArgs(message, level));
    }
}
