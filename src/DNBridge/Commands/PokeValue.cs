using DNBridge.Tlv;

namespace DNBridge.Commands;

/// <summary>
/// Abstract base for poke value types. Mirrors C++ cl_Poke_Value.
/// Concrete types: BoolPokeValue, FourStatePokeValue, FloatPokeValue.
/// </summary>
public abstract class PokeValue : ITlvSerializable
{
    public abstract uint ObjectTag { get; }
    public abstract void Serialize(TlvWriter writer);
    public abstract bool Deserialize(uint tag, uint length, ReadOnlySpan<byte> value);
    public virtual bool ProcessSubObject(ITlvSerializable obj) => false;
    public virtual ITlvSerializable? CreateSubObjectByTag(uint tag) => null;

    /// <summary>Human-readable description for UI display.</summary>
    public abstract string GetDetails();
}
