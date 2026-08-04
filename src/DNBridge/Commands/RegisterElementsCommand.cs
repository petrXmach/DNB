using DNBridge.Tlv;

namespace DNBridge.Commands;

/// <summary>
/// Register elements command. Mirrors C++ cl_Reg_Elems_Cmd.
/// Contains a list of ElementStub describing elements to monitor/control.
/// </summary>
public class RegisterElementsCommand : DnbCommand
{
    public List<ElementStub> Elements { get; } = [];

    public override uint ObjectTag => TlvTags.TAG_CLASS_CMD_REG_ELEMS;

    public override void Serialize(TlvWriter writer)
    {
        base.Serialize(writer);
        foreach (var stub in Elements)
            writer.SerializeObject(stub);
    }

    public override bool Deserialize(uint tag, uint length, ReadOnlySpan<byte> value)
    {
        return base.Deserialize(tag, length, value);
    }

    public override bool ProcessSubObject(ITlvSerializable obj)
    {
        if (obj is ElementStub stub)
        {
            Elements.Add(stub);
            return true;
        }
        return false;
    }

    public override string GetSummary() => $"RegElems: {Elements.Count} elements";

    public override string GetDetails()
    {
        if (Elements.Count == 0)
            return "RegElems: 0 elements";

        var lines = new List<string>
        {
            $"RegElems: {Elements.Count} elements"
        };

        foreach (var elem in Elements)
        {
            var props = new[]
            {
                $"Id={elem.Id}",
                TrafficFormat.Address(elem.Address104),
                $"Ack{TrafficFormat.Address(elem.AckAddress)}",
                $"Type={elem.Iec104Type}",
                $"SetPoint={elem.IsSetPoint}",
                $"Propagate={elem.Propagate}"
            };
            lines.Add(string.Join("; ", props));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
