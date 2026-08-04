using DNBridge.Tlv;

namespace DNBridge.Commands;

/// <summary>
/// Data answer containing element values. Mirrors C++ cl_Data_Answer.
/// Supports pagination: IsFinal=false means more data is available.
/// </summary>
public class GetDataAnswer : DnbCommand
{
    public const int MaxRecordsPerAnswer = 30;

    public List<ElementValue> Values { get; } = [];
    public bool IsFinal { get; set; }

    public override uint ObjectTag => TlvTags.TAG_CLASS_ANSW_DATA;

    public override void Serialize(TlvWriter writer)
    {
        base.Serialize(writer);
        writer.AddBool(TlvTags.TAG_b_Value, IsFinal);
        foreach (var val in Values)
            writer.SerializeObject(val);
    }

    public override bool Deserialize(uint tag, uint length, ReadOnlySpan<byte> value)
    {
        if (tag == TlvTags.TAG_b_Value)
        {
            IsFinal = TlvReader.ReadBool(value);
            return true;
        }
        return base.Deserialize(tag, length, value);
    }

    public override bool ProcessSubObject(ITlvSerializable obj)
    {
        if (obj is ElementValue val)
        {
            Values.Add(val);
            return true;
        }
        return false;
    }

    public override string GetSummary() =>
        $"GetDataAnswer: {Values.Count} values, Final={IsFinal}";

    public override string GetDetails()
    {
        if (Values.Count == 0)
            return GetSummary();

        var lines = new List<string> { GetSummary() };
        foreach (var v in Values)
        {
            lines.Add(TrafficFormat.DetailLine(
                (uint)(v.Address & 0xFFFFFFUL), v.Value, v.Quality, v.DateTime));
        }
        return string.Join(Environment.NewLine, lines);
    }
}
