using System.Buffers.Binary;
using System.Text;

namespace DNBridge.Tlv;

/// <summary>
/// Binary TLV writer. Mirrors the write side of C++ cl_Serializer.
/// All values are written in little-endian byte order.
/// </summary>
public class TlvWriter
{
    private readonly MemoryStream _stream;

    public TlvWriter(int initialCapacity = 4096)
    {
        _stream = new MemoryStream(initialCapacity);
    }

    /// <summary>
    /// Serialize a class object: write tag header (length=0), call obj.Serialize, patch length.
    /// Mirrors C++ cl_Serializer::Serialize (Serializable.cpp lines 217-228).
    /// </summary>
    public void SerializeObject(ITlvSerializable obj)
    {
        long offsetBefore = _stream.Position;
        WriteHeader(obj.ObjectTag, 0); // placeholder length
        obj.Serialize(this);
        uint payloadLength = (uint)(_stream.Position - offsetBefore - 8);
        PatchLength(offsetBefore + 4, payloadLength);
    }

    /// <summary>
    /// Wrap the serialized data in an outer envelope (tag=0, length=payload size).
    /// Mirrors C++ cl_Serializer::WriteTLVArchive (buffer variant, no compression).
    /// </summary>
    public byte[] ToEnvelope()
    {
        byte[] payload = _stream.ToArray();
        byte[] envelope = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(envelope.AsSpan(0), 0); // tag = 0
        BinaryPrimitives.WriteUInt32LittleEndian(envelope.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(envelope.AsSpan(8));
        return envelope;
    }

    public void AddBool(uint tag, bool value) => AddU8(tag, (byte)(value ? 1 : 0));

    public void AddU8(uint tag, byte value)
    {
        WriteHeader(tag, 1);
        _stream.WriteByte(value);
    }

    public void AddU16(uint tag, ushort value)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buf, value);
        WriteHeader(tag, 2);
        _stream.Write(buf);
    }

    public void AddI16(uint tag, short value)
    {
        Span<byte> buf = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(buf, value);
        WriteHeader(tag, 2);
        _stream.Write(buf);
    }

    public void AddU32(uint tag, uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
        WriteHeader(tag, 4);
        _stream.Write(buf);
    }

    public void AddI32(uint tag, int value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, value);
        WriteHeader(tag, 4);
        _stream.Write(buf);
    }

    public void AddU64(uint tag, ulong value)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(buf, value);
        WriteHeader(tag, 8);
        _stream.Write(buf);
    }

    public void AddDouble(uint tag, double value)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(buf, value);
        WriteHeader(tag, 8);
        _stream.Write(buf);
    }

    /// <summary>
    /// Write a UTF-8 string with null terminator (matching C++ AddTLV_UTF8).
    /// Length includes the null byte.
    /// </summary>
    public void AddUtf8(uint tag, string value)
    {
        byte[] utf8Bytes = Encoding.UTF8.GetBytes(value);
        uint length = (uint)(utf8Bytes.Length + 1); // +1 for null terminator
        WriteHeader(tag, length);
        _stream.Write(utf8Bytes);
        _stream.WriteByte(0); // null terminator
    }

    /// <summary>
    /// Write a DateTime as int64 milliseconds since Unix epoch (Jan 1, 1970 UTC).
    /// Matches C++ AddTLV_Date using wxDateTime::GetValue().
    /// </summary>
    public void AddDate(uint tag, DateTime value)
    {
        long milliseconds = new DateTimeOffset(value, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(buf, milliseconds);
        WriteHeader(tag, 8);
        _stream.Write(buf);
    }

    private void WriteHeader(uint tag, uint length)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(header, tag);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], length);
        _stream.Write(header);
    }

    private void PatchLength(long offset, uint length)
    {
        long savedPos = _stream.Position;
        _stream.Position = offset;
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, length);
        _stream.Write(buf);
        _stream.Position = savedPos;
    }
}
