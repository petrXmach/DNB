namespace DNBridge.Events;

public class ScadaDataEventArgs : EventArgs
{
    /// <summary>Fully-formatted master line shown in the traffic list — see TrafficFormat.ScadaMaster.</summary>
    public string Summary { get; }
    /// <summary>Full (possibly multi-line) detail shown in the detail pane.</summary>
    public string Detail { get; }
    public DateTime Timestamp { get; }

    /// <summary>
    /// Direction on the SCADA link: false = received from SCADA (inbound ASDU /
    /// confirmation), true = sent by DNBridge to SCADA (control command, GI).
    /// </summary>
    public bool IsOutbound { get; }

    public ScadaDataEventArgs(string summary, string detail, bool isOutbound = false)
    {
        Summary = summary;
        Detail = detail;
        IsOutbound = isOutbound;
        Timestamp = DateTime.Now;
    }

    public override string ToString() => Summary;
}
