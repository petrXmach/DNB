namespace DNBridge.Events;

/// <summary>
/// Carries registered element descriptors grouped by category after RegisterElements completes.
/// </summary>
public class ElementsRegisteredEventArgs : EventArgs
{
    public IReadOnlyList<ElementInfo> MonitorElements { get; }
    public IReadOnlyList<ElementInfo> CommandElements { get; }
    public IReadOnlyList<ElementInfo> Main104Elements { get; }
    public DateTime Timestamp { get; }

    public ElementsRegisteredEventArgs(
        IReadOnlyList<ElementInfo> monitor,
        IReadOnlyList<ElementInfo> command,
        IReadOnlyList<ElementInfo> main104)
    {
        MonitorElements = monitor;
        CommandElements = command;
        Main104Elements = main104;
        Timestamp = DateTime.Now;
    }
}

/// <summary>
/// Descriptor for a registered element: schema (DNC) ID, IEC 104 address, type, and the
/// element's value at registration time.
/// </summary>
/// <remarks>
/// The value is carried here because the host rebuilds its rows from scratch on every
/// RegisterElements, and DNC sends that several times (double-fire + ≤30-element batches).
/// A row built without a value would blank out any value already in the cache — which is
/// exactly what happens to the XChng.cfg input defaults (seeded before this event, then
/// never re-sent, since the loader is once-per-session) and to any element SCADA had
/// already updated. <see cref="UpdatedAt"/> is <see cref="DateTime.MinValue"/> when the
/// element has no value yet.
/// </remarks>
public record ElementInfo(
    uint SchemaId, ulong Address, ushort CA, uint IOA, byte Iec104Type,
    double Value, uint Quality, DateTime UpdatedAt);
