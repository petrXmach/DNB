using DNBridge.Commands;

namespace DNBridge.Events;

public class CommandReceivedEventArgs : EventArgs
{
    public string CommandName { get; }
    /// <summary>One-line summary shown in the master traffic list.</summary>
    public string Summary { get; }
    /// <summary>Full (possibly multi-line) detail shown in the detail pane.</summary>
    public string Detail { get; }
    public DateTime Timestamp { get; }
    public uint Tag { get; }
    public byte[] Payload { get; }
    public DnbCommand? DeserializedCommand { get; }

    /// <summary>
    /// Direction of the frame on the DNC link: false = received from DNC (inbound),
    /// true = sent by DNBridge to DNC (outbound, e.g. an answer).
    /// </summary>
    public bool IsOutbound { get; init; }

    public CommandReceivedEventArgs(string commandName, string summary, string detail)
        : this(commandName, summary, detail, 0, [], null)
    {
    }

    public CommandReceivedEventArgs(string commandName, string summary, string detail, uint tag, byte[] payload, DnbCommand? deserializedCommand)
    {
        CommandName = commandName;
        Summary = summary;
        Detail = detail;
        Tag = tag;
        Payload = payload;
        DeserializedCommand = deserializedCommand;
        Timestamp = DateTime.Now;
    }

    public override string ToString()
    {
        // ◀ received from DNC, ▶ sent to DNC. The TLV tag name is dropped: every command's
        // Summary already carries a self-identifying label (Poke:, GetData:, GetDataAnswer:, …).
        string arrow = IsOutbound ? "▶" : "◀";
        return $"[{Timestamp:HH:mm:ss.fff}] {arrow} {Summary}";
    }
}
