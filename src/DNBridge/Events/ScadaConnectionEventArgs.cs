namespace DNBridge.Events;

public class ScadaConnectionEventArgs : EventArgs
{
    public bool IsConnected { get; }
    public string? ServerAddress { get; }

    public ScadaConnectionEventArgs(bool isConnected, string? serverAddress = null)
    {
        IsConnected = isConnected;
        ServerAddress = serverAddress;
    }
}
