using DNBridge.Tlv;

namespace DNBridge.Commands;

/// <summary>
/// Element registration DTO. Mirrors C++ cl_Elem_Stub.
/// Used within RegisterElementsCommand to describe elements to register.
/// </summary>
public class ElementStub : ITlvSerializable
{
    /// <summary>MAIN_104_Flag used to distinguish control elements during RegisterElements.</summary>
    public const uint Main104Flag = 0xC0000000;

    public uint Id { get; set; }
    public ulong Address104 { get; set; }
    public ulong AckAddress { get; set; }
    public byte Iec104Type { get; set; }
    public bool IsSetPoint { get; set; }
    public bool Propagate { get; set; }

    public uint ObjectTag => TlvTags.TAG_CLASS_ELEM_STUB;

    public void Serialize(TlvWriter writer)
    {
        writer.AddU32(TlvTags.TAG_u32_ID, Id);
        writer.AddU64(TlvTags.TAG_u64_104ADDR, Address104);
        writer.AddU64(TlvTags.TAG_u64_ACK_104ADDR, AckAddress);
        writer.AddBool(TlvTags.TAG_b_SetPoint, IsSetPoint);
        writer.AddBool(TlvTags.TAG_b_Propagate, Propagate);
        writer.AddU8(TlvTags.TAG_u8_104TYPE, Iec104Type);
    }

    public bool Deserialize(uint tag, uint length, ReadOnlySpan<byte> value)
    {
        switch (tag)
        {
            case TlvTags.TAG_u32_ID:
                Id = TlvReader.ReadU32(value);
                return true;
            case TlvTags.TAG_u64_104ADDR:
                Address104 = TlvReader.ReadU64(value);
                return true;
            case TlvTags.TAG_u64_ACK_104ADDR:
                AckAddress = TlvReader.ReadU64(value);
                return true;
            case TlvTags.TAG_b_SetPoint:
                IsSetPoint = TlvReader.ReadBool(value);
                return true;
            case TlvTags.TAG_b_Propagate:
                Propagate = TlvReader.ReadBool(value);
                return true;
            case TlvTags.TAG_u8_104TYPE:
                Iec104Type = TlvReader.ReadU8(value);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Format address as "CCC.CCC.III.III.III" matching C++ cl_Elem_Stub::AddrToStr.
    /// This is the CONFIG/wire form (as written in XChng.cfg) and the inverse of
    /// <see cref="StrToAddr"/> — logs and traffic views must not use it: they show the
    /// decimal IOA via <see cref="TrafficFormat.Address"/>.
    /// </summary>
    public static string AddrToStr(ulong address) =>
        $"{(byte)((address >> 32) & 0xFF):D3}." +
        $"{(byte)((address >> 24) & 0xFF):D3}." +
        $"{(byte)((address >> 16) & 0xFF):D3}." +
        $"{(byte)((address >> 8) & 0xFF):D3}." +
        $"{(byte)(address & 0xFF):D3}";

    /// <summary>
    /// Parse "CCC.CCC.III.III.III" address string into a ulong.
    /// Inverse of AddrToStr(). Mirrors C++ cl_104_Element::StrToAddr().
    /// </summary>
    public static bool TryParseAddress(string addr, out ulong result)
    {
        result = 0;
        var parts = addr.Split('.');
        if (parts.Length != 5)
            return false;

        var bytes = new byte[5];
        for (int i = 0; i < 5; i++)
        {
            if (!byte.TryParse(parts[i], out bytes[i]))
                return false;
        }

        result = ((ulong)bytes[0] << 32)
               | ((ulong)bytes[1] << 24)
               | ((ulong)bytes[2] << 16)
               | ((ulong)bytes[3] << 8)
               | bytes[4];
        return true;
    }
}
