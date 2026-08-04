using DNBridge.Tlv;

namespace DNBridge.Commands;

/// <summary>
/// Element value DTO for GetData responses. Mirrors C++ cl_Elem_104_Value.
/// </summary>
public class ElementValue : ITlvSerializable
{
    public ulong Address { get; set; }
    public double Value { get; set; }
    public uint Quality { get; set; }
    public DateTime DateTime { get; set; }

    public uint ObjectTag => TlvTags.TAG_CLASS_VALUE;

    public void Serialize(TlvWriter writer)
    {
        writer.AddU64(TlvTags.TAG_u64_104ADDR, Address);
        writer.AddDouble(TlvTags.TAG_dbl_Value, Value);
        writer.AddU32(TlvTags.TAG_u32_Quality, Quality);
        writer.AddDate(TlvTags.TAG_dt_DateTime, DateTime);
    }

    public bool Deserialize(uint tag, uint length, ReadOnlySpan<byte> value)
    {
        switch (tag)
        {
            case TlvTags.TAG_u64_104ADDR:
                Address = TlvReader.ReadU64(value);
                return true;
            case TlvTags.TAG_dbl_Value:
                Value = TlvReader.ReadDouble(value);
                return true;
            case TlvTags.TAG_u32_Quality:
                Quality = TlvReader.ReadU32(value);
                return true;
            case TlvTags.TAG_dt_DateTime:
                DateTime = TlvReader.ReadDate(value);
                return true;
            default:
                return false;
        }
    }
}
