# TLV Tag Reference (L2)

Source: `include/tlv_tag.h`. All values hexadecimal. LE = little-endian.
`TLV_CLASS = 0x80000000` — bit 31 marks class/container tags.

## Common Attribute Tags (0x00000001–0x000000FF)

### Identity & Position
| Tag | Value | Type | Purpose |
|-----|-------|------|---------|
| TAG_u32_ID | 0x01 | u32 | Element ID |
| TAG_sz8_Name | 0x02 | UTF8 | Element name |
| TAG_u32_Version | 0x03 | u32 | Scheme file version |
| TAG_sz8_Manufacturer | 0x04 | UTF8 | Manufacturer name |
| TAG_sz8_Type | 0x05 | UTF8 | Type designation |
| TAG_sz8_Font | 0x06 | UTF8 | Font name |
| TAG_dtm_Created | 0x07 | datetime | Creation timestamp |
| TAG_u32_Type | 0x08 | u32 | Type code |
| TAG_u32_Sel_ID | 0x09 | u32 | Selected ID |
| TAG_dbl_Freq | 0x0A | double | Frequency |
| TAG_Colour | 0x0B | color | Color (RGBA) |
| TAG_u64_ID | 0x0C | u64 | Extended ID |
| TAG_sz8_ID | 0x0D | UTF8 | String ID |
| TAG_u64_ExtID | 0x0E | u64 | External ID |
| TAG_u32_Index | 0x0F | u32 | Index |
| TAG_u32_Position_X | 0x10 | u32 | X position |
| TAG_u32_Position_Y | 0x11 | u32 | Y position |
| TAG_u32_Position_Z | 0x12 | u32 | Z-order |
| TAG_u32_Orietation | 0x17 | u32 | Orientation (0/90/180/270) |
| TAG_b_Visible | 0x18 | bool | Visibility |
| TAG_b_Selected | 0x45 | bool | Selection state |

### Electrical Parameters (shared across element types)
| Tag | Value | Type | Purpose |
|-----|-------|------|---------|
| TAG_dbl_Un | 0x20 | double | Nominal voltage (kV) |
| TAG_dbl_Imax | 0x22 | double | Max current (A) |
| TAG_dbl_U1 | 0x23 | double | Primary voltage |
| TAG_dbl_U2 | 0x24 | double | Secondary voltage |
| TAG_dbl_S | 0x26 | double | Apparent power (VA) |
| TAG_dbl_P | 0x27 | double | Active power (W) |
| TAG_dbl_Q | 0x28 | double | Reactive power (VAr) |
| TAG_dbl_I | 0x29 | double | Current (A) |
| TAG_dbl_cosPhi | 0x2A | double | Power factor |
| TAG_dbl_R0_R1 | 0x2D | double | R0/R1 ratio |
| TAG_dbl_X0_X1 | 0x2E | double | X0/X1 ratio |
| TAG_dbl_R_X | 0x2F | double | R/X ratio |
| TAG_dbl_Pk | 0x31 | double | Short-circuit losses (W) |
| TAG_dbl_uk | 0x32 | double | Short-circuit voltage (%) |
| TAG_dbl_SpecR | 0x33 | double | Specific resistance (Ω/km) |
| TAG_dbl_SpecX | 0x34 | double | Specific reactance (Ω/km) |
| TAG_dbl_SpecB | 0x35 | double | Specific susceptance (S/km) |
| TAG_dbl_Po | 0x37 | double | No-load losses (W) |
| TAG_dbl_Sn | 0x49 | double | Nominal apparent power |
| TAG_dbl_Pn | 0x4A | double | Nominal active power |
| TAG_dbl_In | 0x4B | double | Nominal current |
| TAG_dbl_cosPhin | 0x4C | double | Nominal power factor |
| TAG_u32_Terminal | 0x4D | u32 | Terminal index |
| TAG_dbl_Length | 0x40 | double | Line length (km) |
| TAG_b_State | 0x41 | bool | Switch state (open/closed) |
| TAG_3Ph_Connection | 0x8F | u32 | 3-phase connection type (D/Y/YN/NA) |

## Element Class Tags (bit 31 set)

| Tag | Value | Element Type |
|-----|-------|-------------|
| TAG_CLASS_SCHEME | 0x80000100 | Scheme container |
| TAG_CLASS_TERM_CONN_HLP | 0x80000280 | Terminal connection helper |
| TAG_CLASS_POINT_CONN_HLP | 0x80000290 | Point connection helper |
| TAG_CLASS_NODE | 0x80001000 | Node/bus |
| TAG_CLASS_POWER | 0x80001100 | Power source |
| TAG_CLASS_LINE | 0x80001200 | Line |
| TAG_CLASS_XFORMER | 0x80001300 | Transformer (2W) |
| TAG_CLASS_SWITCH | 0x80001400 | Switch |
| TAG_CLASS_LOAD | 0x80001500 | Load |
| TAG_CLASS_ASYNC | 0x80001600 | Async machine |
| TAG_CLASS_SYNC | 0x80001700 | Sync machine |
| TAG_CLASS_CURR_SRC | 0x80001800 | Current source |
| TAG_CLASS_PHOTOVOLT | 0x80001900 | Photovoltaic |
| TAG_CLASS_GATE | 0x80001A00 | Gate/capacitor bank |
| TAG_CLASS_CHOKE | 0x80001B00 | Choke |
| TAG_CLASS_REACTOR | 0x80001C00 | Reactor |
| TAG_CLASS_XFORMER3 | 0x80001D00 | Transformer (3W) |
| TAG_CLASS_TEXT | 0x80001E00 | Text annotation |
| TAG_CLASS_HDO_SRC | 0x80001F00 | HDO source |
| TAG_CLASS_CLIPBOARD | 0x80002000 | Clipboard |
| TAG_CLASS_FUSE_RACK | 0x80002100 | Fuse rack |
| TAG_CLASS_ACCU | 0x80002200 | Accumulation/battery |
| TAG_CLASS_CONFIG | 0x80002F00 | Configuration block |
| TAG_CLASS_CALC_TEST | 0x80003000 | Calculation test |

## Result Class Tags

| Tag | Value | Result Type |
|-----|-------|-------------|
| TAG_CLASS_OPER_RESULT | 0x80003100 | Operational result container |
| TAG_CLASS_NODE_OPER_RESULT | 0x80003101 | Node result (1-phase) |
| TAG_CLASS_NODE_OPER_RESULT3 | 0x80003102 | Node result (3-phase) |
| TAG_CLASS_ELEM_OPER_RESULT | 0x80003103 | Element result (1-phase) |
| TAG_CLASS_ELEM_OPER_RESULT3 | 0x80003104 | Element result (3-phase) |
| TAG_CLASS_SHORTCIRC_RESULT | 0x80003200 | Short-circuit result |
| TAG_CLASS_FREQ_CHAR_RESULT | 0x80003300 | Frequency char result |
| TAG_CLASS_HARM_AN_RESULT | 0x80003400 | Harmonic analysis result |
| TAG_CLASS_HDO_RESULT | 0x80005000 | HDO result |
| TAG_CLASS_FLIKR_RESULT | 0x80005100 | Flicker result |
| TAG_CLASS_LOADC_RESULT | 0x80005400 | Load connection result |
| TAG_CLASS_PQ_Diagram | 0x80007000 | PQ diagram |

## Per-Element Specific Tags

Each element type has its own tag range (element class + offset):
- **Node**: 0x1001–0x1008 (terminal count, orientation, grounding, measured voltage)
- **Power**: 0x1101–0x1110 (Izkr, Szkr, R0/R1, X0/X1, neutral impedance, 3ph voltages)
- **Line**: 0x1201–0x1206 (subterranean, kind, forest length, LC entry modes)
- **Transformer**: 0x1301–0x1318 (regulation, branches, winding types, neutral impedance)
- **Switch**: 0x1401–0x1403 (switch type, time manipulation, protection flag)
- **Load**: 0x1501–0x1510 (input type, flicker params, asymmetry, constant impedance)
- **Async**: 0x1601–0x1610 (async type, stator, flicker, converter, startup, SK contribution)
- **Sync**: 0x1701–0x1720 (sync type, Xd/X'd/X''d, Q limits, P limits, power block, Tm)
- **CurrSrc**: 0x1801–0x1839 (harmonic currents I1–I50, type, subtype)
- **PhotoVolt**: 0x1901–0x1920 (Sn entry, regulation type, Q(U) curve points)
- **Gate**: 0x1A01–0x1A03 (Qk, detuning, KB mode)
- **Choke**: 0x1B01–0x1B07 (Petersen, Uln, Q entry, R/X, additional R)
- **Reactor**: 0x1C01–0x1C02 (Unr, Inr)
- **Transformer3**: 0x1D01–0x1D0E (pairwise Sn/Pk/Uk, G0/G1, B0/B1)
- **HDO_Src**: 0x1F01 (HDO voltage)
- **FuseRack**: 0x2120–0x2125 (fuse states 1–6)
- **Accumulation**: 0x2201–0x2202 (MiCoGe type/kind)

## Protection Tags (0x8000–0x811F)
Used when `LIM_PROTECTION` is defined. Tags for overcurrent protection parameters (Ir, In, timing, voltage sensing, etc.)

## Note
When `_VOLTAGE_CTRL_` is defined, additional tags are included from `DN_Cors_tag.h` (IEC 104 / SCADA-specific tags).
When `_CLIENT_` is defined, additional tags from `Auth_tlv_tag.h` (authentication).
