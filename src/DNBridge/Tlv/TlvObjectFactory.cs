using DNBridge.Commands;

namespace DNBridge.Tlv;

/// <summary>
/// Global factory: creates ITlvSerializable objects by class tag.
/// Mirrors C++ IEC104_CreateObjectByTag from Commands.cpp.
/// No Replay classes (skipped per project rules).
/// </summary>
public static class TlvObjectFactory
{
    public static ITlvSerializable? CreateObjectByTag(uint tag) => tag switch
    {
        TlvTags.TAG_CLASS_COMMAND        => new DnbCommand(),
        TlvTags.TAG_CLASS_ANSW_ACK       => new AckAnswer(),
        TlvTags.TAG_CLASS_CMD_INIT       => new InitCommand(),
        TlvTags.TAG_CLASS_ANSW_INIT      => new InitAnswer(),
        TlvTags.TAG_CLASS_CMD_REG_ELEMS  => new RegisterElementsCommand(),
        TlvTags.TAG_CLASS_ANSW_REG_ELEMS => new RegisterElementsAnswer(),
        TlvTags.TAG_CLASS_CMD_GET_DATA   => new GetDataCommand(),
        TlvTags.TAG_CLASS_ANSW_DATA      => new GetDataAnswer(),
        TlvTags.TAG_CLASS_ELEM_STUB      => new ElementStub(),
        TlvTags.TAG_CLASS_VALUE          => new ElementValue(),
        TlvTags.TAG_CLASS_POKE_FLT       => new FloatPokeValue(),
        TlvTags.TAG_CLASS_POKE_BOOL      => new BoolPokeValue(),
        TlvTags.TAG_CLASS_POKE_4STATE    => new FourStatePokeValue(),
        TlvTags.TAG_CLASS_POKE_CMD       => new PokeCommand(),
        _ => null
    };
}
