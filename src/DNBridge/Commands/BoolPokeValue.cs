using DNBridge.Tlv;

namespace DNBridge.Commands;

/// <summary>
/// Boolean poke value. Mirrors C++ cl_Bool_Poke_Value.
/// Used for single point commands (M_SP_NA_1).
/// </summary>
public class BoolPokeValue : PokeValue
{
    public bool Value { get; set; }
    public byte COT { get; set; } = 3; // CS101_COT_SPONTANEOUS

    public override uint ObjectTag => TlvTags.TAG_CLASS_POKE_BOOL;

    public override void Serialize(TlvWriter writer)
    {
        writer.AddBool(TlvTags.TAG_b_Value, Value);
        writer.AddU8(TlvTags.TAG_u8_COT, COT);
    }

    public override bool Deserialize(uint tag, uint length, ReadOnlySpan<byte> value)
    {
        switch (tag)
        {
            case TlvTags.TAG_b_Value:
                Value = TlvReader.ReadBool(value);
                return true;
            case TlvTags.TAG_u8_COT:
                COT = TlvReader.ReadU8(value);
                return true;
            default:
                return false;
        }
    }

    public override string GetDetails() => $"Bool({Value}, COT={COT})";
}
