using DNBridge.Tlv;

namespace DNBridge.Commands;

/// <summary>
/// Answer to Init command. Mirrors C++ cl_Init_Answer.
/// </summary>
public class InitAnswer : DnbCommand
{
    public const byte ModeIec104 = 0;

    public bool IsOk { get; set; }
    public byte Mode { get; set; }

    public override uint ObjectTag => TlvTags.TAG_CLASS_ANSW_INIT;

    public override void Serialize(TlvWriter writer)
    {
        base.Serialize(writer);
        writer.AddBool(TlvTags.TAG_b_OK, IsOk);
        writer.AddU8(TlvTags.TAG_u8_Mode, Mode);
    }

    public override bool Deserialize(uint tag, uint length, ReadOnlySpan<byte> value)
    {
        switch (tag)
        {
            case TlvTags.TAG_b_OK:
                IsOk = TlvReader.ReadBool(value);
                return true;
            case TlvTags.TAG_u8_Mode:
                Mode = TlvReader.ReadU8(value);
                return true;
            default:
                return base.Deserialize(tag, length, value);
        }
    }

    public override string GetDetails() => $"InitAnswer: OK={IsOk}, Mode={Mode}";
}
