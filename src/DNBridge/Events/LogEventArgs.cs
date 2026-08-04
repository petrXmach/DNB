namespace DNBridge.Events;

public enum DnbLogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4,
    Fatal = 5
}

public class LogEventArgs : EventArgs
{
    public DateTime Timestamp { get; }
    public string Message { get; }
    public DnbLogLevel Level { get; }

    public LogEventArgs(string message, DnbLogLevel level = DnbLogLevel.Info)
    {
        Timestamp = DateTime.Now;
        Message = message;
        Level = level;
    }

    public override string ToString() => $"[{Timestamp:HH:mm:ss.fff}] [{Level}] {Message}";
}
