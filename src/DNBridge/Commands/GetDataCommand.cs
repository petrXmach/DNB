using DNBridge.Tlv;

namespace DNBridge.Commands;

/// <summary>
/// Get data command. Mirrors C++ cl_Get_Data_Cmd.
/// Requests element values newer than a specified time, with pagination support.
/// </summary>
public class GetDataCommand : DnbCommand
{
    public DateTime NewerThan { get; set; }
    public bool Start { get; set; }

    public override uint ObjectTag => TlvTags.TAG_CLASS_CMD_GET_DATA;

    public override void Serialize(TlvWriter writer)
    {
        base.Serialize(writer);
        writer.AddDate(TlvTags.TAG_dt_DateTime, NewerThan);
        writer.AddBool(TlvTags.TAG_b_Value, Start);
    }

    public override bool Deserialize(uint tag, uint length, ReadOnlySpan<byte> value)
    {
        switch (tag)
        {
            case TlvTags.TAG_dt_DateTime:
                NewerThan = TlvReader.ReadDate(value);
                return true;
            case TlvTags.TAG_b_Value:
                Start = TlvReader.ReadBool(value);
                return true;
            default:
                return base.Deserialize(tag, length, value);
        }
    }

    public override string GetDetails() =>
        $"GetData: NewerThan={NewerThan:yyyy-MM-dd HH:mm:ss}, Start={Start}";
}
