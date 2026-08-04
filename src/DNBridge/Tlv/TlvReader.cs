using System.Buffers.Binary;
using System.Text;

namespace DNBridge.Tlv;

/// <summary>
/// Binary TLV reader with recursive deserialization.
/// Mirrors the read side of C++ cl_Serializer (Serializable.cpp).
/// </summary>
public class TlvReader
{
    private readonly byte[] _data;
    private int _offset;

    private TlvReader(byte[] data)
    {
        _data = data;
        _offset = 0;
    }

    /// <summary>
    /// Deserialize a TLV payload into an object.
    /// The payload starts at the inner command class tag (outer envelope already stripped).
    /// Mirrors C++ cl_Serializer::Deserialize (Serializable.cpp lines 230-259).
    /// </summary>
    public static ITlvSerializable? Deserialize(byte[] payload)
    {
        var reader = new TlvReader(payload);

        if (!reader.TryReadHeader(out uint tag, out uint length))
            return null;

        var root = TlvObjectFactory.CreateObjectByTag(tag);
        if (root == null)
            return null;

        reader.DeserializeRecursive(root, reader._offset + (int)length);
        return root;
    }

    /// <summary>
    /// Recursively process nested TLV records within a class object.
    /// Mirrors C++ cl_Serializer::DeserializeClassRecursive (Serializable.cpp lines 261-289).
    /// </summary>
    private void DeserializeRecursive(ITlvSerializable obj, int endOffset)
    {
        while (_offset < endOffset && TryReadHeader(out uint tag, out uint length))
        {
            if (TlvTags.IsClassTag(tag))
            {
                var sub = obj.CreateSubObjectByTag(tag);
                if (sub != null)
                {
                    int subEnd = _offset + (int)length;
                    DeserializeRecursive(sub, subEnd);
                    obj.ProcessSubObject(sub);
                }
                else
                {
                    // Unknown class tag — skip entire block
                    _offset += (int)length;
                }
            }
            else
            {
                // Attribute tag — pass value bytes to the object
                var valueSpan = new ReadOnlySpan<byte>(_data, _offset, (int)length);
                obj.Deserialize(tag, length, valueSpan);
                _offset += (int)length;
            }
        }
    }

    private bool TryReadHeader(out uint tag, out uint length)
    {
        if (_offset + 8 > _data.Length)
        {
            tag = 0;
            length = 0;
            return false;
        }

        tag = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_offset, 4));
        length = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_offset + 4, 4));
        _offset += 8;
        return true;
    }

    // --- Static read helpers (matching C++ GetTLV_* methods) ---

    public static bool ReadBool(ReadOnlySpan<byte> data) => data[0] != 0;

    public static byte ReadU8(ReadOnlySpan<byte> data) => data[0];

    public static ushort ReadU16(ReadOnlySpan<byte> data) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data);

    public static short ReadI16(ReadOnlySpan<byte> data) =>
        BinaryPrimitives.ReadInt16LittleEndian(data);

    public static uint ReadU32(ReadOnlySpan<byte> data) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data);

    public static int ReadI32(ReadOnlySpan<byte> data) =>
        BinaryPrimitives.ReadInt32LittleEndian(data);

    public static ulong ReadU64(ReadOnlySpan<byte> data) =>
        BinaryPrimitives.ReadUInt64LittleEndian(data);

    public static double ReadDouble(ReadOnlySpan<byte> data) =>
        BinaryPrimitives.ReadDoubleLittleEndian(data);

    /// <summary>
    /// Read a null-terminated UTF-8 string. Length includes the null byte.
    /// Mirrors C++ GetTLV_UTF8 (Serializable.cpp lines 207-210).
    /// </summary>
    public static string ReadUtf8(ReadOnlySpan<byte> data)
    {
        // Strip the null terminator; C++ uses nLength - 1
        int strLen = data.Length > 0 ? data.Length - 1 : 0;
        if (strLen <= 0)
            return string.Empty;
        return Encoding.UTF8.GetString(data[..strLen]);
    }

    /// <summary>
    /// Read a DateTime from int64 milliseconds since Unix epoch (Jan 1, 1970 UTC).
    /// Mirrors C++ GetTLV_Date using wxDateTime(wxLongLong(val)).
    /// </summary>
    public static DateTime ReadDate(ReadOnlySpan<byte> data)
    {
        long milliseconds = BinaryPrimitives.ReadInt64LittleEndian(data);
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
    }
}
