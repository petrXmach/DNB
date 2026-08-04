using DNBridge.Tlv;

namespace DNBridge.Commands;

/// <summary>
/// Init command sent by DNC after connecting. Mirrors C++ cl_Init_Cmd.
/// Contains the SCADA server name to connect to.
/// </summary>
public class InitCommand : DnbCommand
{
    public string ServerName { get; set; } = string.Empty;

    public override uint ObjectTag => TlvTags.TAG_CLASS_CMD_INIT;

    public override void Serialize(TlvWriter writer)
    {
        base.Serialize(writer);
        writer.AddUtf8(TlvTags.TAG_sz8_Server, ServerName);
    }

    public override bool Deserialize(uint tag, uint length, ReadOnlySpan<byte> value)
    {
        if (tag == TlvTags.TAG_sz8_Server)
        {
            ServerName = TlvReader.ReadUtf8(value);
            return true;
        }
        return base.Deserialize(tag, length, value);
    }

    public override string GetDetails() => $"Init: Server=\"{ServerName}\"";
}
