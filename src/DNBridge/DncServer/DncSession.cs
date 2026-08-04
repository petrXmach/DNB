using DNBridge.Elements;

namespace DNBridge.DncServer;

/// <summary>
/// Per-connection session state for a DNC client.
/// Holds SchemaID↔Element104 mappings and GetData pagination cursor.
/// </summary>
public class DncSession
{
    public string? ServerName { get; set; }

    /// <summary>Monitor elements: SchemaID → Element104 (used by GetData).</summary>
    public Dictionary<uint, Element104> MonitorElements { get; } = new();

    /// <summary>Command elements: SchemaID → Element104 (used by Poke).</summary>
    public Dictionary<uint, Element104> CommandElements { get; } = new();

    /// <summary>
    /// TEMPORARY (Main104 traffic-loop test): IEC104 address → flagged Main104 id for
    /// INPUT regulation params (SCADA → DNC). On a SCADA update of one of these addresses,
    /// DNBridge pushes a reverse-Poke to DNC — the only path that reaches DNC's SetParameter
    /// (GetData cannot carry Main104 values). Remove with the XChng.cfg loader.
    /// </summary>
    public Dictionary<ulong, uint> Main104Inputs { get; } = new();

    /// <summary>
    /// TEMPORARY (Main104 input mirrors): input params echoed back to SCADA at a separate
    /// OUTPUT address so the operator can verify the values DNBridge received. DNC never
    /// touches these — DNBridge owns them: on the State output Poke (one per calc cycle) the
    /// current cached input value (Source) is re-sent to SCADA at the mirror address (Target).
    /// Declared by "M"-flagged lines in XChng.cfg. Remove with the loader.
    /// </summary>
    public List<Main104Mirror> Main104Mirrors { get; } = new();

    /// <summary>
    /// TEMPORARY (Main104): load-once guard for the XChng.cfg loader. DNC sends RegisterElements
    /// several times (double-fire + ≤30-element batching), each dispatched concurrently, so the
    /// static XChng.cfg config must be loaded exactly once per session — otherwise the appended
    /// <see cref="Main104Mirrors"/> list accumulates duplicates and each mirror is sent multiple
    /// times per calc cycle. Reset in <see cref="Clear"/>. Remove with the loader.
    /// </summary>
    private int _xchngLoaded;

    /// <summary>
    /// TEMPORARY (Main104): returns true exactly once per session (race-safe against concurrent
    /// RegisterElements frames); subsequent calls return false so the loader is skipped.
    /// </summary>
    public bool TryBeginXChngLoad() => Interlocked.CompareExchange(ref _xchngLoaded, 1, 0) == 0;

    // GetData pagination state
    private List<KeyValuePair<uint, Element104>>? _dataList;
    private int _dataCursorIndex;

    /// <summary>
    /// Snapshot MonitorElements into an ordered list and reset cursor to beginning.
    /// Called when GetDataCommand.Start == true.
    /// </summary>
    public void ResetDataCursor()
    {
        _dataList = new List<KeyValuePair<uint, Element104>>(MonitorElements);
        _dataCursorIndex = 0;
    }

    /// <summary>
    /// Advance cursor and return next element pair, or null if exhausted.
    /// </summary>
    public (uint SchemaId, Element104 Element)? GetNext()
    {
        if (_dataList == null || _dataCursorIndex >= _dataList.Count)
            return null;

        var kvp = _dataList[_dataCursorIndex];
        _dataCursorIndex++;
        return (kvp.Key, kvp.Value);
    }

    /// <summary>True when cursor has reached the end of the element list.</summary>
    public bool IsCursorExhausted =>
        _dataList == null || _dataCursorIndex >= _dataList.Count;

    /// <summary>Clear all session state (on disconnect).</summary>
    public void Clear()
    {
        ServerName = null;
        MonitorElements.Clear();
        CommandElements.Clear();
        Main104Inputs.Clear();
        Main104Mirrors.Clear();
        _xchngLoaded = 0;
        _dataList = null;
        _dataCursorIndex = 0;
    }
}

/// <summary>
/// TEMPORARY (Main104 input mirrors): one input→SCADA-output echo mapping.
/// <paramref name="Source"/> is the cached input element (live value, updated by SCADA);
/// <paramref name="Target"/> is the cached mirror output element (address / CA / IOA and the
/// IEC104 control type to send it with); <paramref name="SourceId"/> is the bare Main104 id
/// (1,2,4,5,6,7) for logging. Remove with the XChng.cfg loader.
/// </summary>
public record Main104Mirror(Element104 Source, Element104 Target, uint SourceId);
