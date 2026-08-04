namespace DNBridge.Events;

/// <summary>
/// Fired after a Poke value has been forwarded to SCADA, confirming what was sent.
/// </summary>
public class ElementPokeEventArgs : EventArgs
{
    public uint SchemaId { get; }
    public ulong Address { get; }
    public ushort CA { get; }
    public uint IOA { get; }
    public double Value { get; }
    public string PokeType { get; }
    public DateTime SentAt { get; }

    public ElementPokeEventArgs(uint schemaId, ulong address, ushort ca, uint ioa, double value, string pokeType)
    {
        SchemaId = schemaId;
        Address = address;
        CA = ca;
        IOA = ioa;
        Value = value;
        PokeType = pokeType;
        SentAt = DateTime.Now;
    }
}
