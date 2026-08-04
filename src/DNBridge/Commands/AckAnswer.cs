using DNBridge.Tlv;

namespace DNBridge.Commands;

/// <summary>
/// Generic acknowledgement answer. Mirrors C++ cl_Ack_Answ.
/// </summary>
public class AckAnswer : DnbCommand
{
    public override uint ObjectTag => TlvTags.TAG_CLASS_ANSW_ACK;

    public override string GetDetails() => "ACK";
}
