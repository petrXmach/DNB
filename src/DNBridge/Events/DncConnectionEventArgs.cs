namespace DNBridge.Events;

public class DncConnectionEventArgs : EventArgs
{
    public bool IsConnected { get; }
    public string? ClientAddress { get; }

    public DncConnectionEventArgs(bool isConnected, string? clientAddress = null)
    {
        IsConnected = isConnected;
        ClientAddress = clientAddress;
    }
}
