namespace DNBridge.Events;

public class IsRunningEventArgs : EventArgs
{
    public bool IsRunning { get; }

    public IsRunningEventArgs(bool isRunning)
    {
        IsRunning = isRunning;
    }
}
