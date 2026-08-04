namespace DNBridge.Wpf;

/// <summary>
/// One row in a master/detail traffic view (DNC or SCADA).
/// Both Summary (master line) and Detail (detail pane) are built eagerly when the
/// frame is received/sent, so a click just displays the stored Detail.
/// </summary>
public class TrafficItem
{
    public string Summary { get; init; } = "";
    public string Detail  { get; init; } = "";

    /// <summary>false = inbound (◀ RX), true = outbound (▶ TX). Drives row color.</summary>
    public bool Outbound { get; init; }
}
