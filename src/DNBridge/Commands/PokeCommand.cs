using DNBridge.Tlv;

namespace DNBridge.Commands;

/// <summary>
/// Poke command — write values to SCADA. Mirrors C++ cl_Poke_Command.
/// Contains an element ID and a list of poke values to send.
/// </summary>
public class PokeCommand : DnbCommand
{
    public uint ElementId { get; set; }
    public List<PokeValue> Values { get; } = [];

    public override uint ObjectTag => TlvTags.TAG_CLASS_POKE_CMD;

    public override void Serialize(TlvWriter writer)
    {
        base.Serialize(writer);
        writer.AddU32(TlvTags.TAG_u32_Value, ElementId);
        foreach (var val in Values)
            writer.SerializeObject(val);
    }

    public override bool Deserialize(uint tag, uint length, ReadOnlySpan<byte> value)
    {
        if (tag == TlvTags.TAG_u32_Value)
        {
            ElementId = TlvReader.ReadU32(value);
            return true;
        }
        return base.Deserialize(tag, length, value);
    }

    public override bool ProcessSubObject(ITlvSerializable obj)
    {
        if (obj is PokeValue pokeValue)
        {
            Values.Add(pokeValue);
            return true;
        }
        return false;
    }

    public override string GetDetails()
    {
        string valuesDesc = Values.Count > 0
            ? string.Join(", ", Values.Select(v => v.GetDetails()))
            : "none";
        return $"Poke: ElemId=0x{ElementId:X8}, Values=[{valuesDesc}]";
    }
}
