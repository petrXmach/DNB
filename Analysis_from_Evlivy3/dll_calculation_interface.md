# DLL Calculation Interface Analysis

## EVlivy3 / DNCalc -- Interface with AN Calculation DLL

This document provides a comprehensive analysis of how the EVlivy3 application interfaces with the AN (Analyza Napeťových poměrů / Network Analysis) calculation DLL. It covers data preparation, DLL invocation, and result extraction for both single-phase and three-phase calculation modes.

---

## 1. Architecture Overview

The data flow from scheme elements to calculation results follows this pipeline:

```
Scheme Elements (cl_Scheme_Element hierarchy)
        |
        v
cl_Calculation::PrepareData()   -- serialize elements to text/binary input
        |
        v
cl_AN_Lib / cl_AN3_Lib         -- DLL interface wrapper
        |
        v
wxbase_supp.dll (1-phase)      -- actual AN computation engine
wxbase_supp3.dll (3-phase, 3-wire)
wxbase_supp4.dll (3-phase, 4-wire, 32-bit)
wxbase_supp64.dll (3-phase, 4-wire, 64-bit)
        |
        v
cl_Calculation::ProcessResult() -- parse binary/structured output
        |
        v
cl_AN_Result / cl_Oper_Result   -- result storage per node & branch
        |
        v
GUI display (grids, graphs)
```

### Key classes in the pipeline

| Class | Role |
|---|---|
| `cl_Calculation` | Base class for all calculations. Owns `m_szInpData` (text string) and `m_InpData` (wxMemoryBuffer). Calls DLL functions. |
| `cl_OperCalc` | Operational (power flow) calculation. Subclass of `cl_Calculation`. |
| `cl_ShortCircCalc` | Short-circuit calculation. |
| `cl_FreqCalc`, `cl_HarmCalc`, `cl_HDO_Calc`, `cl_Flikr_Calc` | Other specialized calculations. |
| `cl_AN_Lib` | Single-phase DLL wrapper (wxbase_supp.dll). |
| `cl_AN3_Lib` | Three-phase DLL wrapper (wxbase_supp3/4/64.dll). |
| `cl_AN_Result` | Base result container with node/branch maps. |
| `cl_Oper_Result` | Operational result with node voltages, branch currents, powers, impedances. |
| `cl_Bodor_Element` | Intermediate 3-phase element representation for Bodor-format export. |

---

## 2. DLL Loading

### 2.1 Loading mechanism

Both `cl_AN_Lib` and `cl_AN3_Lib` use the **Win32 `LoadLibraryA` / `GetProcAddress`** pattern (explicit dynamic linking). They do NOT use wxDynamicLibrary (there is a commented-out attempt in AN3_Iface.cpp) nor static linking.

### 2.2 cl_AN_Lib::Open(wxString szDLL_Path)

**File:** `c:\DNCalc\EVlivy\EVlivy3\AN_Iface.cpp`, line 37

1. Loads `borlndmm.dll` (Borland Memory Manager -- the DLL is written in Delphi/C++ Builder):
   ```cpp
   m_hBorland = LoadLibraryA((szDLL_Path + "borlndmm.dll").mb_str());
   ```
2. Loads `wxbase_supp.dll` (single-phase AN engine):
   ```cpp
   m_hAN = LoadLibraryA((szDLL_Path + "wxbase_supp.dll").mb_str());
   ```
3. If `DLL_ALTER_LOC` is defined, falls back to `AN_dll/` subdirectory.
4. Resolves all function pointers via `GetProcAddress` with `"an"` prefix (e.g., `"anInitLibrary"`, `"anRunAnalysis"`).
5. Calls `InitLibrary()` to initialize the DLL.
6. In demo builds (`LIM_DEMO`), sets limits: `SetMaxBusCount(g_nDemo_Nodes)`, `SetMaxBranchCount(g_nDemo_Branches)`.

### 2.3 cl_AN3_Lib::Open()

**File:** `c:\DNCalc\EVlivy\EVlivy3\AN3_Iface.cpp`, line 467

1. Loads `borlndmm.dll` (with 64-bit fallback to `AN_dll64/` directory).
2. Selects DLL based on wire mode:
   - 4-wire + 64-bit: `wxbase_supp64.dll`
   - 4-wire + 32-bit: `wxbase_supp4.dll`
   - 3-wire: `wxbase_supp3.dll`
3. Resolves all function pointers (with "an" prefix).
4. Calls `InitLibrary()`.
5. Allocates a 64KB internal buffer for data transfer:
   ```cpp
   m_uBuffer = (std::unique_ptr<uint8_t[]>)(new uint8_t[65536]);
   ```
6. Additional 4-wire-only functions: `GetFinishFrequency`, `GetNodalHardness`, `GetDLLVersion`.

### 2.4 Instance handles

Both classes maintain two `HINSTANCE` members:
- `m_hBorland` -- for `borlndmm.dll` (Borland memory manager dependency)
- `m_hAN` -- for the actual calculation DLL

Cleanup in destructors calls `DoneLibrary()` then `FreeLibrary()`.

---

## 3. AN Library API -- Exported Functions

All exported functions follow the `__stdcall` calling convention and are prefixed with `"an"` in the DLL export table.

### 3.1 Single-phase API (cl_AN_Lib)

**File:** `c:\DNCalc\EVlivy\EVlivy3\AN_Iface.h`, lines 142-188

| C++ function pointer | DLL export name | Signature | Purpose |
|---|---|---|---|
| `InitLibrary` | `anInitLibrary` | `int32_t (void)` | Initialize DLL, returns non-zero on success |
| `DoneLibrary` | `anDoneLibrary` | `int32_t (void)` | Finalize / cleanup DLL |
| `SetFileName` | `anSetFileName` | `int32_t (char*)` | Set output file path for debug data |
| `ClearInpData` | `anClearInpData` | `int32_t (void)` | Clear internal input data buffer |
| `ReadInpData` | `anReadInpData` | `int32_t (const char*)` | **Primary input** -- pass text data to DLL (CP1250 encoded) |
| `GetInpData` | `anGetInpData` | `char* (void)` | Retrieve current input data |
| `LoadInpDataFromFile` | `anLoadInpDataFromFile` | `int32_t (char*)` | Load input data from file |
| `SaveInpDataToFile` | `anSaveInpDataToFile` | `int32_t (char*)` | Save input data to file |
| `SetMaxBusCount` | `anSetMaxBusCount` | `int32_t (int32_t)` | Set max bus count (demo limits) |
| `SetMaxBranchCount` | `anSetMaxBranchCount` | `int32_t (int32_t)` | Set max branch count (demo limits) |
| `GetMaxBusCount` | `anGetMaxBusCount` | `int32_t (void)` | Get max bus count |
| `GetMaxBranchCount` | `anGetMaxBranchCount` | `int32_t (void)` | Get max branch count |
| `SetParamStr` | `anSetParamStr` | `int32_t (char*, char*, char*)` | Set named parameter |
| `RunAnalysis` | `anRunAnalysis` | `int32_t (int32_t)` | **Run calculation** -- parameter controls file output (true/false) |
| `GetStatusDescr` | `anGetStatusDescr` | `StatusDescr_T* (void)` | Get status/progress descriptor |
| `GetOutDataDescr` | `anGetOutDataDescr` | `DataDescr_T* (void)` | **Get output data** -- returns pointer to binary result buffer |
| `GetDynDataDescr` | `anGetDynDataDescr` | `DataDescr_T* (void)` | Get dynamic stability data descriptor |
| `GetZkrDataDescr` | `anGetZkrDataDescr` | `DataDescr_T* (void)` | Get short-circuit data descriptor |
| `GetDysData` | `anGetDysData` | `char* (void)` | Get dynamic stability text data |
| `GetRepData` | `anGetRepData` | `char* (void)` | Get convergence report data |
| `GetResData` | `anGetResData` | `char* (void)` | Get resonance data |
| `GetStaData` | `anGetStaData` | `char* (void)` | Get static stability data |
| `GetErrData` | `anGetErrData` | `const char* (void)` | **Get error/status data** -- checked for "Finished: OK" |
| `SaveOutDataToFiles` | `anSaveOutDataToFiles` | `int32_t (char*)` | Save output data to files |

### 3.2 Three-phase API (cl_AN3_Lib)

**File:** `c:\DNCalc\EVlivy\EVlivy3\AN3_Iface.h`, lines 533-729

| C++ function pointer | DLL export name | Signature | Purpose |
|---|---|---|---|
| `InitLibrary` | `anInitLibrary` | `int32_t (void)` | Initialize DLL |
| `DoneLibrary` | `anDoneLibrary` | `int32_t (void)` | Finalize DLL |
| `ReadInpData` | `anReadInpData` | `int32_t (const char*)` | Pass UTF-16LE text input data |
| `SetMaxBusCount` | `anSetMaxBusCount` | `int32_t (int32_t)` | Demo limits |
| `SetMaxBranchCount` | `anSetMaxBranchCount` | `int32_t (int32_t)` | Demo limits |
| `RunAnalysis` | `anRunAnalysis` | `int32_t (uint32_t, int32_t)` | Run calculation -- param 1: `Calc3_Kind_T` type, param 2: output-to-file flag |
| `CheckFinishStatus` | `anCheckFinishStatus` | `int32_t (void)` | Check if calculation finished successfully |
| `GetErrorMsg` | `anGetErrorMsg` | `wchar_t* (void)` | Get error message (wide string) |
| `GetFinishMessage` | `anGetFinishMessage` | `wchar_t* (void)` | Get finish message (wide string) |
| `GetNodeData` | `anGetNodeData` | `int32_t (uint32_t nIndex, uint32_t nHarm, void* pData)` | Copy node result into caller's buffer |
| `GetBranchData` | `anGetBranchData` | `int32_t (uint32_t nIndex, uint32_t nHarm, void* pData)` | Copy branch result into caller's buffer |
| `GetNodalHarVoltage` | `anGetNodalHarVoltage` | `int32_t (uint32_t nIndex, uint32_t nHarm, void* pData)` | Get harmonic voltage for node |
| `GetBranchHarCurrent` | `anGetBranchHarCurrent` | `int32_t (uint32_t nIndex, uint32_t nHarm, void* pData)` | Get harmonic current for branch |
| `GetBranchImpedance` | `anGetBranchImpedance` | `int32_t (uint32_t nIndex, void* pData)` | Get branch impedance matrix |
| `GetNodalZthImpedance` | `anGetNodalZthImpedance` | `int32_t (uint32_t nIndex, void* pData)` | Get Thevenin nodal impedance |
| `GetNodalHarImpedance` | `anGetNodalHarImpedance` | `double (uint32_t nIndex, void* pData)` | Get harmonic impedance, returns frequency |
| `GetSCFaultData` | `anGetSCFaultData` | `int32_t (void* pData)` | Get short-circuit fault data |
| `GetNodeCount` | `anGetNodeCount` | `int32_t (void)` | Get number of nodes in results |
| `GetBranchCount` | `anGetBranchCount` | `int32_t (void)` | Get number of branches in results |
| `GetFinishFrequency` | `anGetFinishFrequency` | `double (void)` | Get final computed frequency (island mode) |
| `GetNodalHardness` | `anGetNodalHardness` | `int32_t (uint32_t, AN_NODE_HARDNESS_4_DATA_T*)` | Get nodal Thevenin data for 4-wire |
| `GetSystemLosses1f` | `anGetSystemLosses1f` | `AN_CPLX_T (void)` | Get total system losses (1st harmonic) |
| `GetContingencyData` | `anGetContingencyData` | `wchar_t* (void)` | Get contingency analysis results |
| `GetContingencyDataDescr` | `anGetContingencyDataDescr` | `AN_OutDataDescr_T* (void)` | Get contingency data descriptor |
| `SetData_UminUmax` | `anSetData_UminUmax` | `int32_t (char*)` | Set voltage limits for contingency |
| `SaveInpDataToFile` | `anSaveInpDataToFile` | `int32_t (char*)` | Debug: save input to file |
| `SetFileName` | `anSetFileName` | `int32_t (const char*)` | Set output file path |
| `GetFileName` | `anGetFileName` | `wchar_t* (void)` | Get current file path |
| `GetDLLVersion` | `anDLLVersion` | `wchar_t* (void)` | Get DLL version string (4-wire only) |

#### Protection-specific functions (conditional, `LIM_PROTECTION`):

| Function | Signature | Purpose |
|---|---|---|
| `CalcSCFaultDataProgress` | `int32_t (double fTmax, uint32_t nSamplesPP, uint32_t nPeriodsBefore, double fAlphaUA)` | Calculate time-domain SC fault progress |
| `GetMaxIndexOfProgressData` | `int32_t (void)` | Get max time index |
| `GetSCFaultProgressOfNode` | `int32_t (uint32_t nNodeNumber, uint32_t nTimeIndex, void* pDataPtr)` | Get node SC fault at time step |
| `GetSCFaultProgressOfBranch` | `int32_t (uint32_t nBranchNumber, uint32_t nTimeIndex, void* pDataPtr)` | Get branch SC fault at time step |

#### Voltage control functions (conditional, `_VOLTAGE_CTRL_`):

| Function | Signature | Purpose |
|---|---|---|
| `GetVoltageSetpoint` | `double (uint32_t nIndex)` | Get voltage setpoint for node |
| `GetOptimalTapPosition` | `double (char*)` | Get optimal tap position for transformer |

---

## 4. cl_AN_Lib -- Detailed Single-Phase Interface

**Files:** `c:\DNCalc\EVlivy\EVlivy3\AN_Iface.h`, `c:\DNCalc\EVlivy\EVlivy3\AN_Iface.cpp`

### 4.1 StatusDescr_T

```cpp
struct StatusDescr_T
{
    bool     m_bInpDataOK;      // input data available
    bool     m_bInProcess;      // calculation in progress
    bool     m_bCompleted;      // calculation completed
    bool     m_bOutDataOK;      // output data available
    char    *m_szProcessMsg;    // current process name
    char    *m_szSubProcess;    // current subprocess name
    int32_t  m_nIteration;      // current iteration number
    float    m_fProgress;       // progress percentage
    int32_t  m_nErrorRow;       // syntax error row
    int32_t  m_nErrorCol;       // syntax error column
    char    *m_szErrorMsg;      // error message
    char    *m_szResStr_1;      // reserved PChar
    char    *m_szResStr_2;      // reserved PChar
    float    m_fResSng_1;       // reserved Single
    float    m_fResSng_2;       // reserved Single
    int32_t  m_nResInt_1;       // reserved Integer
    int32_t  m_nResInt_2;       // reserved Integer
};
```

### 4.2 DataDescr_T (output data descriptor)

```cpp
struct DataDescr_T
{
    uint8_t  *m_pData;         // pointer to raw binary data
    int32_t   m_nDataSize;     // data size in bytes
    int32_t   m_nDataStruct;   // structure type (DataStructType_T)
    bool      m_bDataReady;    // data ready flag
};
```

### 4.3 Data_Header_T (binary output header)

```cpp
struct Data_Header_T
{
    char      m_cMode;         // calculation mode
    bool      m_bFlag;         // flag
    uint16_t  m_nPocetHar;     // number of harmonics
    uint16_t  m_nPocetUzlu;    // number of nodes (buses)
    uint16_t  m_nPocetVetvi;   // number of branches
};
```

### 4.4 DataStructType_T

```cpp
enum DataStructType_T
{
    dsNone = 0, dsBIN = 1, dsSYM = 2, dsDYN = 3, dsZKR = 4
};
```

### 4.5 Admittance structures (single-phase)

- `Admitance_T` -- line/cable: `m_cfPod` (longitudinal), `m_cfPr1`, `m_cfPr2` (transversal at both ends)
- `Admitance_TR_T` -- 2-winding transformer: Y11, Y12, Y21, Y22, Yk (short-circuit admittance)
- `Admitance_T3_T` -- 3-winding transformer: 3x3 Y-matrix plus Y1, Y2, Y3

### 4.6 ZKR_Data_T (short-circuit results, single-phase)

Contains: Unom, Uvyp (computed voltage), c factor, impedances (Z positive/negative/zero sequence), total Zk, earth Zk, peak factor K, Ik'' (initial), IkmE (earth), Ikm (peak), Ivyp (breaking), Ike (thermal equivalent), Ik (steady-state), Tk (duration), m/n factors.

---

## 5. cl_AN3_Lib -- Three-Phase Variant

**Files:** `c:\DNCalc\EVlivy\EVlivy3\AN3_Iface.h`, `c:\DNCalc\EVlivy\EVlivy3\AN3_Iface.cpp`

### 5.1 StatusDescr3_T

Differs from `StatusDescr_T` -- uses `int8_t` instead of `bool` for flags (packed struct alignment), and adds readiness flags for specific data types:

```cpp
struct StatusDescr3_T   // #pragma pack(push,1)
{
    int8_t  m_nInpDataOK;       // input data available
    int8_t  m_nInProcess;       // calculation in progress
    int8_t  m_nCompleted;       // calculation completed
    int8_t  m_nOutDataOK;       // output data available
    char   *m_szProcessMsg;     // process name
    char   *m_szSubProcess;     // subprocess name
    int32_t m_nIteration;       // iteration number
    float   m_fProgress;        // progress %
    int32_t m_nErrorRow;        // syntax error row
    int32_t m_nErrorCol;        // syntax error column
    char   *m_szErrorMsg;       // error message
    int8_t  m_nRepDataOK;       // REP data ready (convergence)
    int8_t  m_nResDataOK;       // RES data ready (resonance)
    int8_t  m_nDynDataOK;       // DYN data ready (dynamic stability)
    int8_t  m_nDysDataOK;       // DYS data ready
    int8_t  m_nStaDataOK;       // STA data ready (static stability)
    int8_t  m_nZbusReady;       // Zbus impedance matrix ready
    int8_t  m_nCalcKind;        // calculation type
    int8_t  m_nFKonDataOK;      // KON data ready (contingency)
    int8_t  m_nFFliDataOK;      // FLI data ready (flicker)
};
```

### 5.2 Core data types

All structures use `_PACKED_` attribute for exact binary layout matching between C++ and Delphi/C++ Builder DLL.

| Type | Description |
|---|---|
| `AN_CPLX_T` | Complex number: `{ double m_fReal; double m_fImag; }` (16 bytes) |
| `AN_DBL_T` | Single double: `{ double m_fVal; }` (8 bytes) |
| `AN_4DBL_T` | Four doubles: `{ double m_fVal[4]; }` (32 bytes) |
| `T3_PHASE_T` | 3-phase phasor: `{ AN_CPLX_T m_Phase[3]; }` (48 bytes) |
| `T4_PHASE_T` | 4-phase phasor (A,B,C,N): `{ AN_CPLX_T m_Phase[4]; }` (64 bytes) |
| `AN_CPLX_POL_T` | Polar complex: `{ double m_fAbs; double m_fAng; }` |
| `AN_ASYM_FACTOR_T` | Asymmetry: `{ double m_fUnbalance; double m_fAsymmetry; }` |
| `T3_SYM_COMP_T` | Symmetrical components: `{ AN_CPLX_T m_cVal[3]; }` (positive, negative, zero) |
| `T3_MATRIX_T` | 3x3 complex matrix: `{ AN_CPLX_T m_Value[9]; }` (144 bytes) |
| `T4_MATRIX_T` | 4x4 complex matrix: `{ AN_CPLX_T m_Value[16]; }` (256 bytes) |
| `AN_MULTIPORT_T` | Multiport 3-phase: `{ T3_MATRIX_T m_Elem3[3][3]; }` |
| `AN_MULTIPORT_4_T` | Multiport 4-phase: `{ T4_MATRIX_T m_Elem3[3][3]; }` |
| `AN_TWOPORT_T` | Two-port: `{ uint32_t m_Is_Dipole; T3_MATRIX_T m_Elem2[2][2]; }` |

### 5.3 Node identification

```cpp
typedef wchar_t AN_ID_STR[80];  // 160 bytes wide-char ID string

struct AN_NODE_ID_4_T   // 4-wire node ID
{
    AN_ID_STR  m_szID;         // identification name
    uint32_t   m_nNumber;      // ordinal number (from zero)
    uint8_t    m_nType;        // classification (load, balance, regulation, comp.)
    uint8_t    m_nKind;        // physical/fictitious
    uint8_t    m_bGenerator;   // generator node flag
    uint8_t    m_bTxHDO;       // HDO transmitter flag
    uint8_t    m_Padding[32];  // padding
};
```

### 5.4 Node result data

**3-wire:** `AN_NODE_DATA_T` -- check number, ID string, 3-phase voltages, symmetrical component voltages, asymmetry factors, injected currents/powers.

**4-wire:** `AN_NODE_DATA_4_T` -- same concept but with `T4_PHASE_T` (4 phases including neutral), `AN_NODE_ID_4_T`, larger structure.

### 5.5 Branch result data

**3-wire:** `AN_BRANCH_DATA_T` -- check, ID, 3 ports (each with currents, symmetrical components, power, cos-phi), losses.

**4-wire:** `AN_BRANCH_DATA_4_T` -- same with 4-phase ports, additional padding, R/X ratio.

### 5.6 Short-circuit result structures

**3-wire:** `AN_SHORTCIRC_DATA_T` with `AN_SHORTCIRC_REC_T[11]` -- 11 fault types (A-E, B-E, C-E, A-B, B-C, A-C, A-B-E, B-C-E, A-C-E, A-B-C, A-B-C-E).

**4-wire:** `AN_SHORTCIRC_4_DATA_T` with `AN_SHORTCIRC_4_REC_T[26]` -- 26 fault types (adds neutral combinations: A-N, B-N, C-N, N-E, etc.).

### 5.7 Internal buffer and data retrieval

The 3-phase library uses a different pattern than single-phase. Instead of returning a `DataDescr_T*` pointer to internal memory, it copies data into the caller's buffer:

```cpp
// In cl_AN3_Lib (64KB buffer allocated at Open() time)
m_uBuffer = std::unique_ptr<uint8_t[]>(new uint8_t[65536]);

// Reading node data -- DLL copies into m_uBuffer
void cl_AN3_Lib::ReadNodeData(uint32_t nIndex) {
    uint32_t nRes = GetNodeData(nIndex, 0, (void*)m_uBuffer.get());
}
```

The buffer is then cast to the appropriate structure type (`AN_NODE_DATA_T`, `AN_NODE_DATA_4_T`, etc.) depending on `m_b4Wire` and `nCalcMethod`.

---

## 6. Data Preparation (cl_Calculation)

**Files:** `c:\DNCalc\EVlivy\EVlivy3\include\cl_Calculation.h`, `c:\DNCalc\EVlivy\EVlivy3\cl_Calculation.cpp`

### 6.1 Two storage mechanisms for input data

The class maintains two parallel input data buffers:

```cpp
wxString         m_szInpData;     // text-based input (wxString, used for 1-phase old format)
wxMemoryBuffer   m_InpData;       // binary buffer (used for 3-phase NEW_EXPORT format)
std::wostringstream m_OStream;    // stream for NEW_EXPORT element serialization
```

### 6.2 Single-phase data preparation

For single-phase calculation, `cl_OperCalc::PrepareData()` builds `m_szInpData` as a structured text string in CP1250 encoding. The format is a line-oriented text protocol with sections marked by `=SectionName` headers and `;` comment lines.

**Section structure:**

```
=Název akce:  Výpočet přenosových poměrů v síti
=Varianta:    ChodPQ
=Frekv.1.hr:  50
=Provozni_stav: maximalni
=Chod PQ
  it.metoda:  Newtonova
  epsilon P:  1e-6
  epsilon U:  1e-6
  max.kroků:  100
  čas.limit:  10

=Uzly
   1  22.0
   2  22.0
   ...

=Vztažné napětí kV:  22.000000

=Vetve VK     ;Vedení - kilometrové parametry
; zapoj   kód       Rk       Lk      Gk      Ck       S      l     R0/R1  X0/X1
  1-2     VK    0.120000  1.05    0       3.80    150     5.2     1       1      42

=Vetve TR     ;Transformátor dvouvinuťový
  3-4     TR    ...

=Vetve SI     ;Napájecí uzel sítě
  5-5     SI    500.000  0.995  22.00  0.000  1.00  1.00  17

=Vetve ZQ     ;Zátěž - paralelní model s kompenzací
  6-6     ZQ    0.50000  0.30000  0.00000  22

=Konec dat.
```

### 6.3 Element encoding (single-phase)

Each element type has its own `Prepare*` method:

| Method | Section code | Parameters |
|---|---|---|
| `PrepareNodes()` | `=Uzly` | Node ID, Un (kV) |
| `PrepareLines()` | `=Vetve VK` | Connection, Rk, Lk, Gk, Ck, S, length, R0/R1, X0/X1, element ID |
| `PrepareSwitches()` | `=Vetve VY` | Connection, state (0/1), element ID |
| `PrepareTrafos()` | `=Vetve TR` | Connection, Sn, Un1, Un2, uk, dPk, i0, dP0, Xn1, Rn1, Xn2, Rn2, R0/R1, X0/X1, winding types, hour angle, regulation, block trafo flag, pT, element ID |
| `PrepareTrafos3()` | `=Vetve T3` | 3-winding transformer: terciary node, three Sn values, three Un values, three uk/Pk pairs, i0, dP0, windings, hour angles, regulation, ID |
| `PreparePwrNode()` | `=Vetve SI` | Connection, Sk (MVA), cos(fi), U (kV), alfaU, R0/R1, X0/X1, element ID |
| `PrepareLoad()` | `=Vetve ZQ` or `=Vetve PQ` or `=Větve NZ` | P, Q, Qk values depending on content type |
| `PreparePhotoVolt()` | `=Vetve ZQ` or `=Vetve PQ` or `=Větve M3` | PV power, cos(fi), reactance ratio |
| `PrepareASync()` | `=Vetve ZQ` or `=Vetve PQ` or `=Větve M3` | Motor/generator parameters |
| `PrepareSync()` | `=Vetve SM` or `=Větve SG` | Synchronous machine parameters |
| `PrepareGate()` | (gate elements) | Gate parameters |
| `PrepareReactor()` | `=Vetve RE` | Reactor parameters |
| `PrepareChoke()` | `=Vetve L3` | Choke parameters |

Node connections are formatted as `"i-j"` where i and j are the AN result node IDs.

### 6.4 Three-phase data preparation (Bodor format)

For 3-phase, two approaches coexist (controlled by `NEW_EXPORT` preprocessor define):

**Old approach (AppendData):** Each element has an `AppendData(nType, pCalc, nFlags)` method that writes formatted text to `pCalc->m_szInpData`. Headers are written by `AppendHeader(HEADER_*)`.

**New approach (AddData + ExportElements):** Two-step process:
1. `AddElements(nFlags, CalcType)` iterates all scheme elements, calling `pElem->AddData(pCalc, CalcType, nFlags)` which creates `cl_Bodor_Element` subclass instances stored in `m_pResult->m_Bodor_Elems`.
2. `ExportElements(nFlags)` iterates `m_Bodor_Elems` and calls `BElem->DoExport(this, nFlags)` which writes to `pCalc->m_OStream` (wostringstream).
3. `AddToBuff(m_OStream)` converts the stream to UTF-16LE and appends to `m_InpData` (wxMemoryBuffer).

### 6.5 Three-phase headers

`Add3PhHeadr()` writes the system header with:
- Calculation name, variant
- Frequency, operating state (max/min)
- Calculation type (PQ, short circuit, etc.)
- Newton method parameters (epsilon, max steps)

`AddPQChoHeadr()` writes PQ power flow headers.
`AddLimQHeadr()` writes reactive power limit headers.
`AddDGHeadr()` writes distributed generation headers.

---

## 7. File-based vs Memory-based Communication

**Both modes exist, but memory-based is the primary mode. File-based is used only for debugging and specific scenarios.**

### 7.1 Memory-based communication (primary)

#### Single-phase:
```cpp
// cl_Calculation::Do_Calculate() -- AN_Iface.cpp line 270
wxCharBuffer Buff = m_szInpData.mb_str(wxCSConv(wxFONTENCODING_CP1250));
const char *pBuff = Buff.data();
bool bOK = (m_pAN_Lib->ReadInpData(pBuff) != 0);   // pass text as CP1250 C-string
bOK = (m_pAN_Lib->RunAnalysis(false) != 0);          // false = no file output
DataDescr_T *pDataDesc = m_pAN_Lib->GetOutDataDescr(); // get pointer to DLL's internal buffer
```

Input: text string in CP1250 encoding passed via `ReadInpData(const char*)`.
Output: `DataDescr_T*` pointer to DLL-internal binary buffer containing packed result data.

#### Three-phase:
```cpp
// cl_Calculation::Do_Calculate3() -- cl_Calculation.cpp line 310
// NEW_EXPORT path (preferred):
if (m_InpData.GetDataLen() > 0) {
    m_InpData.AppendData(Nulls, 4);   // null-terminate
    bOK = (pCurrent_Lib->ReadInpData((const char*)m_InpData.GetData()) != 0);  // UTF-16LE binary buffer
}
// Fallback (old path):
else {
    wxCharBuffer Buff = m_szInpData.mb_str(wxMBConvUTF16LE());
    bOK = (pCurrent_Lib->ReadInpData(pBuff) != 0);  // UTF-16LE encoded string
}

bOK = (pCurrent_Lib->RunAnalysis(nCalctype, bOutToFile) != 0);

// Results are retrieved per-element:
pCurrent_Lib->ReadNodeData(i);     // copies into m_uBuffer
pCurrent_Lib->ReadBranchData(i);   // copies into m_uBuffer
```

Input: UTF-16LE encoded text buffer passed via `ReadInpData(const char*)`.
Output: Results retrieved one element at a time via `GetNodeData` / `GetBranchData` which copy into the caller's 64KB buffer.

### 7.2 File-based communication (debug/alternative)

#### Single-phase file operations:
- `LoadInpDataFromFile(char*)` -- load input from file
- `SaveInpDataToFile(char*)` -- save input to file
- `SaveOutDataToFiles(char*)` -- save output to files
- `SetFileName(char*)` -- set output path (used in debug: `RunAnalysis(true)` enables file output)

#### Three-phase file operations:
- `SaveInpDataToFile(char*)` -- save input to file
- `SetFileName(const char*)` -- set output path for debug

#### Debug data saving in application code:
```cpp
// cl_OperCalc::PrepareData() writes debug input file:
if (EVlivy3App::GetApp()->m_bDebugData) {
    Out.Write(m_szInpData, wxCSConv(wxFONTENCODING_CP1250));  // single-phase
    Out.Write(m_szInpData, wxCSConv(wxFONTENCODING_UTF16LE)); // three-phase
    Out2.Write(m_InpData.GetData(), m_InpData.GetDataLen());   // binary buffer
}
```

### 7.3 Summary

| Aspect | Single-phase (cl_AN_Lib) | Three-phase (cl_AN3_Lib) |
|---|---|---|
| Input encoding | CP1250 text | UTF-16LE text |
| Input method | `ReadInpData(const char*)` | `ReadInpData(const char*)` |
| Input buffer | `m_szInpData` (wxString) | `m_InpData` (wxMemoryBuffer) or `m_szInpData` |
| Run method | `RunAnalysis(bool bOutToFile)` | `RunAnalysis(uint32_t calcType, int32_t bOutToFile)` |
| Output method | `GetOutDataDescr()` returns `DataDescr_T*` | `GetNodeData/GetBranchData` copy to caller buffer |
| Status check | `GetErrData()` -> parse "Finished: OK" | `CheckFinishStatus()` returns non-zero |
| File I/O | `LoadInpDataFromFile`, `SaveInpDataToFile`, `SaveOutDataToFiles` | `SaveInpDataToFile`, `SetFileName` |

**The "two modes" are: (a) in-memory text string passed to `ReadInpData` (normal operation), and (b) file-based via `LoadInpDataFromFile` / `SetFileName` + `RunAnalysis(true)` (debug/alternative). The primary, production mode is always memory-based.**

---

## 8. Result Extraction (cl_OperCalc)

**Files:** `c:\DNCalc\EVlivy\EVlivy3\include\cl_OperCalc.h`, `c:\DNCalc\EVlivy\EVlivy3\cl_OperCalc.cpp`

### 8.1 Single-phase result extraction

After `Do_Calculate()` returns `DataDescr_T*`:

1. `cl_Calculation::ProcessResult(pDataDesc)` sets the data pointer and reads the header (`Data_Header_T`).
2. `cl_OperCalc::ProcessResult(pDataDesc)` reads:
   - Reference voltage complex
   - Reference node complex
   - Base frequency complex
   - Skips Unom values
3. Depending on method, calls `ProcessResultNV()` or `ProcessResultNewt()`.

**ProcessResultNV** (nodal voltage method):
- For each harmonic: reads harmonic number, then for each node reads voltage, for each branch reads `Popis_T` descriptor + element ID + admittances.

**ProcessResultNewt** (Newton/Gauss-Seidel):
- For each harmonic: reads harmonic, then for each node reads voltage + power complex, for each branch reads `Popis_T` + element ID + three power complexes (S1, S2, S3).

### 8.2 Three-phase result extraction

After `Do_Calculate3()`, `ProcessResult3()` iterates:

```cpp
int32_t nNodes = pCurrent_Lib->GetNodeCount();
int32_t nBranches = pCurrent_Lib->GetBranchCount();

for (int i = 0; i < nNodes; i++) {
    pCurrent_Lib->ReadNodeData(i);           // copy to m_uBuffer
    nID = pCurrent_Lib->GetID(&nSubID);      // parse ID from buffer
    pResElem = m_pResult->Find_AN_Result(nID);
    pResElem->ExtractNodeResult(pCurrent_Lib);  // virtual method
}

for (int i = 0; i < nBranches; i++) {
    pCurrent_Lib->ReadBranchData(i);
    nID = pCurrent_Lib->GetID(&nSubID);
    pResElem->ExtractBranchResult(pCurrent_Lib);  // virtual method
}
```

### 8.3 Result data structures

#### cl_Node_Op_Result (single-phase node)

```cpp
class cl_Node_Op_Result : public cl_Node_Op_Base {
    std::complex<double>  m_cfU;     // voltage phasor
    double                m_fdUn;    // voltage deviation [%]
    double                m_fdU;     // voltage deviation [kV]
    std::complex<double>  m_cfZ;     // Thevenin impedance
};
```

#### cl_Node_Op_Result3 (three-phase node)

```cpp
class cl_Node_Op_Result3 : public cl_Node_Op_Base {
    std::complex<double>  m_cfUf[MAX_PHASES];       // phase voltages (A, B, C, N)
    std::complex<double>  m_cfU[MAX_COMP_VOLT];      // symmetrical component voltages (3)
    double                m_fdUnf[MAX_PHASES];        // phase voltage deviations [%]
    double                m_fdUn[MAX_COMP_VOLT];      // component voltage deviations
    std::complex<double>  m_cfZk[MAX_PHASES];         // Thevenin impedances per phase
    std::complex<double>  m_cfZk_Sym[MAX_PHASES];     // symmetrical component impedances
    std::complex<double>  m_cfSk_Sym[MAX_PHASES];     // short-circuit powers
    double                m_fZk_R0R1;                  // zero/positive sequence R ratio
    double                m_fZk_X0X1;                  // zero/positive sequence X ratio
    std::complex<double>  m_cfZk_1f;                   // single-phase short-circuit impedance
    std::complex<double>  m_cfZk_3f;                   // three-phase short-circuit impedance
    int32_t               m_nDeltaUnErrLevel[MAX_PHASES]; // voltage error levels
    bool                  m_bAsymErr;                  // asymmetry error flag
    bool                  m_bOverCurrent;              // overcurrent flag
};
```

#### cl_Elem_Op_Result (single-phase branch)

```cpp
class cl_Elem_Op_Result : public cl_Elem_Op_Base {
    std::complex<double>  m_cfI[MAX_NODE_TERMINALS];  // current phasors (up to 3 terminals)
    std::complex<double>  m_cfZ[MAX_NODE_TERMINALS];  // impedances
    double                m_fP[MAX_NODE_TERMINALS];    // active power [MW]
    double                m_fQ[MAX_NODE_TERMINALS];    // reactive power [Mvar]
    double                m_fS[MAX_NODE_TERMINALS];    // apparent power [MVA]
};
```

#### cl_Elem_Op_Result3 (three-phase branch)

```cpp
class cl_Elem_Op_Result3 : public cl_Elem_Op_Base {
    std::complex<double>  m_cfI[MAX_NODE_TERMINALS][MAX_PHASES];   // currents per terminal per phase
    std::complex<double>  m_cfS[MAX_NODE_TERMINALS][MAX_PHASES];   // power per terminal per phase
    std::complex<double>  m_cfSs[MAX_NODE_TERMINALS];              // total power per terminal
    int32_t               m_nOverCurrentLevel[MAX_NODE_TERMINALS][MAX_PHASES];  // overcurrent levels
    bool                  m_bOverPower[MAX_NODE_TERMINALS];        // overpower flags
};
```

### 8.4 Three-phase data extraction helpers

`cl_AN3_Lib` provides helper methods that cast the internal buffer and extract values:

```cpp
void Get_Uf(std::complex<double> *pDst, uint8_t nCalcMethod);       // phase voltages
void Get_USymComp(std::complex<double> *pDst, uint8_t nCalcMethod); // symmetrical components
void Get_I(std::complex<double> *pDst, int nTerminal, uint8_t nCalcMethod);  // branch currents
void Get_S(std::complex<double> *pDst, int nTerminal, uint8_t nCalcMethod);  // branch powers
void Get_Zk(std::complex<double> *pDst, uint32_t nIndex, uint8_t nCalcMethod); // Thevenin impedances
void Get_Ik(std::complex<double> *pDst, uint8_t nSCType, uint8_t nCalcMethod); // SC fault currents
void Get_Uk(std::complex<double> *pDst, uint8_t nSCType, uint8_t nCalcMethod); // SC fault voltages
// ... etc.
```

The `nCalcMethod` parameter controls the number of phases extracted (`nCalcMethod & 0x07`) and whether to use 3-wire or 4-wire structures (`nCalcMethod >= CALC_METH_NEW_BASE`).

### 8.5 Values displayed in results

| Category | Values |
|---|---|
| Node voltages | Uf (phase), U (line), dUn (deviation %), Zk (Thevenin), Sk (short-circuit power) |
| Branch currents | Ia, Ib, Ic, In (per phase), phasors with magnitude and angle |
| Branch powers | Pa, Pb, Pc (active per phase), P total, Qa, Qb, Qc (reactive), Q total, S (apparent) |
| Losses | DeltaS per phase and total |
| Impedances | Za, Zb, Zc (per phase), with angle |
| Asymmetry | Unbalance factor, asymmetry factor |
| Short-circuit | Ik'', Ip (peak), Ith (thermal), Ib (breaking), cos(fi) |

---

## 9. Three-Phase Export (Bodor Format)

**Files:** `c:\DNCalc\EVlivy\EVlivy3\Elem_3ph_Export.h`, `c:\DNCalc\EVlivy\EVlivy3\Elem_3ph_Export.cpp`

The "Bodor format" is the input text format expected by the 3-phase DLL (named after the DLL author). Elements are serialized as tab-separated lines written via `std::wostringstream`.

### 9.1 cl_Bodor_Element hierarchy

```
cl_Bodor_Element (abstract base)
  |-- cl_VK_Element    (line/cable - VK)
  |-- cl_TR_Element    (2-winding transformer - TR)
  |   |-- cl_T3_Element (3-winding transformer - T3)
  |-- cl_SI_Element    (power source / supply node - SI)
  |-- cl_PQ_Element    (PQ load - PQ)
  |-- cl_ZQ_Element    (impedance load - ZQ)
  |-- cl_FV_Element    (photovoltaic/DG source - FV)
  |-- cl_M3_Element    (three-phase motor - M3)
  |-- cl_SG_Element    (synchronous generator - SG)
  |-- cl_VY_Element    (switch/breaker - VY)
  |-- cl_RE_Element    (reactor - RE)
  |-- cl_L3_Element    (choke/inductor - L3)
  |-- cl_KB_Element    (capacitor bank - KB)
  |-- cl_QK_Element    (compensation element - QK)
  |-- cl_ZN_Element    (grounding impedance - ZN)
```

### 9.2 cl_Bodor_Element base

```cpp
class cl_Bodor_Element {
    cl_Scheme_Element      *m_pScheme_Element;    // link to scheme element
    cl_AN_Result_Element   *m_pResult_Element;    // link to result container
    uint32_t                m_nSubIx;             // sub-index for multi-element decomposition

    virtual void DoExport(cl_Calculation *pCalc, uint32_t nFlags = 0) = 0;
    wxString GetIDString();    // returns "<XXXXXXXX>" format ID
    uint64_t GetID();          // returns m_nID + (m_nSubIx << 32)
};
```

### 9.3 ID encoding

Element IDs in the Bodor format use a specific string encoding:
- Simple: `<XXXXXXXX>` (10 chars: `<` + 8-digit padded ID + `>`)
- With sub-index: `<XXXXXXXX-NNN>` (14 chars: `<` + 8-digit ID + `-` + 3-digit sub-index + `>`)

The `GetID()` method on `cl_AN3_Lib` parses these back, supporting both formats. Fictive nodes (starting with "Fik") are skipped.

### 9.4 Export format example (VK line element)

```cpp
void cl_VK_Element::DoExport(cl_Calculation *pCalc, uint32_t nFlags) {
    pCalc->m_OStream << nodeIDs << '\t'
        << "VK" << '\t'              // element type code
        << m_szPhases << '\t'        // e.g. "ABC" or "ABCN"
        << m_fRk << '\t'             // specific resistance [ohm/km]
        << m_fLk << '\t'             // specific inductance [mH/km]
        << m_fGk << '\t'             // specific conductance [uS/km]
        << m_fCk << '\t'             // specific capacitance [nF/km]
        << m_fCrossSect << '\t'      // cross section [mm2]
        << m_fLength << '\t'         // length [km]
        << m_fR0R1 << '\t'           // zero/positive R ratio
        << m_fX0X1 << '\t'           // zero/positive X ratio
        << m_fB0B1 << '\t'           // zero/positive B ratio
        << m_fImax << '\t'           // max current [A]
        << '"' << idString << '"'    // element ID
        << "\r\n";
}
```

### 9.5 Two-step creation process (NEW_EXPORT)

1. **AddData()** -- called on each `cl_Scheme_Element`, creates a `cl_Bodor_Element` and inserts into `m_pResult->m_Bodor_Elems` map (keyed by `uint64_t` composite ID):
   ```cpp
   cl_Bodor_Element_UPtr uElement = std::make_unique<cl_VK_Element>(...);
   pCalc->m_pResult->m_Bodor_Elems.insert(std::make_pair(uElement->GetID(), std::move(uElement)));
   ```

2. **ExportElements()** -- iterates the map and serializes each element:
   ```cpp
   for (auto iter = m_pResult->m_Bodor_Elems.begin(); ...) {
       BElem->DoExport(this, nFlags);
       AddToBuff(m_OStream);        // converts wostringstream -> UTF-16LE -> m_InpData
   }
   ```

---

## 10. Calculation Types

### 10.1 Single-phase calculation types

Determined by the calculation class used:

| Class | Type constant | Description |
|---|---|---|
| `cl_OperCalc` | `CALC_OPERATION` (1) | Power flow (steady-state) |
| `cl_ShortCircCalc` | -- | Short-circuit calculation |
| `cl_FreqCalc` | -- | Frequency characteristic |
| `cl_HarmCalc` | -- | Harmonic analysis |
| `cl_HDO_Calc` | -- | HDO (ripple control) signal propagation |
| `cl_Flikr_Calc` | -- | Flicker calculation |
| `cl_LoadConnCalc` | -- | Load connectivity |
| `cl_DeltaOperCalc` | -- | Differential power flow (delta Sk) |
| `cl_Contingency_Calc` | -- | Contingency analysis (N-1) |

### 10.2 Three-phase calculation types (Calc3_Kind_T)

```cpp
enum Calc3_Kind_T {
    dvAll  = 0,    // all calculation types
    dvHar  = 1,    // harmonic analysis
    dvFch  = 2,    // frequency characteristic of nodal impedance
    dvHdo  = 3,    // HDO signal propagation
    dvLin  = 4,    // steady-state as linear problem
    dvChod = 5,    // steady-state as nonlinear problem (power flow)
    dvZkr  = 6,    // short circuits
    dvPre  = 7,    // reserved (phase interruption)
    dvZSp  = 8,    // earth fault
    dvStat = 9,    // static stability
    dvDyn  = 10,   // dynamic stability
    dvHpf  = 11,   // power flow with nonlinear loads
    dvKon  = 12,   // contingency analysis
    dvFli  = 13,   // flicker propagation
    dvNes  = 14,   // asymmetric load in single-phase model
    dvZth  = 15,   // Thevenin nodal impedances
    dvEpf  = 16,   // power flow with nonlinear loads + HDO
    dvOpf  = 20,   // optimal power flow
    dvOrpf = 21,   // optimal reactive power flow
};
```

### 10.3 Result type identifiers

```cpp
#define RESULT_OPERATION      1
#define RESULT_SHORT_CIRCUIT  4
#define RESULT_NODE_FREQCHAR  6
#define RESULT_HARMONICS      7
#define RESULT_HDO            9
#define RESULT_FLIKR          10
#define RESULT_CONTINGENCY    24
#define RESULT_3_POL          0x8000   // OR'd with above for 3-phase results
```

### 10.4 Calculation method encoding

```cpp
#define CALC_METH_OLD_SYM       0x01   // old symmetric (single-phase equivalent)
#define CALC_METH_3V            0x0B   // 3-wire
#define CALC_METH_3V_ISLE       0x0C   // 3-wire island mode
#define CALC_METH_NEW_BASE      0x10   // threshold: >= this uses new 4-wire DLL
#define CALC_METH_NEW_SYM       0x11   // new symmetric (1 phase shown)
#define CALC_METH_NEW_3V        0x13   // new 3-wire (3 phases shown)
#define CALC_METH_NEW_4V        0x14   // new 4-wire (4 phases shown)
#define CALC_METH_NEW_ISLE_MSK  0x20   // island mode bitmask
#define CALC_METH_NEW_ISLE      0x21   // island mode
```

The low 3 bits (`& 0x07`) encode the number of phases to display/extract.

---

## 11. Error Handling

### 11.1 Pre-calculation checks

`cl_Calculation::Check(wxString &szReason)` performs:
- Misconfigured elements check (`m_lstMisConf`)
- Unsupported element types in single-phase mode
- Topology analysis (`m_pScheme->Topology()`)
- Voltage level consistency (`CheckVoltLevels()`)
- 4-wire compatibility (`Check4W()`)
- Demo element count limits

Returns: `CHECK_OK` (0), `CHECK_BAD_ELEM` (1), `CHECK_ERRORS_LISTED` (2), `CHECK_WARNINGS_LISTED` (3), `CHECK_SHOW_ERROR` (4), `CHECK_ABORT` (5).

### 11.2 Single-phase error detection

After `RunAnalysis()`:
1. Check return value of `RunAnalysis()` -- zero means failure. Error from `GetStatusDescr()->m_szErrorMsg`.
2. Check `GetErrData()` output for `"Finished: OK"` substring. If not found, extract error description.
3. Check `GetOutDataDescr()` for null or `!m_bDataReady`.

```cpp
// Example from Do_Calculate():
bOK = (m_pAN_Lib->RunAnalysis(false) != 0);
if (!bOK)
    throw new BASE_EXCEPTION(... + wxString((m_pAN_Lib->GetStatusDescr())->m_szErrorMsg));

wxString szErrData(m_pAN_Lib->GetErrData());
int nPos = szErrData.Find(wxT("Finished: "));
// ... parse result status
```

### 11.3 Three-phase error detection

After `RunAnalysis()`:
1. Check `RunAnalysis()` return value. Error from `GetErrorMsg()` (wide string).
2. Check `CheckFinishStatus()` return value. Error from `GetErrorMsg()`.
3. Individual `ReadNodeData` / `ReadBranchData` calls check return value (0 = error), throw exceptions.

### 11.4 Post-calculation error checking

`cl_AN_Result::CheckErrors3()` and element-specific `CheckErrors3()` methods check:
- Voltage deviations exceeding limits
- Asymmetry exceeding thresholds
- Overcurrent conditions
- Overpower conditions

These set error level members (e.g., `m_nDeltaUnErrLevel[]`, `m_bAsymErr`, `m_nOverCurrentLevel[][]`).

---

## 12. Calculation Modifiers

**File:** `c:\DNCalc\EVlivy\EVlivy3\include\cl_Calculation.h`, lines 122-133

### 12.1 cl_Elem_Modifier

```cpp
class cl_Elem_Modifier {
    cl_Scheme_Element *m_pActElement;   // currently modified element

    virtual void Modify(cl_Scheme_Element *pElem);
    virtual void UnModify(cl_Scheme_Element *pElem);
};
```

### 12.2 Modifier array

`cl_Calculation` holds up to 16 modifiers:

```cpp
#define MAX_Modifiers  16
cl_Elem_Modifier *m_pModifier[MAX_Modifiers];
```

### 12.3 Usage pattern

Modifiers are applied temporarily during data preparation. Each scheme element has a `m_nModifier` field. If non-zero, the corresponding modifier is applied before and removed after serialization:

```cpp
// In AddElements() / AppendElement():
if (pElem->m_nModifier != 0)
    Modify(pElem);
pElem->m_bInCalculation |= pElem->AddData(this, CalcType, nFlags);
if (pElem->m_nModifier != 0)
    UnModify(pElem);
```

```cpp
void cl_Calculation::Modify(cl_Scheme_Element *pElem) {
    // delegates to appropriate modifier in m_pModifier[] array
}
```

Modifiers allow temporary parameter changes for sensitivity analysis, regulation iteration, or scenario evaluation without permanently altering scheme element data. For example, during voltage regulation iteration, transformer tap positions or generator reactive power setpoints may be temporarily modified.

### 12.4 Export flags

Flags control what data is exported and how:

```cpp
#define EXP_FLAG_SHORT         0x0001  // short-circuit mode
#define EXP_FLAG_LOAD_PQ_ONLY  0x0002  // export loads as PQ only
#define EXP_FLAG_LOAD_ZQ_ONLY  0x0004  // export loads as ZQ only
#define EXP_FLAG_POSITIVE      0x0008  // positive power only
#define EXP_FLAG_HA            0x0010  // harmonic analysis
#define EXP_FLAG_REGULATION    0x0020  // regulation mode (use regulated tap positions)
#define EXP_FLAG_SK_CONTRIB    0x0040  // short-circuit contribution
#define EXP_FLAG_TIME_SLICES   0x0080  // time slices mode
#define EXP_FLAG_FLIKR         0x0100  // flicker calculation
#define EXP_FLAG_ISLE          0x0200  // island mode
#define EXP_FLAG_OPTIMIZE      0x0400  // optimization (OPF)
#define EXP_FLAG_ADJUST        0x0800  // adjustment mode
#define EXP_FLAG_CONTINGENCY   0x1000  // contingency analysis (N-1)
```

---

## Appendix A: Encoding Summary

| Context | Encoding |
|---|---|
| Single-phase input to DLL | CP1250 (Windows Central European) |
| Three-phase input to DLL | UTF-16LE |
| Single-phase output from DLL | Binary packed struct via `DataDescr_T*` |
| Three-phase output from DLL | Binary packed struct copied via `GetNodeData/GetBranchData` |
| Error messages (single-phase) | CP1250 char* |
| Error messages (three-phase) | UTF-16LE wchar_t* |
| Element IDs in Bodor format | `<XXXXXXXX>` or `<XXXXXXXX-NNN>` wide strings |

## Appendix B: Key File References

| File | Purpose |
|---|---|
| `c:\DNCalc\EVlivy\EVlivy3\AN_Iface.h` | Single-phase DLL interface, data structures |
| `c:\DNCalc\EVlivy\EVlivy3\AN_Iface.cpp` | Single-phase DLL loading, function resolution |
| `c:\DNCalc\EVlivy\EVlivy3\AN3_Iface.h` | Three-phase DLL interface, packed data structures |
| `c:\DNCalc\EVlivy\EVlivy3\AN3_Iface.cpp` | Three-phase DLL loading, data extraction helpers |
| `c:\DNCalc\EVlivy\EVlivy3\include\cl_Calculation.h` | Base calculation class, modifiers, element preparation declarations |
| `c:\DNCalc\EVlivy\EVlivy3\cl_Calculation.cpp` | Data preparation implementation, DLL invocation |
| `c:\DNCalc\EVlivy\EVlivy3\include\cl_OperCalc.h` | Operational calculation, result classes |
| `c:\DNCalc\EVlivy\EVlivy3\cl_OperCalc.cpp` | Power flow preparation, 3-phase data assembly, result parsing |
| `c:\DNCalc\EVlivy\EVlivy3\Elem_3ph_Export.h` | Bodor element hierarchy for 3-phase export |
| `c:\DNCalc\EVlivy\EVlivy3\Elem_3ph_Export.cpp` | Element serialization to Bodor format |
| `c:\DNCalc\EVlivy\EVlivy3\cl_Calc_Test.cpp` | Calculation testing framework |
| `c:\DNCalc\EVlivy\EVlivy3\include\cl_Scheme.h` | CALC_METH_* constants |
| `c:\DNCalc\EVlivy\EVlivy3\cl_AN_Result.h` | Result type constants, result element base classes |
