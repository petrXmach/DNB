namespace DNBridge.Tlv;

/// <summary>
/// Interface for TLV-serializable objects.
/// Mirrors C++ cl_SerializableObject from Serializable.h.
/// </summary>
public interface ITlvSerializable
{
    /// <summary>
    /// The class tag identifying this object type (e.g., TAG_CLASS_CMD_INIT).
    /// Used as the TLV header tag during serialization.
    /// </summary>
    uint ObjectTag { get; }

    /// <summary>
    /// Serialize this object's attributes into the writer.
    /// Called between the class tag header write and the length patch.
    /// </summary>
    void Serialize(TlvWriter writer);

    /// <summary>
    /// Deserialize a single attribute TLV record into this object.
    /// Called for each non-class tag encountered within this object's TLV block.
    /// </summary>
    /// <returns>true if the tag was recognized and handled.</returns>
    bool Deserialize(uint tag, uint length, ReadOnlySpan<byte> value);

    /// <summary>
    /// Accept a deserialized child class object.
    /// Called after a nested class TLV is fully deserialized.
    /// </summary>
    /// <returns>true if the object was accepted (ownership transferred).</returns>
    bool ProcessSubObject(ITlvSerializable obj) => false;

    /// <summary>
    /// Factory method for creating child objects by class tag.
    /// Override to handle type-specific children (e.g., ElementStub inside RegisterElementsCommand).
    /// Falls through to TlvObjectFactory if not overridden.
    /// </summary>
    ITlvSerializable? CreateSubObjectByTag(uint tag) => TlvObjectFactory.CreateObjectByTag(tag);
}
