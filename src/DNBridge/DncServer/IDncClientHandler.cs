namespace DNBridge.DncServer;

/// <summary>
/// Represents a single DNC client TCP session.
/// </summary>
public interface IDncClientHandler : IAsyncDisposable
{
    int SessionId { get; }
    string RemoteAddress { get; }
    bool IsConnected { get; }

    Task SendAsync(byte[] data, CancellationToken ct);
    Task DisconnectAsync();
}
