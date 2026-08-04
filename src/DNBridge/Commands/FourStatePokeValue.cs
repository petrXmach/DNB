using DNBridge.Tlv;

namespace DNBridge.Commands;

/// <summary>
/// Four-state (double point) poke value. Mirrors C++ cl_4State_Poke_Value.
/// Used for double point commands (M_DP_NA_1). Value range: 0-3.
/// </summary>
public class FourStatePokeValue : PokeValue
{
    public byte Value { get; set; }
    public byte COT { get; set; } = 3; // CS101_COT_SPONTANEOUS

    public override uint ObjectTag => TlvTags.TAG_CLASS_POKE_4STATE;

    public override void Serialize(TlvWriter writer)
    {
        writer.AddU8(TlvTags.TAG_u8_Value, Value);
        writer.AddU8(TlvTags.TAG_u8_COT, COT);
    }

    public override bool Deserialize(uint tag, uint length, ReadOnlySpan<byte> value)
    {
        switch (tag)
        {
            case TlvTags.TAG_u8_Value:
                Value = TlvReader.ReadU8(value);
                return true;
            case TlvTags.TAG_u8_COT:
                COT = TlvReader.ReadU8(value);
                return true;
            default:
                return false;
        }
    }

    public override string GetDetails() => $"4State(0x{Value:X2}, COT={COT})";
}
