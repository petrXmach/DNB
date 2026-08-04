using DNBridge.Tlv;

namespace DNBridge.Commands;

/// <summary>
/// Answer to RegisterElements command. Mirrors C++ cl_Reg_Elems_Answer.
/// </summary>
public class RegisterElementsAnswer : DnbCommand
{
    public bool IsOk { get; set; }

    public override uint ObjectTag => TlvTags.TAG_CLASS_ANSW_REG_ELEMS;

    public override void Serialize(TlvWriter writer)
    {
        base.Serialize(writer);
        writer.AddBool(TlvTags.TAG_b_OK, IsOk);
    }

    public override bool Deserialize(uint tag, uint length, ReadOnlySpan<byte> value)
    {
        if (tag == TlvTags.TAG_b_OK)
        {
            IsOk = TlvReader.ReadBool(value);
            return true;
        }
        return base.Deserialize(tag, length, value);
    }

    public override string GetDetails() => $"RegElemsAnswer: OK={IsOk}";
}
