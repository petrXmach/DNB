namespace DNBridge.Elements;

/// <summary>
/// Represents a single IEC 60870-5-104 element in the cache.
/// Address encoding: bits 39-24 = CA (uint16), bits 23-0 = IOA (uint24).
/// </summary>
public class Element104
{
    public ulong Address { get; }
    public ushort CA => (ushort)((Address >> 24) & 0xFFFF);
    public uint IOA => (uint)(Address & 0xFFFFFF);

    public double Value { get; set; }
    public uint Quality { get; set; }
    public DateTime LastDataTime { get; set; }
    public byte Iec104Type { get; set; }
    public bool IsSetPoint { get; set; }

    public Element104(ulong address, bool isSetPoint = false)
    {
        Address = address;
        IsSetPoint = isSetPoint;
    }
}
