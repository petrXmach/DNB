using DNBridge.Tlv;

namespace DNBridge.Commands;

/// <summary>
/// Float poke value. Mirrors C++ cl_Float_Poke_Value.
/// Used for measured value short commands (M_ME_NC_1).
/// </summary>
public class FloatPokeValue : PokeValue
{
    public double Value { get; set; }
    public byte COT { get; set; } = 3; // CS101_COT_SPONTANEOUS

    public override uint ObjectTag => TlvTags.TAG_CLASS_POKE_FLT;

    public override void Serialize(TlvWriter writer)
    {
        writer.AddDouble(TlvTags.TAG_dbl_Value, Value);
        writer.AddU8(TlvTags.TAG_u8_COT, COT);
    }

    public override bool Deserialize(uint tag, uint length, ReadOnlySpan<byte> value)
    {
        switch (tag)
        {
            case TlvTags.TAG_dbl_Value:
                Value = TlvReader.ReadDouble(value);
                return true;
            case TlvTags.TAG_u8_COT:
                COT = TlvReader.ReadU8(value);
                return true;
            default:
                return false;
        }
    }

    public override string GetDetails() => $"Float({Value:F4}, COT={COT})";
}
