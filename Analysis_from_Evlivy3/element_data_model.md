# EVlivy3 / DNCalc -- Element Data Model Analysis

## 1. Inheritance Hierarchy

The element class hierarchy is rooted in `cl_SerializableObject` (from `Serializable.h`), which provides TLV-based serialize/deserialize infrastructure. All scheme elements derive from `cl_Scheme_Element`.

```
cl_SerializableObject
  |
  +-- cl_Scheme_Element                          [include/cl_Scheme_Element.h]
        |
        +-- cl_Node                              [include/cl_Node.h]
        |     |
        |     +-- cl_FuseRack_Element            [cl_FuseRack_Element.h]
        |
        +-- cl_Term_Element                      [include/TermElement.h]
        |     |
        |     +-- cl_MultiTerm_Element           [include/TermElement.h]
        |     |     |
        |     |     +-- cl_Line_Element          [include/cl_Line_Element.h]
        |     |     +-- cl_Switch_Element        [include/cl_Switch_Element.h]
        |     |     +-- cl_Reactor_Element       [include/cl_Reactor_Element.h]
        |     |     +-- cl_Transformer_Element   [include/cl_Transformer_Element.h]
        |     |           |                       (also: cl_Power_Colour_Element, cl_Regulation_Interface)
        |     |           +-- cl_Transformer3_Element [include/cl_Transformer3_Element.h]
        |     |
        |     +-- cl_Deviation_Element           [include/TermElement.h]
        |     |     |
        |     |     +-- cl_Load_Element          [include/cl_Load_Element.h]
        |     |     +-- cl_Power_Element         [include/cl_Power_Element.h]
        |     |     |                             (also: cl_Power_Colour_Element)
        |     |     +-- cl_PhotoVolt_Element     [include/cl_PhotoVolt_Element.h]
        |     |     |     |                       (also: cl_Inverter_Regulation)
        |     |     |     +-- cl_Accumulation_Element  [cl_Accumulation_Element.h]
        |     |     |     +-- cl_MicroCoGen_Photo_Element [cl_MicroCoGen_Element.h]
        |     |     |           |                  (also: cl_MicroCoGen_Element mixin)
        |     |     |           +-- cl_MicroCoGen_Photo1_Element
        |     |     |
        |     |     +-- cl_CircleTerm_Element    [include/TermElement.h]
        |     |           |
        |     |           +-- cl_Sync_Element    [include/cl_Sync_Element.h]
        |     |           |     |                 (also: cl_Inverter_Regulation)
        |     |           |     +-- cl_MicroCoGen_Sync_Element [cl_MicroCoGen_Element.h]
        |     |           |                        (also: cl_MicroCoGen_Element mixin)
        |     |           +-- cl_Async_Element   [include/cl_Async_Element.h]
        |     |                 |                  (also: cl_Inverter_Regulation)
        |     |                 +-- cl_MicroCoGen_Async_Element [cl_MicroCoGen_Element.h]
        |     |                                    (also: cl_MicroCoGen_Element mixin)
        |     |
        |     +-- cl_Gate_Element                [include/cl_Gate_Element.h]
        |     +-- cl_Choke_Element               [include/cl_Choke_Element.h]
        |     +-- cl_CurrSrc_Element             [include/cl_CurrSrc_Element.h]
        |     +-- cl_HDO_Src_Element             [include/cl_HDO_Src_Element.h]
        |     +-- cl_Text_Element                [include/cl_Text_Element.h]
        |
        +-- cl_Shadow_Element                    [include/TermElement.h]
```

### Mixin / Interface Classes (not deriving from cl_Scheme_Element)

| Class | File | Purpose |
|-------|------|---------|
| `cl_Power_Colour_Element` | `include/TermElement.h` | Adds `m_Colour` member for power-domain color |
| `cl_Regulation_Interface` | `Regulation.h` | Pure interface: Initialize, SetResult, Compute, IsSolved, GetResult |
| `cl_Inverter_Regulation` | `Regulation.h` | Extends `cl_Regulation_Interface` with Q(U)/Q(P)/cos(phi) regulation curves |
| `cl_MicroCoGen_Element` | `cl_MicroCoGen_Element.h` | Mixin for micro-cogeneration type/kind and hexagonal drawing |

---

## 2. cl_Scheme_Element Base Class

**File:** `include/cl_Scheme_Element.h`
**TLV Class Tag:** `TAG_CLASS_NODE` (for nodes), each concrete type has its own `TAG_CLASS_xxx`

### Core Data Members

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_nID` | `uint32_t` | `TAG_u32_ID` (0x01) | Unique element identifier |
| `m_nDB_ID` | `uint32_t` | `TAG_u32_DB_ID` (0x46) | Database library reference ID |
| `m_szID` | `wxString` | `TAG_sz8_ID` (0x0D) | String identifier |
| `m_nExt_ID` | `uint64_t` | `TAG_u64_ExtID` (0x0E) | External system ID |
| `m_szName` | `wxString` | `TAG_sz8_Name` (0x02) | Human-readable name |
| `m_bPassive` | `bool` | `TAG_b_Passive` (0x43) | Passive (disconnected) flag |
| `m_szNote` | `wxString` | `TAG_sz8_Note` (0x44) | User note |
| `m_Position` | `wxPoint` | `TAG_u32_Position_X/Y` (0x10/0x11) | Canvas position |
| `m_nZAxis` | `uint32_t` | `TAG_u32_Position_Z` (0x12) | Z-order for drawing |
| `m_bSelected` | `bool` | `TAG_b_Selected` (0x45) | Selection state |
| `m_bVisible` | `bool` | `TAG_b_Visible` (0x18) | Visibility flag |
| `m_nNodeType` | `int` | - | Node type classification |
| `m_bSeen` | `bool` | - | Topology walk flag |
| `m_bInCalculation` | `bool` | - | Included in calculation |
| `m_bNotInvolved` | `bool` | - | Excluded from calculations |
| `m_bPowered` | `bool` | - | Element is powered |
| `m_nModifier` | `uint32_t` | - | Modification flags |
| `m_TopoPos` | `cl_OSMPosition` | `TAG_u32_Topo_X/Y` (0x1A/0x1B) | Geographic position |
| `m_pParent` | `cl_Scheme*` | - | Back-reference to scheme |

### Attached Attribute Objects

`m_lstAttribs` is a list of `cl_Element_Attrib` objects, each serialized as a nested TLV class:

| Attribute Class | TLV Class Tag | Purpose |
|----------------|---------------|---------|
| `cl_Name_Attrib` | `TAG_CLASS_NAME_ATTRIB` (0x80000010) | Positioned text label |
| `cl_ClrName_Attrib` | `TAG_CLASS_NAME_COLOUR_ATTRIB` (0x80000014) | Colored text label |
| `cl_ResultValue_Attrib` | `TAG_CLASS_VALUE_ATTRIB` (0x80000012) | Calculation result display |
| `cl_Measurement_Attrib` | `TAG_CLASS_MEAS_ATTRIB` (0x80000013) | Measurement annotation |
| `cl_LineType_Attrib` | `TAG_CLASS_LINE_TYPE_ATTRIB` (0x80000011) | Line type label |
| `cl_LineLen_Attrib` | `TAG_CLASS_LINE_LEN_ATTRIB` (0x80000015) | Line length label |

### Support Structures (defined alongside cl_Scheme_Element)

**`cl_Reliability_Params`** -- base reliability parameters:
- `m_fError_Freq[2][2]` -- failure frequency (planned/unplanned x 2 levels)
- `m_fError_Mean[2][2]` -- mean repair time
- Tags: `TAG_dbl_Error_Freq00..11` (0x60..0x67), `TAG_dbl_Error_Mean00..11` (0x64..0x67)

**`cl_Economy_Params`** -- economic data:
- `m_fOPEX`, `m_fCAPEX`, `m_fOPEX_Chng`, `m_nLifetime`, `m_nOper_Start`
- Tags: `TAG_dbl_OPEX` (0x70), `TAG_dbl_CAPEX` (0x71), etc.

**`cl_Harmonic_Params`** -- harmonic current injection:
- `m_fI[Converter_HARM_Cnt]` -- harmonic currents (up to 50 harmonics)
- Tags: `TAG_dbl_CurrSrc_I1..I50` (0x1801..0x1832)

---

## 3. Terminal System

**File:** `include/TermElement.h`

### cl_Term_Element

Adds terminal/connection infrastructure to `cl_Scheme_Element`.

| Member | Type | Description |
|--------|------|-------------|
| `m_pConnection[MAX_NODE_TERMINALS]` | `cl_Node*[3]` | Connected nodes (MAX_NODE_TERMINALS=3) |
| `m_lstLinePoint[MAX_NODE_TERMINALS]` | `T_OSMPoint_List[3]` | Polyline points for each terminal |
| `m_pXferNode` | `cl_Node*` | Handover node reference |
| `m_pXferLine` | `cl_Term_Element*` | Handover line reference |
| `m_nPhase_Conn` | `uint8_t` | Phase connection bitmap |
| `m_fDPDU` | `double` | dP/dU coefficient |
| `m_fDQDU` | `double` | dQ/dU coefficient |
| `m_fMP` | `double` | MP coefficient |
| `m_fNQ` | `double` | NQ coefficient |
| `m_3Ph_Conn_Type` | `Phase_Conn_Type_T` | 3-phase connection type (D, Y, YN, ZN) |

TLV tags for terminal data:
- `TAG_u8_Phase_Conn` (0x8C), `TAG_dbl_dPdU` (0x8D), `TAG_dbl_dQdU` (0x8E)
- `TAG_3Ph_Connection` (0x8F), `TAG_dbl_MP` (0x90), `TAG_dbl_NQ` (0x91)
- `TAG_u32_HandOvr_Node` (0x83), `TAG_u32_HandOvr_Line` (0x84)
- Terminal connections stored via `TAG_CLASS_TERM_CONN_HLP` (0x80000280)
- Polyline points via `TAG_CLASS_POINT_CONN_HLP` (0x80000290)

### cl_Deviation_Element

Extends `cl_Term_Element` for elements with reliability/economy/time-slice data:

| Member | Type | Description |
|--------|------|-------------|
| `m_RelParams` | `cl_Dev_Elem_Reliability` | Reliability + maintenance parameters |
| `m_EcoParams` | `cl_Economy_Params` | Economic parameters |
| `m_TimeSlice` | `cl_TimeSlice_Set` | Time-dependent operation profiles |
| `m_bPQ_Diagr` | `bool` | Has PQ diagram |
| `m_uPQ_Diag` | `cl_PQ_Diagram` | PQ diagram data |

Maintenance tags: `TAG_dbl_Maint_Freq` (0x52), `TAG_dbl_Maint_Mean` (0x53), `TAG_dbl_Maint_Freq2` (0x54), `TAG_dbl_Maint_Mean2` (0x55)

### cl_MultiTerm_Element

For elements with exactly 2 terminals (lines, transformers, switches, reactors). Provides `GetPoint()` for connection geometry.

### cl_CircleTerm_Element

For elements drawn as circles (sync/async machines). Single terminal. Provides `GetRadius()` virtual.

### cl_Shadow_Element

Lightweight proxy: stores `m_lID` (reference to real element ID). Used for clipboard and undo operations.

---

## 4. Per-Element Type Details

### 4.1 cl_Line_Element (Power Line)

**File:** `include/cl_Line_Element.h`
**TLV Class:** `TAG_CLASS_LINE` (0x80001200)
**Inherits:** `cl_MultiTerm_Element`
**Terminals:** 2

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_fUn` | `double` | `TAG_dbl_Un` (0x20) | Nominal voltage [kV] |
| `m_fImax` | `double` | `TAG_dbl_Imax` (0x22) | Max thermal current [A] |
| `m_fImaxn` | `double` | `TAG_dbl_Imaxn` (0x4F) | Nominal max current [A] |
| `m_fLength` | `double` | `TAG_dbl_Length` (0x40) | Line length [km] |
| `m_fSpecR` | `double` | `TAG_dbl_SpecR` (0x33) | Specific resistance [Ohm/km] |
| `m_fSpecX` | `double` | `TAG_dbl_SpecX` (0x34) | Specific reactance [Ohm/km] |
| `m_fSpecB` | `double` | `TAG_dbl_SpecB` (0x35) | Specific susceptance [uS/km] |
| `m_fCrossSect` | `double` | `TAG_dbl_CrossSection` (0x36) | Cross-section [mm2] |
| `m_fR0_R1` | `double` | `TAG_dbl_R0_R1` (0x2D) | Zero/positive seq R ratio |
| `m_fX0_X1` | `double` | `TAG_dbl_X0_X1` (0x2E) | Zero/positive seq X ratio |
| `m_bEnterL` | `bool` | `TAG_b_EnterL` (0x1205) | Enter inductance instead of X |
| `m_bEnterC` | `bool` | `TAG_b_EnterC` (0x1206) | Enter capacitance instead of B |
| `m_fSpecL` | `double` | `TAG_dbl_SpecL` (0x88) | Specific inductance [mH/km] |
| `m_fSpecC` | `double` | `TAG_dbl_SpecC` (0x89) | Specific capacitance [nF/km] |
| `m_LineKind` | `Line_Type` | `TAG_LINE_Kind` (0x1204) | lt_Outdoor or lt_Cable |
| `m_szType` | `wxString` | `TAG_sz8_Type` (0x05) | Line type name |
| `m_RelParams` | `cl_Line_Reliability` | various (0x60..0x67) | Line-specific reliability |
| `m_EcoParams` | `cl_Economy_Params` | various (0x70..0x78) | Economic parameters |

Custom attributes: `cl_LineType_Attrib`, `cl_LineLen_Attrib`

### 4.2 cl_Transformer_Element (2-Winding Transformer)

**File:** `include/cl_Transformer_Element.h`
**TLV Class:** `TAG_CLASS_XFORMER` (0x80001300)
**Inherits:** `cl_MultiTerm_Element` + `cl_Power_Colour_Element` + `cl_Regulation_Interface`
**Terminals:** 2

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_fU1` | `double` | `TAG_dbl_U1` (0x23) | Primary voltage [kV] |
| `m_fU2` | `double` | `TAG_dbl_U2` (0x24) | Secondary voltage [kV] |
| `m_fSt` | `double` | `TAG_dbl_St` (0x30) | Rated power [MVA] |
| `m_fPk` | `double` | `TAG_dbl_Pk` (0x31) | Short-circuit losses [kW] |
| `m_fUk` | `double` | `TAG_dbl_uk` (0x32) | Short-circuit voltage [%] |
| `m_fI0` | `double` | `TAG_dbl_io` (0x38) | No-load current [%] |
| `m_fP0` | `double` | `TAG_dbl_Po` (0x37) | No-load losses [kW] |
| `m_fHoudUhel` | `double` | `TAG_dbl_HodUhel` (0x1305) | Winding angle [deg] |
| `m_fIn1/m_fIn2` | `double` | `TAG_dbl_In1/In2` (0x3A/0x3B) | Nominal currents [A] |
| `m_PrimWindg` | `WindingType` | `TAG_PrimWindg` (0x1306) | Primary winding: D/Y/YN/ZN |
| `m_SecWindg` | `WindingType` | `TAG_SecWindg` (0x1307) | Secondary winding |
| `m_szManufacturer` | `wxString` | `TAG_sz8_Manufacturer` (0x04) | Manufacturer |
| `m_szType` | `wxString` | `TAG_sz8_Type` (0x05) | Type designation |
| `m_bBranchReg` | `bool` | `TAG_b_BranchRegulation` (0x1302) | Has tap changer |
| `m_nBranches` | `uint16_t` | `TAG_u16_Branches` (0x1303) | Number of tap positions |
| `m_fBranchStep` | `double` | `TAG_dbl_BranchStep` (0x1304) | Voltage step per tap [%] |
| `m_nActBranch` | `int16_t` | `TAG_i16_ActBranch` (0x130F) | Current tap position |
| `m_fR0_R1[3]` | `double[3]` | `TAG_dbl_R0_R1` + `TAG_dbl_R0_R1_1` | Zero-seq R ratios per winding |
| `m_fX0_X1[3]` | `double[3]` | `TAG_dbl_X0_X1` + `TAG_dbl_X0_X1_1` | Zero-seq X ratios per winding |
| `m_fXn1/Rn1/Xn2/Rn2` | `double` | `TAG_dbl_Xn1..Rn2` (0x1308..0x130B) | Grounding impedances |
| `m_bBlokoveTrafo` | `bool` | `TAG_b_BlockTransf` (0x1310) | Power plant block transformer |
| `m_fPT` | `double` | `TAG_TR_f_PT` (0x1318) | Transformer power PT |
| `m_RelParams` | `cl_Xfmr_Reliability` | various | Transformer-specific reliability |
| `m_EcoParams` | `cl_Economy_Params` | various | Economic parameters |

Regulation members (from `cl_Regulation_Interface`):
- `m_bAutoRegulation` -> `TAG_b_AutoRegulation` (0x1313)
- `m_fTargetVoltage` -> `TAG_dbl_TargetVolt` (0x1314)
- `m_fUnSensZone` -> `TAG_dbl_UnSensZone` (0x1315)
- `m_fTimedZone` -> `TAG_dbl_TimedZone` (0x1316)
- `m_fTimeConst` -> `TAG_dbl_TimeConst` (0x1317)

### 4.3 cl_Transformer3_Element (3-Winding Transformer)

**File:** `include/cl_Transformer3_Element.h`
**TLV Class:** `TAG_CLASS_XFORMER3` (0x80001D00)
**Inherits:** `cl_Transformer_Element`
**Terminals:** 3

Additional members beyond cl_Transformer_Element:

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_fU3` | `double` | `TAG_dbl_U3` (0x86) | Tertiary voltage [kV] |
| `m_fIn3` | `double` | `TAG_dbl_In3` (0x87) | Tertiary current [A] |
| `m_fSn12` | `double` | `TAG_dbl_Sn12` (0x1D02) | Power 1-2 [MVA] |
| `m_fSn13` | `double` | `TAG_dbl_Sn13` (0x1D03) | Power 1-3 [MVA] |
| `m_fSn23` | `double` | `TAG_dbl_Sn23` (0x1D04) | Power 2-3 [MVA] |
| `m_fPk12/13/23` | `double` | `TAG_dbl_Pk12/13/23` (0x1D05..07) | Short-circuit losses per pair |
| `m_fUk12/13/23` | `double` | `TAG_dbl_Uk12/13/23` (0x1D08..0A) | Short-circuit voltages per pair |
| `m_fHoudUhel_ts` | `double` | `TAG_dbl_HodUhel_ts` (0x1D01) | Tertiary winding angle |
| `m_fG0_G1` | `double` | `TAG_dbl_G0_G1` (0x1D0D) | Zero-seq conductance ratio |
| `m_fB0_B1` | `double` | `TAG_dbl_B0_B1` (0x1D0E) | Zero-seq susceptance ratio |

### 4.4 cl_Switch_Element

**File:** `include/cl_Switch_Element.h`
**TLV Class:** `TAG_CLASS_SWITCH` (0x80001400)
**Inherits:** `cl_MultiTerm_Element`
**Terminals:** 2

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_bState` | `bool` | `TAG_b_State` (0x41) | Open/closed state |
| `m_nType` | `SwitchType` | `TAG_SwitchType` (0x1401) | st_Arbitrary, st_RemoteCtrl, st_Recloser |
| `m_nTimeManip` | `uint16_t` | `TAG_u16_TimeManip` (0x1402) | Manipulation time [min] |
| `m_RelParams` | `cl_Switch_Reliability` | various (0x60..0x67) | Reliability data |
| `m_EcoParams` | `cl_Economy_Params` | various (0x70..0x78) | Economic data |
| `m_bProtection` | `bool` | `TAG_b_Protection` (0x1403) | Has protection set |
| `m_Protection` | `cl_Protection_Set` | nested TLV | Protection configuration |

### 4.5 cl_Load_Element

**File:** `include/cl_Load_Element.h`
**TLV Class:** `TAG_CLASS_LOAD` (0x80001500)
**Inherits:** `cl_Deviation_Element`
**Terminals:** 1

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_fUn` | `double` | `TAG_dbl_Un` (0x20) | Nominal voltage [kV] |
| `m_fSn` | `double` | `TAG_dbl_Sn` (0x49) | Nominal apparent power [kVA] |
| `m_fPn` | `double` | `TAG_dbl_Pn` (0x4A) | Nominal active power [kW] |
| `m_fQn` | `double` | `TAG_dbl_Qn` (0x85) | Nominal reactive power [kvar] |
| `m_fIn` | `double` | `TAG_dbl_In` (0x4B) | Nominal current [A] |
| `m_fCosFin` | `double` | `TAG_dbl_cosPhin` (0x4C) | Nominal power factor |
| `m_fS` | `double` | `TAG_dbl_S` (0x26) | Operating apparent power |
| `m_fP` | `double` | `TAG_dbl_P` (0x27) | Operating active power |
| `m_fQ` | `double` | `TAG_dbl_Q` (0x28) | Operating reactive power |
| `m_fI` | `double` | `TAG_dbl_I` (0x29) | Operating current |
| `m_fCosFi` | `double` | `TAG_dbl_cosPhi` (0x2A) | Operating power factor |
| `m_cfZ` | `complex<double>` | `TAG_cd_Z` (0x47) | Impedance |
| `m_nInputType` | `int` | `TAG_u16_InpType` (0x1501) | LOAD_TYPE_USPhi/UPQ/UIP/UIPhi/UPPhi |
| `m_LoadKind` | `Load_Kind` | `TAG_Load_Kind` (0x150C) | lk_Common, lk_Flikr, lk_Asym |
| `m_bConstImp` | `bool` | `TAG_b_ConstImp` (0x150D) | Constant impedance model |
| `m_bSupply_P` | `bool` | `TAG_b_SupplyP` (0x150F) | Supplies active power |
| `m_bSupply_Q` | `bool` | `TAG_b_SupplyQ` (0x1510) | Supplies reactive power |

Flicker parameters (when `m_LoadKind == lk_Flikr`):

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_fFlikrPst` | `double` | `TAG_dbl_Flikr_PST` (0x1505) | Flicker Pst |
| `m_fFlikrFreq` | `double` | `TAG_dbl_Flikr_Freq` (0x1506) | Switching frequency |
| `m_fFlikrTp` | `double` | `TAG_dbl_Flikr_TP` (0x1507) | Switching period |
| `m_fFlikrChngs` | `double` | `TAG_dbl_Flikr_Chngs` (0x1508) | Number of changes |
| `m_fFlikrAlpha` | `double` | `TAG_dbl_Flikr_Alpha` (0x150E) | Shape factor alpha |
| `m_nFlikrType` | `int` | `TAG_u16_Flikr_Type` (0x1503) | Flicker type |
| `m_nModFlikrType` | `int` | `TAG_u16_Flikr_ModType` (0x1504) | Modified flicker type |

Asymmetric parameters (when `m_LoadKind == lk_Asym`):

| Member | Type | TLV Tag |
|--------|------|---------|
| `m_AsymType` | `Asym_Type` | `TAG_Asym_Type` (0x150A) |
| `m_fPasy` | `double` | `TAG_dbl_Asym_Power` (0x150B) |

Asym_Type: `at_InterPhase`, `at_TwoPhase`, `at_OnePhase`

### 4.6 cl_Power_Element (Power Source / Grid Feed)

**File:** `include/cl_Power_Element.h`
**TLV Class:** `TAG_CLASS_POWER` (0x80001100)
**Inherits:** `cl_Deviation_Element` + `cl_Power_Colour_Element`
**Terminals:** 1

Most members are arrays of size 2 (for dual-feed or backup source):

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_fUn` | `double` | `TAG_dbl_Un` (0x20) | Nominal voltage [kV] |
| `m_fUprov[2]` | `double[2]` | `TAG_dbl_Pwr_Uprov` + offset (0x1101+) | Operating voltage [kV] |
| `m_bEnterIzkr[2]` | `bool[2]` | `TAG_b_Pwr_EntIzkr` (0x1107+) | Enter Ik vs Sk |
| `m_fIzkr[2]` | `double[2]` | `TAG_dbl_Pwr_Izkr` (0x1102+) | Short-circuit current [kA] |
| `m_fSzkr[2]` | `double[2]` | `TAG_dbl_Pwr_Szkr` (0x1103+) | Short-circuit power [MVA] |
| `m_fR0_R1[2]` | `double[2]` | `TAG_dbl_Pwr_R0_R1` (0x1104+) | Zero-seq R ratio |
| `m_fX0_X1[2]` | `double[2]` | `TAG_dbl_Pwr_X0_X1` (0x1105+) | Zero-seq X ratio |
| `m_fR_X[2]` | `double[2]` | `TAG_dbl_Pwr_R_X` (0x1106+) | R/X ratio |
| `m_fRn[2]` | `double[2]` | `TAG_dbl_Pwr_Rn` (0x110D+) | Grounding resistance |
| `m_fXn[2]` | `double[2]` | `TAG_dbl_Pwr_Xn` (0x110E+) | Grounding reactance |

3-phase arrays (for asymmetric power sources):
- `m_f3Uprov[2][3]`, `m_f3UprovAng[2][3]` -- per-phase voltage magnitudes and angles
- Tags: `TAG_dbl_Pwr_UprovB/C` (0x1108/0x1109), `TAG_dbl_Pwr_AngA/B/C` (0x110A..0x110C)

Second set of parameters uses tag offset +0x40 from the base tags.

### 4.7 cl_Sync_Element (Synchronous Machine)

**File:** `include/cl_Sync_Element.h`
**TLV Class:** `TAG_CLASS_SYNC` (0x80001700)
**Inherits:** `cl_CircleTerm_Element` + `cl_Inverter_Regulation`
**Terminals:** 1

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_nType` | `Sync_Type` | `TAG_SyncType` (0x1701) | Motor/Generator/Wind |
| `m_fUn` | `double` | `TAG_dbl_Un` (0x20) | Nominal voltage [kV] |
| `m_bSn` | `bool` | `TAG_b_Sync_Sn` (0x1702) | Enter as Sn (vs Pn) |
| `m_fPn` | `double` | `TAG_dbl_Pn` (0x4A) | Nominal active power [kW] |
| `m_fSn` | `double` | `TAG_dbl_Sn` (0x49) | Nominal apparent power [kVA] |
| `m_fCosFin` | `double` | `TAG_dbl_cosPhin` (0x4C) | Nominal power factor |
| `m_fXd0` | `double` | `TAG_dbl_Sync_Xd0` (0x1716) | Xd synchronous reactance [%] |
| `m_fXd1` | `double` | `TAG_dbl_Sync_Xd1` (0x1717) | X'd transient reactance [%] |
| `m_fXd` | `double` | `TAG_dbl_Sync_Xd` (0x1704) | X''d subtransient reactance [%] |
| `m_fRX` | `double` | `TAG_dbl_R_X` (0x2F) | R/X ratio |
| `m_fUprov` | `double` | `TAG_dbl_Uprov` (0x21) | Operating voltage [kV] |
| `m_fPprov` | `double` | `TAG_dbl_P_op` (0x3C) | Operating active power [kW] |
| `m_bQprov` | `bool` | `TAG_b_Sync_Q_op` (0x1714) | Enter Q (vs cos phi) |
| `m_fQprov` | `double` | `TAG_dbl_Q_op` (0x8A) | Operating reactive power [kvar] |
| `m_fCosFiprov` | `double` | `TAG_dbl_cosPhi_op` (0x3D) | Operating power factor |
| `m_fQmin/Qmax` | `double` | `TAG_dbl_Sync_Qmin/Qmax` (0x1708/09) | Q regulation limits |
| `m_fPmin/Pmax` | `double` | `TAG_dbl_Sync_Pmin/Pmax` (0x170A/0B) | P regulation limits |
| `m_fRn` | `double` | `TAG_dbl_Sync_Rn` (0x1706) | Grounding resistance |
| `m_fXn` | `double` | `TAG_dbl_Sync_Xn` (0x1707) | Grounding reactance |
| `m_fR0_R1` | `double` | `TAG_dbl_R0_R1` (0x2D) | Zero-seq R ratio |
| `m_fX0_X1` | `double` | `TAG_dbl_X0_X1` (0x2E) | Zero-seq X ratio |
| `m_fR2_R1` | `double` | `TAG_dbl_Sync_R2_R1` (0x170C) | Negative-seq R ratio |
| `m_fX2_X1` | `double` | `TAG_dbl_Sync_X2_X1` (0x170D) | Negative-seq X ratio |
| `m_bSkContrib` | `bool` | - | SK contribution flag |
| `m_nSkCategory` | `Categ_Type` | `TAG_SyncCateg` (0x1715) | SK category |
| `m_bPwrBlock` | `bool` | `TAG_dbl_Sync_PwrBlock` (0x1718) | Power plant block |
| `m_fPG` | `double` | `TAG_dbl_Sync_PG` (0x1719) | PG factor |
| `m_fTm` | `double` | `TAG_dbl_Sync_Tm` (0x1720) | Mechanical time constant [s] |
| `m_fFlikr_c` | `double` | `TAG_dbl_Sync_Flikr_c` (0x1710) | Flicker coefficient c |
| `m_bConverter` | `bool` | `TAG_b_Async_Converter` (0x160B) | Has converter |
| `m_Harmonics` | `cl_Harmonic_Params` | TAG_dbl_CurrSrc_I* | Harmonic injection |
| `m_bSupply_Q` | `bool` | `TAG_b_SupplyQ` (0x1510) | Supplies reactive power |

Default values (const members):
- `m_cnSkCategory = categ_type_regul`
- `m_cfXd0 = 150.`, `m_cfXd1 = 35.`
- `m_cfPG = 0.05`, `m_cfTm = 8.`

### 4.8 cl_Async_Element (Asynchronous Machine)

**File:** `include/cl_Async_Element.h`
**TLV Class:** `TAG_CLASS_ASYNC` (0x80001600)
**Inherits:** `cl_CircleTerm_Element` + `cl_Inverter_Regulation`
**Terminals:** 1

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_nType` | `Async_Type` | `TAG_AsyncType` (0x1601) | Motor/Generator/Wind |
| `m_fUn` | `double` | `TAG_dbl_Un` (0x20) | Nominal voltage [kV] |
| `m_bSn` | `bool` | `TAG_b_Async_Sn` (0x1602) | Enter as Sn |
| `m_fPn` | `double` | `TAG_dbl_Pn` (0x4A) | Nominal power [kW] |
| `m_fSn` | `double` | `TAG_dbl_Sn` (0x49) | Nominal apparent power [kVA] |
| `m_fCosFi` | `double` | `TAG_dbl_cosPhi` (0x2A) | Nominal power factor |
| `m_fRX` | `double` | `TAG_dbl_R_X` (0x2F) | R/X ratio |
| `m_fPprov` | `double` | `TAG_dbl_P_op` (0x3C) | Operating power [kW] |
| `m_bQprov` | `bool` | `TAG_b_Q_op` (0x8B) | Enter Q vs cos phi |
| `m_fQprov` | `double` | `TAG_dbl_Q_op` (0x8A) | Operating reactive power |
| `m_fCosFiprov` | `double` | `TAG_dbl_cosPhi_op` (0x3D) | Operating power factor |
| `m_fR0_R1` | `double` | `TAG_dbl_R0_R1` (0x2D) | Zero-seq R ratio |
| `m_fX0_X1` | `double` | `TAG_dbl_X0_X1` (0x2E) | Zero-seq X ratio |
| `m_StatorType` | `Stator_Type` | `TAG_Async_Stator` (0x1605) | Y/D/Yn |
| `m_fHA_k` | `double` | `TAG_dbl_HA_k` (0x80) | Starting current ratio Ia/In |
| `m_fCosFi_k` | `double` | `TAG_dbl_cosPhi_k` (0x81) | Starting power factor |
| `m_fFlikr_c` | `double` | `TAG_dbl_Async_Flikr_c` (0x1608) | Flicker coefficient |
| `m_bConverter` | `bool` | `TAG_b_Async_Converter` (0x160B) | Has converter |
| `m_Harmonics` | `cl_Harmonic_Params` | TAG_dbl_CurrSrc_I* | Harmonic injection |
| `m_bStartUp` | `bool` | `TAG_b_Async_Startup` (0x160C) | Motor startup analysis |
| `m_bConstImp` | `bool` | `TAG_b_ConstImp` (0x150D) | Constant impedance model |
| `m_fNomEff` | `double` | `TAG_b_Async_NomEff` (0x160D) | Nominal efficiency |
| `m_fNomSlide` | `double` | `TAG_b_Async_Slide` (0x160E) | Nominal slip |
| `m_fPoleNum` | `double` | `TAG_b_Async_PoleNum` (0x160F) | Number of poles |
| `m_bSkContrib` | `bool` | `TAG_b_Async_SkContrib` (0x1610) | SK contribution flag |
| `m_bSupply_Q` | `bool` | `TAG_b_SupplyQ` (0x1510) | Supplies reactive power |

### 4.9 cl_PhotoVolt_Element (Photovoltaic / Inverter Source)

**File:** `include/cl_PhotoVolt_Element.h`
**TLV Class:** `TAG_CLASS_PHOTOVOLT` (0x80001900)
**Inherits:** `cl_Deviation_Element` + `cl_Inverter_Regulation`
**Terminals:** 1

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_fUn` | `double` | `TAG_dbl_Un` (0x20) | Nominal voltage [kV] |
| `m_bSn` | `bool` | `TAG_b_Photo_Sn` (0x1901) | Enter as Sn |
| `m_fPn` | `double` | `TAG_dbl_Pn` (0x4A) | Nominal power [kW] |
| `m_fSn` | `double` | `TAG_dbl_Sn` (0x49) | Nominal apparent power [kVA] |
| `m_fCosFi` | `double` | `TAG_dbl_cosPhi` (0x2A) | Nominal power factor |
| `m_fK` | `double` | `TAG_dbl_k` (0x82) | Starting current ratio |
| `m_fCosFiK` | `double` | `TAG_dbl_cosPhi_k` (0x81) | Starting power factor |
| `m_fPprov` | `double` | `TAG_dbl_P_op` (0x3C) | Operating power [kW] |
| `m_bQprov` | `bool` | `TAG_b_Q_op` (0x8B) | Enter Q vs cos phi |
| `m_fQprov` | `double` | `TAG_dbl_Q_op` (0x8A) | Operating reactive power |
| `m_fCosFiprov` | `double` | `TAG_dbl_cosPhi_op` (0x3D) | Operating power factor |
| `m_Category` | `Categ_Type` | `TAG_SyncCateg` (0x1715) | Category type |
| `m_Harmonics` | `cl_Harmonic_Params` | TAG_dbl_CurrSrc_I* | Harmonic injection |
| `m_bSupply_P` | `bool` | `TAG_b_SupplyP` (0x150F) | Supplies active power |
| `m_bSupply_Q` | `bool` | `TAG_b_SupplyQ` (0x1510) | Supplies reactive power |

Regulation data (from `cl_Inverter_Regulation`):
- `TAG_b_Photo_Regulation` (0x1902), `TAG_Regulation_Type` (0x1903)
- `TAG_dbl_Photo_UUn1..4` (0x1904..0x1907) -- regulation curve U/Un breakpoints
- `TAG_dbl_Photo_QQmax1..4` (0x1908..0x190B) -- regulation curve Q/Qmax breakpoints
- `TAG_b_Sec_Regulation` (0x1910), `TAG_dbl_Cos_min` (0x1911), `TAG_dbl_Pmax` (0x1912)
- `TAG_b_Has_PQ_Diag` (0x1913), `TAG_cd_Photo_Reg_Point` (0x1920)

### 4.10 cl_Accumulation_Element (Battery Storage)

**File:** `cl_Accumulation_Element.h`
**TLV Class:** `TAG_CLASS_ACCU` (0x80002200)
**Inherits:** `cl_PhotoVolt_Element`
**Terminals:** 1

Additional members:

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_fCapacity` | `double` | `TAG_dbl_Capacity` (0x4E) | Battery capacity [kWh] |
| `m_bSupply_P` | `bool` | `TAG_b_SupplyP` (0x150F) | Supplies active power |
| `m_bSupply_Q` | `bool` | `TAG_b_SupplyQ` (0x1510) | Supplies reactive power |

### 4.11 cl_Gate_Element (Compensation / Capacitor Bank)

**File:** `include/cl_Gate_Element.h`
**TLV Class:** `TAG_CLASS_GATE` (0x80001A00)
**Inherits:** `cl_Term_Element`
**Terminals:** 1

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_bKB` | `bool` | `TAG_b_Gate_KB` (0x1A03) | Capacitor bank flag |
| `m_fQk` | `double` | `TAG_dbl_Gate_Qk` (0x1A01) | Compensation power [kvar] |
| `m_fDetune` | `double` | `TAG_dbl_Gate_p` (0x1A02) | Detuning factor [%] |
| `m_fPz` | `double` | `TAG_dbl_Pz` (0x50) | Losses [kW] |
| `m_fUn` | `double` | `TAG_dbl_Un` (0x20) | Nominal voltage [kV] |
| `m_EcoParams` | `cl_Economy_Params` | various | Economic data |

### 4.12 cl_Reactor_Element

**File:** `include/cl_Reactor_Element.h`
**TLV Class:** `TAG_CLASS_REACTOR` (0x80001C00)
**Inherits:** `cl_MultiTerm_Element`
**Terminals:** 2

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_fUnr` | `double` | `TAG_dbl_Choke_Unr` (0x1C01) | Nominal voltage [kV] |
| `m_fInr` | `double` | `TAG_dbl_Choke_Inr` (0x1C02) | Nominal current [A] |
| `m_fUk` | `double` | `TAG_dbl_uk` (0x32) | Short-circuit voltage [%] |
| `m_fR_X` | `double` | `TAG_dbl_R_X` (0x2F) | R/X ratio |
| `m_fR0_R1` | `double` | `TAG_dbl_R0_R1` (0x2D) | Zero-seq R ratio |
| `m_fX0_X1` | `double` | `TAG_dbl_X0_X1` (0x2E) | Zero-seq X ratio |

### 4.13 cl_Choke_Element (Choke / Inductor)

**File:** `include/cl_Choke_Element.h`
**TLV Class:** `TAG_CLASS_CHOKE` (0x80001B00)
**Inherits:** `cl_Term_Element`
**Terminals:** 1

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_fUn` | `double` | `TAG_dbl_Un` (0x20) | Nominal voltage [kV] |
| `m_fQn` | `double` | `TAG_dbl_Qn` (0x85) | Nominal reactive power [kvar] |
| `m_fR_X` | `double` | `TAG_dbl_R_X` (0x2F) | R/X ratio |
| `m_fR0_R1` | `double` | `TAG_dbl_R0_R1` (0x2D) | Zero-seq R ratio |
| `m_fX0_X1` | `double` | `TAG_dbl_X0_X1` (0x2E) | Zero-seq X ratio |
| `m_bPetersen` | `bool` | `TAG_b_Choke_Petersen` (0x1B01) | Petersen coil mode |
| `m_fUln` | `double` | `TAG_dbl_Choke_Uln` (0x1B02) | Line voltage [kV] |
| `m_bEntQ` | `bool` | `TAG_b_Choke_EntQ` (0x1B03) | Enter Q (vs I) |
| `m_fQ` | `double` | `TAG_dbl_Q` (0x28) | Reactive power [kvar] |
| `m_fI` | `double` | `TAG_dbl_I` (0x29) | Current [A] |
| `m_fR_X_p` | `double` | `TAG_dbl_Choke_RXp` (0x1B04) | R/X ratio (Petersen) |
| `m_bAddR` | `bool` | `TAG_b_Choke_AddR` (0x1B05) | Additional resistance |
| `m_fUnp` | `double` | `TAG_dbl_Choke_Unp` (0x1B06) | Additional R voltage [kV] |
| `m_fRp` | `double` | `TAG_dbl_Choke_Rp` (0x1B07) | Additional resistance [Ohm] |

### 4.14 cl_CurrSrc_Element (Current Source / Harmonic Generator)

**File:** `include/cl_CurrSrc_Element.h`
**TLV Class:** `TAG_CLASS_CURR_SRC` (0x80001800)
**Inherits:** `cl_Term_Element`
**Terminals:** 1

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_nType` | `CurrSrc_Type` | `TAG_CurrSrc_Type` (0x1838) | None/TyristorCnv/CapRectifier/CoilRectifier |
| `m_nSubType` | `int` | `TAG_CurrSrc_SubType` (0x1839) | Subtype |
| `m_Harmonics` | `cl_Harmonic_Params` | `TAG_dbl_CurrSrc_I1..I50` (0x1801..0x1832) | Harmonic currents |

### 4.15 cl_HDO_Src_Element (HDO / Ripple Control Source)

**File:** `include/cl_HDO_Src_Element.h`
**TLV Class:** `TAG_CLASS_HDO_SRC` (0x80001F00)
**Inherits:** `cl_Term_Element`
**Terminals:** 1

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_fU` | `double` | `TAG_dbl_HDO_U` (0x1F01) | HDO source voltage [V] |

### 4.16 cl_Text_Element (Text Annotation)

**File:** `include/cl_Text_Element.h`
**TLV Class:** `TAG_CLASS_TEXT` (0x80001E00)
**Inherits:** `cl_Term_Element`
**Terminals:** 0

No additional electrical data. Purely visual element for scheme annotations.

### 4.17 cl_MicroCoGen_Element (Micro-Cogeneration Mixin)

**File:** `cl_MicroCoGen_Element.h`
**Not a scheme element** -- this is a mixin class providing type/kind serialization and hexagonal drawing.

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_nMicoge_Type` | `MicroCogEn_Type` | `TAG_MiCoGe_Type` (0x2201) | none/inv3f/inv1f/asyn3f/asyn1f/syn3f |
| `m_nKind` | `MicroCogEn_Kind` | `TAG_MiCoGe_Kind` (0x2202) | none/motor/turbine/stirling/fuel_cell |

Concrete micro-cogeneration elements use multiple inheritance:

| Class | TLV Class Tag | Inherits |
|-------|--------------|----------|
| `cl_MicroCoGen_Photo_Element` | `TAG_CLASS_PHOTO_MICOGE` (0x80002300) | cl_PhotoVolt_Element + cl_MicroCoGen_Element |
| `cl_MicroCoGen_Photo1_Element` | `TAG_CLASS_PHOTO1_MICOGE` (0x80002600) | cl_MicroCoGen_Photo_Element (1-phase variant) |
| `cl_MicroCoGen_Async_Element` | `TAG_CLASS_ASYN_MICOGE` (0x80002400) | cl_Async_Element + cl_MicroCoGen_Element |
| `cl_MicroCoGen_Sync_Element` | `TAG_CLASS_SYNC_MICOGE` (0x80002500) | cl_Sync_Element + cl_MicroCoGen_Element |

### 4.18 cl_FuseRack_Element (Fuse Rack / Distribution Cabinet)

**File:** `cl_FuseRack_Element.h`
**TLV Class:** `TAG_CLASS_FUSE_RACK` (0x80002100)
**Inherits:** `cl_Node`
**Terminals:** 2..6 (variable, MIN_FUSE_RACK_TERM=2, MAX_FUSE_RACK_TERM=6)

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_nFuseType[6]` | `Fuse_Type[6]` | `TAG_FUSE_RACK_FUSE..FUSE5` (0x2120..0x2125) | Fuse type per terminal |

Fuse_Type enum: fuse_type_125, fuse_type_160, fuse_type_200, fuse_type_250, fuse_type_315, fuse_type_350, fuse_type_400.

`MAX_RACK_CURRENT = 620.0` -- maximum allowed fuse current rating.

---

## 5. Node Details

**File:** `include/cl_Node.h`
**TLV Class:** `TAG_CLASS_NODE` (0x80001000)
**Inherits:** `cl_Scheme_Element`

| Member | Type | TLV Tag | Description |
|--------|------|---------|-------------|
| `m_mapTermElem` | `TermElemMap_T` | via `TAG_CLASS_NODE_CONN_HLP` | Map of connected terminals |
| `m_nTermNum` | `int` | `TAG_u32_TermNum` (0x1001) | Number of terminals |
| `m_nOrientation` | `int` | `TAG_u32_Orientation` (0x1002) | Visual orientation |
| `m_bDontCheck_dU` | `bool` | `TAG_b_DontCheck_dU` (0x1003) | Skip voltage drop check |
| `m_bDontList` | `bool` | `TAG_b_DontList` (0x1004) | Exclude from results listing |
| `m_fPinst` | `double` | `TAG_dbl_Pinst` (0x1005) | Installed power [kW] |
| `m_fUn` | `double` | `TAG_dbl_Un` (0x20) | Nominal voltage [kV] |
| `m_b4w_Connection` | `bool` | - | 4-wire connection |
| `m_bGrounded` | `bool` | `TAG_b_GND` (0x1006) | Grounded node |
| `m_fRn` | `double` | - | Grounding resistance [Ohm] |
| `m_fXn` | `double` | - | Grounding reactance [Ohm] |

Connection management methods:
- `Connect(cl_Term_Element*, int nTerminal)` -- connect element terminal to node
- `DisConnect(cl_Term_Element*, int nTerminal)` -- disconnect
- `Reconnect(cl_Node*)` -- transfer all connections to another node
- `GetTermElem(int nIx)` -- get connected element by index
- `GetTerminal(cl_Term_Element*)` -- get terminal index for element

Measurement support:
- `m_bMeas_U` / `m_fMeas_U` -- measured voltage flag and value
- Tags: `TAG_b_Umeas` (0x1008), `TAG_dbl_Umeas` (0x1007)

---

## 6. Scheme Container

**File:** `include/cl_Scheme.h`
**TLV Class:** `TAG_CLASS_SCHEME` (0x80000100)

### Element Storage

- `m_setElem` -- `ElemSet_T` (typedef for `std::set<cl_Scheme_Element*, cl_Z_Less>`) sorted by Z-axis
- `m_mapID` -- `ID_Elem_Hash_T` (hash map from ID to element pointer) for fast ID lookup
- `m_mapStringID` -- `SID_Elem_Hash_T` (hash map from string ID to element)

### ID Series Counters

Each element type has an independent series counter, serialized to track numbering:

| Counter | TLV Tag |
|---------|---------|
| `m_nElemCounter` | `TAG_u32_Elem_Counter` (0x0105) |
| `m_nNodeCounter` | `TAG_u32_Node_Counter` (0x0106) |
| `m_nPowerCounter` | `TAG_u32_Power_Counter` (0x0107) |
| `m_nLineCounter` | `TAG_u32_Line_Counter` (0x0108) |
| `m_nXFormerCounter` | `TAG_u32_XFormer_Counter` (0x0109) |
| `m_nSwitchCounter` | `TAG_u32_Switch_Counter` (0x010A) |
| `m_nLoadCounter` | `TAG_u32_Load_Counter` (0x010B) |
| `m_nAsyncCounter` | `TAG_u32_Async_Counter` (0x0114) |
| `m_nSyncCounter` | `TAG_u32_Sync_Counter` (0x0115) |
| `m_nCurrSrcCounter` | `TAG_u32_Curr_Src_Counter` (0x0116) |
| `m_nPhotoVoltCounter` | `TAG_u32_PhotoVolt_Counter` (0x0117) |
| `m_nGateCounter` | `TAG_u32_Gate_Counter` (0x0118) |
| `m_nChokeCounter` | `TAG_u32_Choke_Counter` (0x0119) |
| `m_nReactorCounter` | `TAG_u32_Reactor_Counter` (0x011A) |
| `m_nTransformer3Counter` | `TAG_u32_Transformer3_Counter` (0x011B) |
| `m_nHDOCounter` | `TAG_u32_HDO_Counter` (0x0122) |
| `m_nFuseRackCounter` | `TAG_u32_FuseRack_Counter` (0x0127) |
| `m_nAccuCounter` | `TAG_u32_Accu_Counter` (0x012B) |
| `m_nMiCoGeCounter` | `TAG_u32_MiCoGe_Counter` (0x012C) |
| `m_nAsyncGCounter` | `TAG_u32_AsyncG_Counter` (0x012E) |
| `m_nSyncGCounter` | `TAG_u32_SyncG_Counter` (0x012F) |

### Calculation Settings

| Member | TLV Tag | Description |
|--------|---------|-------------|
| `m_Calc_Method` | `TAG_Calc_Method` (0x010C) | Newton-Raphson / Gauss-Seidel |
| `m_Calc_ShortPower` | `TAG_ShortCirc_Power` (0x010D) | Short-circuit power reference |
| `m_fCalc_Acur_Pwr` | `TAG_dbl_Newt_Acur_Pwr` (0x010E) | Newton accuracy power |
| `m_fEpsilonP` | `TAG_dbl_EpsilonP` (0x010F) | P convergence epsilon |
| `m_fEpsilonU` | `TAG_dbl_EpsilonU` (0x0110) | U convergence epsilon |
| `m_nNewt_MaxSteps` | `TAG_u32_Newt_MaxSteps` (0x0111) | Max Newton iterations |
| `m_nGauss_TimeLim` | `TAG_u32_Gauss_TimeLim` (0x0112) | Gauss time limit [ms] |
| `m_b3Phase_Calc` | `TAG_b_3F_Calc` (0x011D) | 3-phase calculation mode |
| `m_b1Phase_Calc_Old` | `TAG_b_1F_Old_Calc` (0x011E) | Legacy 1-phase method |
| `m_nAN_Meth` | `TAG_u8_AN_Meth` (0x012D) | Analysis method variant |
| `m_fNet_Freq` | `TAG_dbl_NET_FREQ` (0x0123) | Network frequency [Hz] |
| `m_fHDO_Freq` | `TAG_dbl_HDO_FREQ` (0x011C) | HDO frequency [Hz] |

### Frequency Characteristics Settings

| Member | TLV Tag |
|--------|---------|
| `m_fFCH_From` | `TAG_dbl_FCH_FROM` (0x011F) |
| `m_fFCH_To` | `TAG_dbl_FCH_TO` (0x0120) |
| `m_fFCH_Step` | `TAG_dbl_FCH_STEP` (0x0121) |

### Impedance Limits

| Member | TLV Tag |
|--------|---------|
| `m_fImpLim_Re` | `TAG_dbl_IMP_LIM_RE` (0x0125) |
| `m_fImpLim_Im` | `TAG_dbl_IMP_LIM_IM` (0x0126) |
| `m_fImpLim_Re_Sym` | `TAG_dbl_IMP_LIM_RE_Sym` (0x0130) |
| `m_fImpLim_Im_Sym` | `TAG_dbl_IMP_LIM_IM_Sym` (0x0131) |

### Connection Limits

| Member | TLV Tag | Description |
|--------|---------|-------------|
| `m_bAltConnLimit` | `TAG_b_ALT_CONN_LIMIT` (0x0132) | Use alternative limits |
| `m_fAltConnLimit_NN` | `TAG_dbl_ALT_CONN_LIMIT_NN` (0x0133) | NN limit |
| `m_fAltConnLimit_VN` | `TAG_dbl_ALT_CONN_LIMIT_VN` (0x0134) | VN limit |
| `m_fAltConnLimit_VVN` | `TAG_dbl_ALT_CONN_LIMIT_VVN` (0x0135) | VVN limit |
| `m_bAltContUsrLimit` | `TAG_b_ALT_CONT_USR_LIMIT` (0x0150) | User-defined continuous limits |
| `m_fContImax` | `TAG_dbl_CONT_IMAX` (0x0151) | Continuous current limit |
| `m_fContUn[4]` | `TAG_dbl_CONT_UN0..3` (0x0152..0x0155) | Continuous voltage limits |

### Reliability/Economy Settings

| Member | TLV Tag |
|--------|---------|
| `m_bEnableReliability` | `TAG_b_EnableReliability` (0x0113) |
| `m_bEnableTimeSlices` | `TAG_b_EnableTimeSlices` (0x0128) |
| `m_nMinTimeStep` | `TAG_u32_MinTimeStep` (0x0129) |
| `m_fDisc_Rate` | `TAG_dbl_DISC_RATE` (0x0136) |
| `m_fY_Income` | `TAG_dbl_Y_INCOME` (0x0138) |
| `m_fIncome_Chng` | `TAG_dbl_INCOME_CHNG` (0x0139) |
| `m_nLifeSpan` | `TAG_u32_LIFE_SPAN` (0x013A) |
| `m_bEnableProtections` | `TAG_b_EnableProtections` (0x013B) |
| `m_bEnableOPF` | `TAG_b_EnableOPF` (0x811E) |
| `m_bCompatibilityCEPS` | `TAG_b_CompatibilityCEPS` (0x811F) |

### Scheme History / Provenance

| Member | TLV Tag |
|--------|---------|
| `m_szSch_Created` | `TAG_sz8_Sch_Created` (0x00C0) |
| `m_szSch_Created_ID` | `TAG_sz8_Sch_Created_ID` (0x00C1) |
| `m_dtSch_Created_Time` | `TAG_dt_Sch_Created_Time` (0x00C2) |
| `m_szSch_Modified` | `TAG_sz8_Sch_Modified` (0x00C3) |
| `m_szSch_Modified_ID` | `TAG_sz8_Sch_Modified_ID` (0x00C4) |
| `m_dtSch_Modified_Time` | `TAG_dt_Sch_Modified_Time` (0x00C5) |

### Power Domains

`cl_PowerDomain` -- groups elements into power domains based on topology.

`cl_Scheme_Walk` -- visitor/iterator for topology walking through connected elements.

### Canvas / Visual

| Member | TLV Tag |
|--------|---------|
| `m_nGridSize` | `TAG_u32_GridSize` (0x0101) |
| `m_nCanvasX/Y` | `TAG_u32_Canvas_X/Y` (0x0102/0x0103) |
| `m_fScale` | `TAG_dbl_Scale` (0x0104) |
| `m_nOffsetX/Y` | `TAG_u32_OffsetX/Y` (0x013C/0x013D) |

### DNCoRS (Voltage Control) Integration

| Member | TLV Tag | Description |
|--------|---------|-------------|
| `m_nCORS_RegMode` | `TAG_u32_CORS_REG_MODE` (0x0140) | Regulation mode |
| `m_bCORS_RegBranch` | `TAG_b_CORS_REG_BRANCH` (0x0141) | Regulate branches |
| `m_fCORS_Unet0/1` | `TAG_dbl_CORS_UNET0/1` (0x0142/0x0143) | Network voltage range |
| `m_fCORS_Qvvn` | `TAG_dbl_CORS_QVVN` (0x0144) | VVN Q target |
| `m_fCORS_Qtol` | `TAG_dbl_CORS_QTOL` (0x0145) | Q tolerance |
| `m_sz104_Link` | `TAG_sz8_104_LINK` (0x0146) | IEC 104 link address |

---

## 7. Topology / GIS

**File:** `include/cl_Topology.h`
**Conditional:** `#if defined LIM_GIS`

### cl_OSMPosition

Geographic coordinate, serializable:

| Member | Type | TLV Tag |
|--------|------|---------|
| `m_fLongitude` | `double` | `TAG_dbl_Longitude` (0xA0) |
| `m_fLatitude` | `double` | `TAG_dbl_Latitude` (0xA1) |

TLV class: `TAG_CLASS_OSM_POINT` (0x800002A0)

### cl_OSM_Base

Static helper functions for tile/geo coordinate conversions:
- `Longitude2TileX`, `Latitude2TileY` -- geo to tile coords
- `TileX2Longitude`, `TileY2Latitude` -- tile to geo coords
- `DistanceBetweenPoints` -- Vincenty's inverse formula

### cl_OSM_Cache

Tile cache with LRU eviction (`OSM_Max_Cache = 1000`).
- Tiles: 256x256 pixels, zoom range 5..19
- HTTP tile fetching via `cl_CURL_Thread` (libcurl, max 20 threads)
- Disk cache in configurable directory
- `cl_OSM_Cache_Item` -- single cached tile, ID encodes zoom+X+Y in 64-bit

### cl_Topo_Provider

Map source configuration:

| Provider | Enum |
|----------|------|
| Mapnik | `pt_Mapnik` |
| Wikimedia | `pt_Wikimedia` |
| OpenTopoMap | `pt_OpentopoMap` |
| WMFlabs | `pt_wmflabs` |
| MapQuest OSM | `pt_MapqOSM` |

### cl_Topo_Pnl

The GIS panel (wxPanel subclass). Manages:
- Map rendering with tile cache
- Element overlay on geographic view
- Mouse interaction (pan, zoom, select, drag)
- Context menus for geographic operations (insert/delete point, set length)

Each scheme element can have a `m_TopoPos` (cl_OSMPosition) and per-terminal polyline points (`m_lstLinePoint[]`) for geographic routing.

---

## 8. Configuration Data Structures

**File:** `Configuration.h`

### cl_Applic_Config (Main Application Configuration)

Contains all sub-configurations and global settings:

| Sub-config | Type | Purpose |
|------------|------|---------|
| `m_ShortCircuitCfg` | `cl_ShortCircuit_Config` | Short-circuit calculation settings |
| `m_OperResultCfg` | `cl_OperResult_Config` | Result display settings |
| `m_PhasorGrCfg` | `cl_PhasorGr_Config` | Phasor diagram settings |
| `m_TopoCfg` | `cl_Topo_Config` | GIS/topology settings |
| `m_szDatabase` | `wxString` | Component database path |
| `m_szLocalDatabase` | `wxString` | Local database path |

### cl_ShortCircuit_Config

| Field | Description |
|-------|-------------|
| `m_nCalcMethod` | GND / 3PH / custom |
| `m_nMinMax` | Min / Max calculation |
| `m_bAE/BE/CE/AB/BC/AC` | Phase combinations |
| `m_bN/NE` | Neutral combinations |
| `m_bSkContrib` | Include motor contributions |
| `m_bAddLoads` | Include load contributions |
| `m_bArcZ` | Arc impedance |
| `m_fDuration` | Short-circuit duration [s] |
| `m_bWaveForm` | Waveform display |

TLV serialization: `TAG_CLASS_CONFIG` (0x80002F00) with tags `TAG_CFG_SC_*` (0x2F01..0x2F12)

### cl_OperResult_Config

Controls which result values to display on the scheme:
- PP/Ph voltages, delta voltage, Zk, Zk angle, short-circuit power
- P/Q/S components and sums
- Impedances (longitudinal, inter-phase, phase-ground)
- Voltage asymmetry, harmonic THD/Ih
- Node/element value bitmasks
- Decimal places per result type
- Inner impedance variants (10A, 16A, 75A, user, module, Caravana)

### cl_Topo_Config

| Field | Description |
|-------|-------------|
| `m_nSource` | Map provider index |
| `m_szCacheDir` | Tile cache directory |
| `m_fDefLat/DefLon` | Default center position |
| `m_bUseProxy` | Proxy enabled |
| `m_szProxy` | Proxy address |
| `m_nPort` | Proxy port |
| `m_ShowNames[VOLT_LEVELS]` | Name visibility per voltage level (zoom thresholds) |

### DNCalc.ini Sections

**File:** `DNCalc.ini`

| Section | Key Fields |
|---------|------------|
| `[Applic]` | Window geometry, Database path, LocalDatabase path, Language, Panel sizes |
| `[Settings]` | Colors (element, voltage levels, overload, phase A/B/C/N), Fonts, Grid, Auto-save, Short-circuit defaults |
| `[EGC_Import]` | X/Y aspect ratios for import scaling |
| `[Sizes]` | Dialog and list column sizes (persisted UI state) |
| `[Results_Oper]` | Result display toggles (PPVolt, PhVolt, Zk, etc.), node/element value bitmasks |
| `[Topo]` | Map source, cache dir, default lat/lon, proxy settings, per-voltage-level name visibility |
| `[Ph_Graph]` | Phasor graph phase voltage toggle |
| `[ReopenFile]` | Files to reopen on startup |
| `[RecentFile]` | Recent file list |
| `[Perspectives]` | wxAUI layout perspectives (docking configuration) |

Voltage level colors are indexed 00..12 (`VoltLevelColor00..12`), corresponding to `VOLT_LEVELS = 13` in common.h.

---

## 9. Database Objects

**File:** `DB_Objects.h`

The SQLite component library provides parameterized templates for creating scheme elements from database records.

### Base Classes

| Class | Purpose |
|-------|---------|
| `cl_DB_Voltage` | Voltage level record (Un, phase count) |
| `cl_DB_Company` | Company/manufacturer record |
| `cl_DB_Producer` | Equipment producer record |
| `cl_DB_Winding` | Transformer winding configuration |

### Component Database Element Classes

| Class | Scheme Element | Key Extra Fields |
|-------|---------------|-----------------|
| `cl_DB_Element` | (base) | Name, manufacturer, type, DB ID |
| `cl_DB_Line_Element` | cl_Line_Element | Un, Imax, SpecR/X/B, CrossSection, R0_R1, X0_X1, line kind |
| `cl_DB_Xformer_Element` | cl_Transformer_Element | U1, U2, St, Pk, Uk, I0, P0, In1/In2, winding types, grounding impedances |
| `cl_DB_Xformer3_Element` | cl_Transformer3_Element | U3, In3, Sn12/13/23, Pk12/13/23, Uk12/13/23 |
| `cl_DB_Power_Element` | cl_Power_Element | Uprov, Izkr/Szkr, R0/R1, X0/X1, R/X, Rn, Xn |
| `cl_DB_PNE_Element` | cl_Power_Element | Power node element variant |
| `cl_DB_Flikr_Element` | cl_Load_Element (flicker) | Flikr parameters |
| `cl_DB_Invertor_Element` | cl_PhotoVolt_Element | Inverter regulation parameters |

Each DB class has `Fill(scheme_element*)` methods to transfer DB record values into scheme element data members.

---

## 10. Regulation

**File:** `Regulation.h`

### cl_Regulation_Interface

Pure interface for voltage/power regulation:

```cpp
virtual void Initialize() = 0;
virtual void SetResult(complex<double> Ux, ...) = 0;
virtual void Compute() = 0;
virtual bool IsSolved() = 0;
virtual void GetResult(complex<double> *pUx, ...) = 0;
```

Used by:
- `cl_Transformer_Element` -- tap changer regulation (direct implementation)

### cl_Inverter_Regulation

Extends `cl_Regulation_Interface` with inverter-specific regulation curves.

| Member | Type | Description |
|--------|------|-------------|
| `m_bRegulation` | `bool` | Regulation enabled |
| `m_nRegType` | `Reg_Type_T` | rt_QU, rt_QP, rt_PU, rt_CosP, rt_CosU, rt_ConstU, rt_None, rt_PQ_Diag |
| `m_cRegPoint[16]` | `complex<double>[16]` | Regulation curve breakpoints (U/Un or P/Pn vs Q/Qmax) |
| `m_fCosPhiMin` | `double` | Minimum power factor |
| `m_fPmax` | `double` | Maximum power |
| `m_bSecondaryReg` | `bool` | Secondary regulation enabled |
| `m_fTargetVoltage` | `double` | Target voltage for ConstU mode |
| `m_fUnSensZone` | `double` | Insensitivity zone |

TLV tags: `TAG_b_Photo_Regulation` (0x1902), `TAG_Regulation_Type` (0x1903), `TAG_cd_Photo_Reg_Point` (0x1920), `TAG_b_Sec_Regulation` (0x1910), `TAG_dbl_Cos_min` (0x1911), `TAG_dbl_Pmax` (0x1912)

Used by: `cl_PhotoVolt_Element`, `cl_Sync_Element`, `cl_Async_Element` (and their MicroCoGen variants)

### Regulation Types (Reg_Type_T)

| Value | Name | Description |
|-------|------|-------------|
| `rt_QU` | Q(U) | Reactive power as function of voltage |
| `rt_QP` | Q(P) | Reactive power as function of active power |
| `rt_PU` | P(U) | Active power as function of voltage |
| `rt_CosP` | cos(P) | Power factor as function of active power |
| `rt_CosU` | cos(U) | Power factor as function of voltage |
| `rt_ConstU` | Const U | Constant voltage regulation |
| `rt_None` | None | No regulation |
| `rt_PQ_Diag` | PQ Diagram | Full PQ diagram-based regulation |

### Categ_Type (SK Category)

Used for short-circuit contribution categorization of generators per EN/IEC standards:

| Value | Name |
|-------|------|
| `categ_type_regul` | Regulated |
| (other values) | Per standard categories |

---

## 11. Conditional Compilation Flags

The codebase uses extensive conditional compilation to separate GUI, calculation, import/export, and feature-specific code:

| Flag | Purpose |
|------|---------|
| `EVLIVY3_GUI` | GUI code (wxPropertyGrid editing, drawing, dialogs) |
| `EVLIVY3_CALC` | Calculation engine (AppendData, AddData) |
| `EVLIVY_IMPORT` | EGC legacy file import |
| `EXPORT_XML` | XML export |
| `LIM_GIS` | GIS/topology map panel |
| `LIM_PROTECTION` | Protection relay modeling |
| `LIM_EGC_ONLY` | EGC-only features |
| `LIM_REG_2` | Extended regulation features |
| `_VOLTAGE_CTRL_` | DNCoRS voltage control integration (IEC 104) |
| `_CLIENT_` | Authentication client mode |

---

## 12. TLV Tag Address Space Summary

| Range | Purpose |
|-------|---------|
| `0x0001 -- 0x00FF` | Common element attributes (ID, name, position, voltages, impedances, reliability, economy) |
| `0x0100 -- 0x015F` | Scheme-level settings (counters, calculation, limits, DNCoRS) |
| `0x1001 -- 0x1008` | Node-specific tags |
| `0x1101 -- 0x1110` | Power element tags |
| `0x1201 -- 0x1206` | Line element tags |
| `0x1301 -- 0x1318` | Transformer tags |
| `0x1401 -- 0x1403` | Switch tags |
| `0x1501 -- 0x1510` | Load element tags |
| `0x1601 -- 0x1610` | Async machine tags |
| `0x1701 -- 0x1720` | Sync machine tags |
| `0x1801 -- 0x1839` | Current source / harmonic tags |
| `0x1901 -- 0x1920` | Photovoltaic / inverter regulation tags |
| `0x1A01 -- 0x1A03` | Gate (compensation) tags |
| `0x1B01 -- 0x1B07` | Choke tags |
| `0x1C01 -- 0x1C02` | Reactor tags |
| `0x1D01 -- 0x1D0E` | 3-winding transformer tags |
| `0x1E00` | Text element (class tag only) |
| `0x1F01` | HDO source tags |
| `0x2120 -- 0x2125` | Fuse rack tags |
| `0x2201 -- 0x2202` | MicroCoGen tags |
| `0x2F01 -- 0x2F12` | Config serialization tags |
| `0x3001 -- 0x3FFF` | Calculation result tags |
| `0x4000 -- 0x4FFF` | Harmonic analysis result tags |
| `0x5000 -- 0x5FFF` | HDO/Flicker/Load connection result tags |
| `0x6000 -- 0x6FFF` | 4-wire result tags |
| `0x7000 -- 0x7021` | PQ diagram tags |
| `0x8000 -- 0x811F` | Protection tags |
| `0x80xxxxxx` | Class tags (TLV_CLASS bit set) |

---

## 13. Key Constants (from common.h)

| Constant | Value | Description |
|----------|-------|-------------|
| `MAX_NODE_TERMINALS` | 3 | Max terminals per element |
| `MAX_PHASES` | 4 | Phases (A, B, C, N) |
| `VOLT_LEVELS` | 13 | Number of voltage level color categories |
| `Converter_HARM_Cnt` | 50 | Max harmonic orders |
| `MAX_FUSE_RACK_TERM` | 6 | Max fuse rack terminals |
| `MAX_OSM_THREADS` | 20 | Max tile download threads |
| `OSMMaxZoom` | 19 | Max map zoom |
| `OSMMinZoom` | 5 | Min map zoom |
| `OSM_Max_Cache` | 1000 | Max cached tiles |

### Value Type Constants (VALUE_xxx)

Used in `GetValue()` / `SetValue()` / `ValueOK()` virtual methods to query/set specific element parameters in a type-safe generic way. Defined in common.h.

### Calc_Type_T Enumeration

Calculation types that drive `AddData()`:
- Power flow (normal, with regulation)
- Short-circuit (various fault types)
- Harmonic analysis
- Frequency characteristics
- HDO analysis
- Flicker analysis
- Reliability/economy
- Load connection analysis

---

## Conclusion

The EVlivy3 element data model is a sophisticated object-oriented representation of electrical power network components designed for multi-domain engineering analysis. Key architectural insights:

### Design Philosophy
- **Single Inheritance Hierarchy**: All elements descend from `cl_Scheme_Element` → ensures uniform serialization, editing, and display
- **Terminal-Based Connectivity**: Elements don't directly reference each other — they connect to `cl_Node` instances via indexed terminals
- **Type Safety**: Each element type has strongly-typed properties (enums for connection types, stator types, load input modes, etc.)
- **Calculation Agnostic**: Elements store physical parameters; `AddData()` transforms them for specific calculation contexts

### Class Hierarchy Summary
```
cl_SerializableObject (TLV interface)
└── cl_Scheme_Element (base: position, name, ID, Z-order)
    ├── cl_Node (bus, no terminals but manages connections)
    └── cl_Term_Element (base for elements with terminals)
        ├── cl_Load_Element (consumption)
        ├── cl_Deviation_Element (uncertainty parameters)
        │   ├── cl_Power_Element (grid infeed)
        │   └── cl_PhotoVolt_Element (PV inverter)
        ├── cl_CircleTerm_Element (rotating machines)
        │   ├── cl_Sync_Element (synchronous generator/motor)
        │   └── cl_Async_Element (induction machine)
        ├── cl_MultiTerm_Element (2+ terminals)
        │   ├── cl_Line_Element (cables/overhead lines)
        │   ├── cl_Transformer_Element (2-winding)
        │   ├── cl_Transformer3_Element (3-winding)
        │   ├── cl_Switch_Element (breakers, switches)
        │   ├── cl_Reactor_Element (series impedance)
        │   └── cl_FuseRack_Element (up to 6 terminals)
        └── Single-terminal elements:
            ├── cl_Gate_Element (shunt compensation)
            ├── cl_Choke_Element (Petersen coil)
            ├── cl_CurrSrc_Element (harmonic injector)
            ├── cl_HDO_Src_Element (ripple control)
            └── cl_Text_Element (annotation, 0 terminals)
```

### 18 Concrete Element Types
Each implements 100+ parameters covering:
- **Electrical**: Un, Sn, Pk, Uk, R, X, G, B, In, cosφ, impedances (positive/zero sequence)
- **Operational**: States, time constants, regulation modes, control categories
- **Reliability**: SAIDI, SAIFI, failure rates, forest/terrain factors
- **Economic**: Investment, maintenance, loss costs
- **Harmonic**: Up to 50 harmonic orders, emission spectra
- **3-Phase**: Winding types (Y/D/YN/ZN), asymmetry types, per-phase parameters

### Connection Model
- **Nodes** (`cl_Node`): Represent electrical buses, manage `TermElemMap_T` (terminal→element mapping)
- **Terminals**: Elements have 0-6 indexed connection points
- **Terminal Helpers**: `cl_Term_Conn_Hlp` serializes connections as (terminal index, node ID) pairs
- **Resolution**: During deserialization, ID references → pointer resolution in `Deserialize_Done()`

### Serialization
- **TLV Format**: Each element is a nested TLV tree (class tag + attributes + sub-objects)
- **Tag Space**: 0x80000000-0x80001FFF reserved for element classes
- **Versioning**: Scheme file version 0x00010005, forward/backward compatibility via optional tags
- **Compression**: bzip2 compression for .tlv files (typically 10:1 ratio)

### Calculation Integration
- **Data Preparation**: `AddData(Calc_Type_T)` method transforms element → AN library input format
- **Modifiers**: 16 slots for calculation modifiers (time slices, contingencies, regulation states)
- **Results**: Stored in parallel `cl_Elem_Op_Result` / `cl_Elem_Op_Result3` objects (not in elements themselves)
- **Regulation**: Transformers, inverters, sync machines implement `I_Regulation_Interface` for tap control and Q(U) regulation

### Configuration System
- **INI Files**: `cl_EVlivy3_Config` hierarchy reads/writes wxFileConfig
- **Sections**: [Calculation], [Display], [ShortCircuit], [Topology], [Application], [Database], [IEC104], [DNCoRS], [SMU]
- **Database**: SQLite-backed component library (`cl_DB_Line_Element`, `cl_DB_Xformer_Element`) for pre-defined types

### Topology & GIS
- **Power Domains**: `cl_Topology` assigns color-coded voltage level domains
- **Islands**: Detects electrically separated networks
- **Map Integration**: OSM tile layer with geo-coordinates, zoom levels 5-19, 1000-tile cache

### Conditional Compilation
- **`_VOLTAGE_CTRL_`**: Enables DNCoRS mode (IEC 104, regulation, SCADA)
- **`LIM_PROTECTION`**: Enables overcurrent protection simulation
- **`_BATT_STORAGE_`**: Enables battery/accumulation elements
- **`LIM_MicroCoGen`**: Enables micro-cogeneration elements

This data model supports not just static network analysis, but dynamic regulation, real-time SCADA integration, contingency analysis, harmonic studies, reliability evaluation, and economic optimization — all from a unified element representation.
