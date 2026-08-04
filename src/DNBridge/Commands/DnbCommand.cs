using DNBridge.Tlv;

namespace DNBridge.Commands;

/// <summary>
/// Base command class. Mirrors C++ cl_Command.
/// All DNC protocol commands inherit from this.
/// </summary>
public class DnbCommand : ITlvSerializable
{
    public uint SessionId { get; set; }
    public DnbCommand? Answer { get; set; }

    public virtual uint ObjectTag => TlvTags.TAG_CLASS_COMMAND;

    public virtual void Serialize(TlvWriter writer)
    {
        writer.AddU32(TlvTags.TAG_u32_Client_ID, SessionId);
    }

    public virtual bool Deserialize(uint tag, uint length, ReadOnlySpan<byte> value)
    {
        switch (tag)
        {
            case TlvTags.TAG_u32_Client_ID:
                SessionId = TlvReader.ReadU32(value);
                return true;
            default:
                return false;
        }
    }

    public virtual bool ProcessSubObject(ITlvSerializable obj) => false;

    public virtual ITlvSerializable? CreateSubObjectByTag(uint tag) =>
        TlvObjectFactory.CreateObjectByTag(tag);

    /// <summary>
    /// One-line human-readable summary for the master traffic list.
    /// Defaults to the full details; commands with multi-line details (e.g.
    /// RegisterElements, GetDataAnswer) override this with a short header.
    /// </summary>
    public virtual string GetSummary() => GetDetails();

    /// <summary>
    /// Full human-readable description of this command for the detail pane.
    /// SessionId is intentionally omitted — DNC never initializes it (it carries
    /// uninitialized memory on the wire), so it is meaningless for display.
    /// </summary>
    public virtual string GetDetails() => string.Empty;
}
