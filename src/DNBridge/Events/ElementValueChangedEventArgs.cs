namespace DNBridge.Events;

/// <summary>
/// Carries element value updates produced by a single SCADA ASDU reception.
/// </summary>
public class ElementValueChangedEventArgs : EventArgs
{
    public IReadOnlyList<ElementUpdate> Updates { get; }
    public DateTime Timestamp { get; }

    public ElementValueChangedEventArgs(IReadOnlyList<ElementUpdate> updates)
    {
        Updates = updates;
        Timestamp = DateTime.Now;
    }
}

/// <summary>
/// One element value update: address, value, quality descriptor, and when it was received.
/// </summary>
public record ElementUpdate(ulong Address, double Value, uint Quality, DateTime UpdatedAt);
