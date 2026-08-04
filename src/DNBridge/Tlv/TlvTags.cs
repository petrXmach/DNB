namespace DNBridge.Tlv;

/// <summary>
/// TLV tag constants and name lookup for the DNC protocol.
/// Mirrors C++ definitions from DN_Cors_tag.h and tlv_tag.h.
/// </summary>
public static class TlvTags
{
    private const uint TLV_CLASS = 0x80000000;

    // Class tags (nested TLV containers — bit 31 set)
    public const uint TAG_CLASS_COMMAND        = TLV_CLASS | 0x00101000;
    public const uint TAG_CLASS_ANSW_ACK       = TLV_CLASS | 0x00101002;
    public const uint TAG_CLASS_CMD_QUIT       = TLV_CLASS | 0x00101004;
    public const uint TAG_CLASS_CMD_QUIT_ACK   = TLV_CLASS | 0x00101005;
    public const uint TAG_CLASS_CMD_INIT       = TLV_CLASS | 0x00101010;
    public const uint TAG_CLASS_ANSW_INIT      = TLV_CLASS | 0x00101011;
    public const uint TAG_CLASS_CMD_REG_ELEMS  = TLV_CLASS | 0x00101012;
    public const uint TAG_CLASS_ANSW_REG_ELEMS = TLV_CLASS | 0x00101013;
    public const uint TAG_CLASS_CMD_GET_DATA   = TLV_CLASS | 0x00101014;
    public const uint TAG_CLASS_ANSW_DATA      = TLV_CLASS | 0x00101015;
    public const uint TAG_CLASS_REPLAY_CMD     = TLV_CLASS | 0x00101016;
    public const uint TAG_CLASS_REPLAY_ANSW    = TLV_CLASS | 0x00101017;
    public const uint TAG_CLASS_POKE_CMD       = TLV_CLASS | 0x00101018;
    public const uint TAG_CLASS_ELEM_STUB      = TLV_CLASS | 0x00102000;
    public const uint TAG_CLASS_VALUE_SW       = TLV_CLASS | 0x00102001;
    public const uint TAG_CLASS_VALUE_GEN      = TLV_CLASS | 0x00102002;
    public const uint TAG_CLASS_VALUE_MEAS     = TLV_CLASS | 0x00102003;
    public const uint TAG_CLASS_VALUE_NODE     = TLV_CLASS | 0x00102004;
    public const uint TAG_CLASS_VALUE_TRANSF   = TLV_CLASS | 0x00102005;
    public const uint TAG_CLASS_VALUE          = TLV_CLASS | 0x00102006;
    public const uint TAG_CLASS_POKE_FLT       = TLV_CLASS | 0x00102010;
    public const uint TAG_CLASS_POKE_BOOL      = TLV_CLASS | 0x00102011;
    public const uint TAG_CLASS_POKE_4STATE    = TLV_CLASS | 0x00102012;

    // Attribute tags (leaf values)
    public const uint TAG_u32_ID          = 0x00000001;
    public const uint TAG_sz8_Name        = 0x00000002;
    public const uint TAG_u32_Client_ID   = 0x00110001;
    public const uint TAG_sz8_IP          = 0x00110003;
    public const uint TAG_u16_Port        = 0x00110004;
    public const uint TAG_sz8_Server      = 0x00110005;
    public const uint TAG_b_OK            = 0x00110006;
    public const uint TAG_dt_DateTime     = 0x00110008;
    public const uint TAG_b_Value         = 0x00110009;
    public const uint TAG_dbl_Value_U     = 0x0011000A;
    public const uint TAG_dbl_Value_P     = 0x0011000B;
    public const uint TAG_dbl_Value_Q     = 0x0011000C;
    public const uint TAG_u8_Mode         = 0x0011000D;
    public const uint TAG_u32_Value       = 0x0011000E;
    public const uint TAG_dbl_Value       = 0x00110010; // collision with TAG_dt_From (Replay only)
    public const uint TAG_dt_From         = 0x00110010; // only used in Replay (skipped)
    public const uint TAG_dt_To           = 0x00110011;
    public const uint TAG_u64_104ADDR     = 0x00110012;
    public const uint TAG_b_SetPoint      = 0x00110013;
    public const uint TAG_u32_Quality     = 0x00110014;
    public const uint TAG_b_Propagate     = 0x00110015;
    public const uint TAG_u64_ACK_104ADDR = 0x00110016;
    public const uint TAG_u8_COT          = 0x00110017;
    public const uint TAG_u8_104TYPE      = 0x00110018;
    public const uint TAG_u8_Value        = 0x00110019;

    private static readonly Dictionary<uint, string> Names = new()
    {
        [TAG_CLASS_COMMAND]        = "COMMAND",
        [TAG_CLASS_ANSW_ACK]       = "ANSW_ACK",
        [TAG_CLASS_CMD_QUIT]       = "CMD_QUIT",
        [TAG_CLASS_CMD_QUIT_ACK]   = "CMD_QUIT_ACK",
        [TAG_CLASS_CMD_INIT]       = "CMD_INIT",
        [TAG_CLASS_ANSW_INIT]      = "ANSW_INIT",
        [TAG_CLASS_CMD_REG_ELEMS]  = "CMD_REG_ELEMS",
        [TAG_CLASS_ANSW_REG_ELEMS] = "ANSW_REG_ELEMS",
        [TAG_CLASS_CMD_GET_DATA]   = "CMD_GET_DATA",
        [TAG_CLASS_ANSW_DATA]      = "ANSW_DATA",
        [TAG_CLASS_REPLAY_CMD]     = "REPLAY_CMD",
        [TAG_CLASS_REPLAY_ANSW]    = "REPLAY_ANSW",
        [TAG_CLASS_POKE_CMD]       = "POKE_CMD",
        [TAG_CLASS_ELEM_STUB]      = "ELEM_STUB",
        [TAG_CLASS_VALUE]          = "VALUE",
        [TAG_CLASS_POKE_FLT]       = "POKE_FLT",
        [TAG_CLASS_POKE_BOOL]      = "POKE_BOOL",
        [TAG_CLASS_POKE_4STATE]    = "POKE_4STATE",
    };

    public static string GetName(uint tag) =>
        Names.TryGetValue(tag, out var name) ? name : $"0x{tag:X8}";

    public static bool IsClassTag(uint tag) => (tag & TLV_CLASS) != 0;
}
