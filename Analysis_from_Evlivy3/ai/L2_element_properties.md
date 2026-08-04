# Element Properties — Detailed Reference (L2)

Full property lists for 7 core element types. Brief listings for remaining 11 types.

## Core Types — Full Detail

### cl_Node (TAG_CLASS_NODE = 0x80001000)
**Inherits**: cl_Scheme_Element → cl_SerializableObject
**Terminals**: 0 (acts as connection hub, manages TermElemMap_T)

| Property | Type | Tag | Default | Description |
|----------|------|-----|---------|-------------|
| m_fUn | double | TAG_dbl_Un (0x20) | 0.4 | Nominal voltage (kV) |
| m_bGND | bool | TAG_b_GND (0x1006) | false | Grounded node |
| m_b4Wire | bool | — | false | 4-wire connection |
| m_fRn | double | TAG_dbl_Pwr_Rn (0x110D) | 0 | Neutral resistance (Ω) |
| m_fXn | double | TAG_dbl_Pwr_Xn (0x110E) | 0 | Neutral reactance (Ω) |
| m_fUmeas | double | TAG_dbl_Umeas (0x1007) | 0 | Measured voltage (kV) |
| m_bUmeas | bool | TAG_b_Umeas (0x1008) | false | Use measured voltage |
| m_nTermNum | u32 | TAG_u32_TermNum (0x1001) | 3 | Max terminal count |
| m_nDontCheckdU | bool | TAG_b_DontCheck_dU (0x1003) | false | Skip voltage check |

### cl_Line_Element (TAG_CLASS_LINE = 0x80001200)
**Inherits**: cl_MultiTerm_Element → cl_Term_Element → cl_Scheme_Element
**Terminals**: 2

| Property | Type | Tag | Default | Description |
|----------|------|-----|---------|-------------|
| m_fUn | double | TAG_dbl_Un (0x20) | 0.4 | Nominal voltage (kV) |
| m_fImax | double | TAG_dbl_Imax (0x22) | 0 | Max current (A) |
| m_fLength | double | TAG_dbl_Length (0x40) | 0 | Length (km) |
| m_fR | double | TAG_dbl_SpecR (0x33) | 0 | Specific R (Ω/km) |
| m_fX | double | TAG_dbl_SpecX (0x34) | 0 | Specific X (Ω/km) |
| m_fB | double | TAG_dbl_SpecB (0x35) | 0 | Specific B (S/km) |
| m_fR0_R1 | double | TAG_dbl_R0_R1 (0x2D) | 1 | R0/R1 ratio |
| m_fX0_X1 | double | TAG_dbl_X0_X1 (0x2E) | 1 | X0/X1 ratio |
| m_nParallelCnt | u32 | TAG_u32_Count (0x1C) | 1 | Parallel line count |
| m_eKind | LineKind_T | TAG_LINE_Kind (0x1204) | lt_Outdoor | Kind (outdoor/cable) |
| m_fForestLen | double | TAG_dbl_Rel_ForestLen (0x1203) | 0 | Forest section length |
| m_nDB_ID | u32 | TAG_u32_DB_ID (0x46) | 0 | Database catalog ID |

**Enums**: `LineKind_T` { lt_Outdoor, lt_Cable, lt_NA }
**Reliability**: inherits from cl_Term_Element (SAIFI/SAIDI parameters)

### cl_Transformer_Element (TAG_CLASS_XFORMER = 0x80001300)
**Inherits**: cl_MultiTerm_Element → cl_Term_Element → cl_Scheme_Element
**Terminals**: 2

| Property | Type | Tag | Default | Description |
|----------|------|-----|---------|-------------|
| m_fU1 | double | TAG_dbl_U1 (0x23) | 22 | Primary voltage (kV) |
| m_fU2 | double | TAG_dbl_U2 (0x24) | 0.4 | Secondary voltage (kV) |
| m_fSn | double | TAG_dbl_Sn (0x49) | 0 | Nominal power (VA) |
| m_fPk | double | TAG_dbl_Pk (0x31) | 0 | Short-circuit losses (W) |
| m_fUk | double | TAG_dbl_uk (0x32) | 0 | Short-circuit voltage (%) |
| m_fPo | double | TAG_dbl_Po (0x37) | 0 | No-load losses (W) |
| m_fio | double | TAG_dbl_io (0x38) | 0 | No-load current (%) |
| m_fR_X | double | TAG_dbl_R_X (0x2F) | 0 | R/X ratio |
| m_fR0_R1 | double | TAG_dbl_R0_R1 (0x2D) | 1 | R0/R1 ratio |
| m_fX0_X1 | double | TAG_dbl_X0_X1 (0x2E) | 1 | X0/X1 ratio |
| m_ePrimWindg | WindingType | TAG_PrimWindg (0x1306) | — | Primary winding (D/Y/YN/ZN) |
| m_eSecWindg | WindingType | TAG_SecWindg (0x1307) | — | Secondary winding |
| m_bBranchReg | bool | TAG_b_BranchRegulation (0x1302) | false | Branch regulation enabled |
| m_nBranches | u16 | TAG_u16_Branches (0x1303) | 0 | Number of tap steps |
| m_fBranchStep | double | TAG_dbl_BranchStep (0x1304) | 0 | Step size (%) |
| m_nActBranch | i16 | TAG_i16_ActBranch (0x130F) | 0 | Current tap position |
| m_bAutoReg | bool | TAG_b_AutoRegulation (0x1313) | false | Auto regulation |
| m_fTargetVolt | double | TAG_dbl_TargetVolt (0x1314) | 1 | Target voltage (p.u.) |
| m_bBlockTransf | bool | TAG_b_BlockTransf (0x1310) | false | Block transformer mode |
| m_fRn1/Xn1 | double | TAG_dbl_Rn1/Xn1 (0x1309/08) | 0 | Primary neutral Z |
| m_fRn2/Xn2 | double | TAG_dbl_Rn2/Xn2 (0x130B/0A) | 0 | Secondary neutral Z |

**Enums**: `WindingType` { D, Y, YN, ZN }

### cl_Switch_Element (TAG_CLASS_SWITCH = 0x80001400)
**Inherits**: cl_MultiTerm_Element → cl_Term_Element → cl_Scheme_Element
**Terminals**: 2

| Property | Type | Tag | Default | Description |
|----------|------|-----|---------|-------------|
| m_bState | bool | TAG_b_State (0x41) | true | Closed (true) / Open (false) |
| m_eSwitchType | SwitchType_T | TAG_SwitchType (0x1401) | — | Type (arbitrary/remote/recloser) |
| m_nTimeManip | u16 | TAG_u16_TimeManip (0x1402) | 0 | Time manipulation (min) |
| m_bProtection | bool | TAG_b_Protection (0x1403) | false | Has protection set |

**Enums**: `SwitchType_T` { sw_Arbitrary, sw_RemoteCtrl, sw_Recloser }

### cl_Load_Element (TAG_CLASS_LOAD = 0x80001500)
**Inherits**: cl_Deviation_Element → cl_Term_Element → cl_Scheme_Element
**Terminals**: 1

| Property | Type | Tag | Default | Description |
|----------|------|-----|---------|-------------|
| m_fUn | double | TAG_dbl_Un (0x20) | 0.4 | Nominal voltage (kV) |
| m_fS | double | TAG_dbl_S (0x26) | 0 | Apparent power (VA) |
| m_fP | double | TAG_dbl_P (0x27) | 0 | Active power (W) |
| m_fQ | double | TAG_dbl_Q (0x28) | 0 | Reactive power (VAr) |
| m_fI | double | TAG_dbl_I (0x29) | 0 | Current (A) |
| m_fCosPhi | double | TAG_dbl_cosPhi (0x2A) | 1 | Power factor |
| m_eInpType | LoadInpType | TAG_u16_InpType (0x1501) | USPhi | Input mode |
| m_bFlikr | bool | TAG_b_Flikr (0x1502) | false | Flicker enabled |
| m_eFlikrType | FlikrType | TAG_u16_Flikr_Type (0x1503) | — | Flicker type |
| m_bAsymLoad | bool | TAG_b_AsymLoad (0x1509) | false | Asymmetric load |
| m_eAsymType | AsymType | TAG_Asym_Type (0x150A) | — | Asymmetry type |
| m_fAsymPower | double | TAG_dbl_Asym_Power (0x150B) | 0 | Asymmetric power |
| m_bConstImp | bool | TAG_b_ConstImp (0x150D) | false | Constant impedance |
| m_eLoadKind | LoadKind | TAG_Load_Kind (0x150C) | — | Load kind |

**Enums**: `LoadInpType` { USPhi, UPQ, UIP, UIPhi, UPPhi }, `AsymType` { InterPhase, TwoPhase, OnePhase }

### cl_Power_Element (TAG_CLASS_POWER = 0x80001100)
**Inherits**: cl_Deviation_Element → cl_Term_Element → cl_Scheme_Element
**Terminals**: 1

| Property | Type | Tag | Default | Description |
|----------|------|-----|---------|-------------|
| m_fUn | double | TAG_dbl_Un (0x20) | 22 | Nominal voltage (kV) |
| m_fUprov | double | TAG_dbl_Pwr_Uprov (0x1101) | — | Operating voltage (kV) |
| m_fIzkr | double | TAG_dbl_Pwr_Izkr (0x1102) | 0 | SC current Ik" (kA) |
| m_fSzkr | double | TAG_dbl_Pwr_Szkr (0x1103) | 0 | SC power Sk" (MVA) |
| m_fR0_R1 | double | TAG_dbl_Pwr_R0_R1 (0x1104) | 1 | R0/R1 ratio |
| m_fX0_X1 | double | TAG_dbl_Pwr_X0_X1 (0x1105) | 1 | X0/X1 ratio |
| m_fR_X | double | TAG_dbl_Pwr_R_X (0x1106) | 0 | R/X ratio |
| m_bEntIzkr | bool | TAG_b_Pwr_EntIzkr (0x1107) | true | Enter as Ik" (vs Sk") |
| m_fRn | double | TAG_dbl_Pwr_Rn (0x110D) | 0 | Neutral R |
| m_fXn | double | TAG_dbl_Pwr_Xn (0x110E) | 0 | Neutral X |

3-phase extensions: m_fUprovB/C, m_fAngA/B/C (per-phase voltages and angles)

### cl_Sync_Element (TAG_CLASS_SYNC = 0x80001700)
**Inherits**: cl_CircleTerm_Element → cl_Term_Element → cl_Scheme_Element
**Terminals**: 1

| Property | Type | Tag | Default | Description |
|----------|------|-----|---------|-------------|
| m_fUn | double | TAG_dbl_Un (0x20) | 0.4 | Nominal voltage (kV) |
| m_fSn | double | TAG_dbl_Sn (0x49) | 0 | Nominal power (VA) |
| m_fPn | double | TAG_dbl_Pn (0x4A) | 0 | Nominal active power (W) |
| m_fCosPhin | double | TAG_dbl_cosPhin (0x4C) | 0.8 | Nominal power factor |
| m_eSyncType | SyncType | TAG_SyncType (0x1701) | Generator | Type |
| m_fXd | double | TAG_dbl_Sync_Xd (0x1704) | 0 | Synchronous reactance Xd |
| m_fXd0 | double | TAG_dbl_Sync_Xd0 (0x1716) | 0 | Transient reactance X'd |
| m_fXd1 | double | TAG_dbl_Sync_Xd1 (0x1717) | 0 | Subtransient reactance X''d |
| m_fQmin | double | TAG_dbl_Sync_Qmin (0x1708) | 0 | Min reactive power |
| m_fQmax | double | TAG_dbl_Sync_Qmax (0x1709) | 0 | Max reactive power |
| m_fPmin | double | TAG_dbl_Sync_Pmin (0x170A) | 0 | Min active power |
| m_fPmax | double | TAG_dbl_Sync_Pmax (0x170B) | 0 | Max active power |
| m_fPwrBlock | double | TAG_dbl_Sync_PwrBlock (0x1718) | 0 | Block transformer power |
| m_fPG | double | TAG_dbl_Sync_PG (0x1719) | 0 | Power generation |
| m_fTm | double | TAG_dbl_Sync_Tm (0x1720) | 0 | Mechanical time const |
| m_eSyncCateg | int | TAG_SyncCateg (0x1715) | 0 | SC contribution category |
| m_fR0_R1 | double | TAG_dbl_Sync_R2_R1 (0x170C) | 1 | R2/R1 ratio |
| m_fX0_X1 | double | TAG_dbl_Sync_X2_X1 (0x170D) | 1 | X2/X1 ratio |

**Enums**: `SyncType` { sync_type_Motor, sync_type_Generator, sync_type_Wind }

---

## Remaining Types — Brief Listings

### cl_Transformer3_Element (TAG_CLASS_XFORMER3 = 0x80001D00)
Extends cl_Transformer_Element with: U3, In3, pairwise impedances (Sn12/13/23, Pk12/13/23, Uk12/13/23), G0/G1, B0/B1 ratios. 3 terminals.

### cl_Async_Element (TAG_CLASS_ASYNC = 0x80001600)
Properties: Un, Sn/Pn, cosPhin, async type (Motor/Generator/Wind), stator type (Y/D/Yn), efficiency, R_X_comp, startup flag, converter flag, pole count, SK contribution. Harmonic support. 1 terminal.

### cl_PhotoVolt_Element (TAG_CLASS_PHOTOVOLT = 0x80001900)
Properties: Un, Sn/Pn, cosPhin, regulation type, Q(U) curve (4 points: UUn1-4, QQmax1-4), secondary regulation, Cos_min, Pmax, PQ diagram flag. 1 terminal.

### cl_Gate_Element (TAG_CLASS_GATE = 0x80001A00)
Properties: Un, Qk (reactive power), detuning factor p, KB (capacitor bank) mode, economy params (OPEX/CAPEX). 1 terminal.

### cl_Reactor_Element (TAG_CLASS_REACTOR = 0x80001C00)
Properties: Un, In (nominal current), Uk (%), R_X ratio, R0_R1, X0_X1. 2 terminals.

### cl_Choke_Element (TAG_CLASS_CHOKE = 0x80001B00)
Properties: Un, Q (reactive power), R_X ratio, R0_R1, X0_X1, Petersen coil mode, additional resistor option, Uln (line voltage). 1 terminal.

### cl_CurrSrc_Element (TAG_CLASS_CURR_SRC = 0x80001800)
Properties: Un, harmonic currents I1–I50 (50 harmonics), CurrSrc type (TyristorCnv/CapRectifier/CoilRectifier), subtype. 1 terminal.

### cl_HDO_Src_Element (TAG_CLASS_HDO_SRC = 0x80001F00)
Properties: m_fU (HDO voltage). Minimal element. 1 terminal.

### cl_Accumulation_Element (TAG_CLASS_ACCU = 0x80002200)
Battery storage element. Requires `_BATT_STORAGE_`. Properties: power, capacity, regulation params. 1 terminal.

### cl_FuseRack_Element (TAG_CLASS_FUSE_RACK = 0x80002100)
Multi-terminal distribution element. Up to 6 fuse-protected terminals. Fuse state per terminal (tags 0x2120–0x2125).

### cl_Text_Element (TAG_CLASS_TEXT = 0x80001E00)
Text annotation. 0 terminals. Only base properties (position, name/text content, font).
