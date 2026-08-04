# IEC 104 / TCP Communication System Analysis

## EVlivy3 (DNCoRS) <-> dncors_iec104 SCADA Service

**Author:** Analysis generated from source code
**Source Files Analyzed:**
- `DB_104.h` / `DB_104.cpp` -- IEC 104 database classes
- `cl_104_Connector.h` / `cl_104_Connector.cpp` -- TCP connection, state machine, calculation pipeline
- `DNCoRS_Data.h` / `DNCoRS_Data.cpp` -- Regulation data manager
- `DNCoRS_Filter.cpp` -- Data quality filtering
- `SMU_Interface.h` / `SMU_Interface.cpp` -- Modbus TCP server for external simulation
- `Commands_CliTst.cpp` -- Command execution (client-side)
- `cl_DNCoRS_Pnl.cpp` -- DNCoRS control panel (data flow)
- `include/Serializable.h` -- TLV serialization framework
- `DNCoRS.ini` / `SMU.ini` -- Configuration files

---

## 1. Architecture Overview

EVlivy3 is an electrical network calculation application (DNCalc) that, when compiled with `_VOLTAGE_CTRL_` defined, becomes **DNCoRS** -- a Distribution Network Control and Regulation System. It communicates with a separate **dncors_iec104** service that acts as a bridge between the IEC 60870-5-104 SCADA world and the EVlivy3 calculation engine.

### Component Roles

| Component | Role |
|---|---|
| **dncors_iec104** | External service. IEC 104 master/slave that connects to SCADA RTUs. Stores telemetry data in a time-series manner. Exposes a TCP socket interface for EVlivy3. |
| **cl_104_Connector** | The central orchestrator in EVlivy3. Manages TCP connection to dncors_iec104, registers IEC 104 elements, requests data, runs calculation pipeline, sends control commands back. Inherits from `cl_Cmd_Dest`. |
| **cl_104_Rx** | Background receive thread. Runs in a dedicated `std::thread`, manages Winsock socket, receives TLV-encoded commands from dncors_iec104, posts them to the GUI thread via `wxThreadEvent`. |
| **cl_104_DB** | SQLite3-based database mapping IEC 104 addresses to scheme elements. Contains lookup tables for measurement types, units, and the many-to-many relationship between IEC 104 items and DNCalc elements. |
| **cl_DNCoRS_Data** | Manages regulation parameters (mode, voltage limits, reactive power setpoints). Persists state to a `.d104.ini` file. Sends parameter updates to dncors_iec104 via Poke commands. |
| **Commands (TLV-serialized)** | A command pattern built on `cl_SerializableObject`. Each command is serialized via TLV (Tag-Length-Value), transmitted over TCP, deserialized on the other side, and executed via virtual `Exec()`. |
| **cl_ModBusServer (SMU)** | A separate Modbus TCP server that allows external simulation tools (SMU) to trigger calculations and read results. |

### High-Level Architecture Diagram

```
+------------------+     IEC 60870-5-104     +------------------+
|   SCADA / RTU    | <---------------------> |  dncors_iec104   |
| (field devices)  |                         |    service       |
+------------------+                         +--------+---------+
                                                      |
                                              TCP socket (TLV protocol)
                                                      |
                                             +--------+---------+
                                             |    EVlivy3       |
                                             |  (cl_104_Connector|
                                             |   + cl_104_Rx)   |
                                             +--------+---------+
                                                      |
                                              +-------+--------+
                                              |  cl_104_DB     |
                                              |  (SQLite3)     |
                                              +-------+--------+
                                                      |
                                              +-------+--------+
                                              |  Scheme Model  |
                                              |  (cl_Scheme)   |
                                              +----------------+
```

---

## 2. TCP Connection

### Connection Establishment

The TCP connection is managed by `cl_104_Rx`, which runs in a dedicated background thread (`std::thread`).

**Method:** `cl_104_Rx::Run()`

1. **Winsock initialization:** `WSAStartup(MAKEWORD(2, 2), &wsaData)`
2. **Socket creation:** `socket(AF_INET, SOCK_STREAM, 0)` -- standard TCP socket
3. **Connection loop:** Repeatedly calls `connect(m_Sockfd, ...)` every 200ms until successful or `m_bStop` is set
4. **On success:** Posts `wxEVT_104_ConnState` with `CON_STATE_CONNECTED` to the GUI thread

### Server Address Configuration

Read from the scheme-specific INI file (`<scheme_name>.ini`):

```ini
[Config]
Server_104=<hostname or "localhost">
Port_104=<port number>
Link_104=<IEC 104 link identifier string>
```

- If `Server_104` is `"localhost"`, uses `wxIPV4address::LocalHost()`
- Otherwise uses `wxIPV4address::Hostname(szServer_104)`

### Receive Loop (Background Thread)

After connection, `cl_104_Rx::Run()` enters a receive loop:

```
while (true):
    1. select() with 100ms timeout, checking for m_bStop
    2. SockRcv(buffer, sizeof(tlv_head_t))     -- read 8-byte TLV header
    3. SockRcv(buffer + 8, head.nLength)        -- read TLV payload
    4. TLV_Rx.ReadTLV(nullptr, buffer, RX_BUFF_LEN)
    5. pRxCmd = dynamic_cast<cl_Command*>(TLV_Rx.Deserialize())
    6. Push pRxCmd to m_lstCommand (protected by wxCriticalSection)
    7. Post wxEVT_104_Rx to GUI thread
```

### Reconnection Logic

On any socket error (`SockRcv` returning false, select failure, or deserialization error):
1. `close(m_Sockfd)` and set `m_Sockfd = -1`
2. If not `m_bStop` and still `m_bRunning`: post `CON_STATE_RECONNECTING` event, then `goto Re_establish` to re-create the socket and retry connecting.

### Thread Safety

- **m_csCmdLst:** `wxCriticalSection` guards `m_lstCommand` (a `std::deque<cl_Command*>`)
- **m_bStop / m_bConnAllowed:** `volatile bool` flags for thread control
- GUI thread processes commands in `cl_104_Connector::On_104_Rx()` by draining the deque

### Send Path

`cl_104_Connector::Send(cl_Command *pCommand)`:

```cpp
cl_Serializer TLV;
TLV.Serialize(pCommand);
uint8_t *pTx_Buff;
uint32_t Cmd_len = TLV.WriteTLVArchive(&pTx_Buff, 0);  // no compression
int nRes = send(m_104_Rx.m_Sockfd, (const char*)pTx_Buff, Cmd_len, 0);
free(pTx_Buff);
```

- Send is called from the **GUI thread** (not the Rx thread)
- Uses the same socket fd `m_104_Rx.m_Sockfd`
- Compression level 0 (no compression) for the TLV archive

---

## 3. Data Format -- TLV Wire Protocol

### TLV Header Structure

Defined in `Serializable.h`:

```cpp
#pragma pack(push,1)
typedef struct {
    uint32_t nTag;      // Object type tag
    uint32_t nLength;   // Payload length in bytes
} tlv_head_t;
```

- **Total header:** 8 bytes, packed (no padding)
- **Wire format:** `[4-byte tag][4-byte length][length bytes of payload]`

### Serialization

The `cl_Serializer` class provides a complete TLV serialization framework:

- **Serialize:** Object -> TLV binary stream. Each attribute is written as `[tag][length][value]`. Nested objects are recursively serialized.
- **Deserialize:** TLV binary stream -> Object. Uses `CreateObjectByTag(nTag)` factory pattern to instantiate the correct class.

### Supported Data Types in TLV

| Method | Tag Size | Value Format |
|---|---|---|
| `AddTLV_Bool` | 4+4 | 1 byte boolean |
| `AddTLV_U8` | 4+4 | 1 byte uint8 |
| `AddTLV_U16` | 4+4 | 2 bytes uint16 |
| `AddTLV_I16` | 4+4 | 2 bytes int16 |
| `AddTLV_U32` | 4+4 | 4 bytes uint32 |
| `AddTLV_I32` | 4+4 | 4 bytes int32 |
| `AddTLV_U64` | 4+4 | 8 bytes uint64 |
| `AddTLV_Dbl` | 4+4 | 8 bytes IEEE 754 double |
| `AddTLV_CD` | 4+4 | 16 bytes (two doubles: real, imag) |
| `AddTLV_Date` | 4+4 | wxDateTime value |
| `AddTLV_UTF8` | 4+4 | UTF-8 encoded string |
| `AddTLV_UTF16` | 4+4 | UTF-16 encoded string |

### WriteTLVArchive

The `WriteTLVArchive()` method wraps the serialized TLV stream with an outer `tlv_head_t` header, optionally compressing with bzip2 (compression level parameter, magic `0x564C5458` = "VLTX" for compressed). For TCP transmission, compression level 0 is used (uncompressed).

### Message Flow

```
[tlv_head_t: outer tag + total length] [TLV payload of serialized command]
```

On receive:
1. Read 8 bytes (outer `tlv_head_t`)
2. Read `head.nLength` bytes (payload)
3. `ReadTLV()` parses the payload
4. `Deserialize()` reconstructs the command object

---

## 4. IEC 104 Data Model

### cl_104_item -- IEC 104 Measurement Point

Represents a single IEC 104 data point (measurement, status, command). Stored in SQLite table `item_104`.

```cpp
class cl_104_item : public cl_SQLite_Object {
    uint64_t   m_nID;                    // Database primary key
    wxString   m_szsjz_mereni;           // SJZ measurement identifier
    wxString   m_szsjz_gis;             // SJZ GIS identifier
    wxString   m_szsjz_drs;             // SJZ DRS identifier
    wxString   m_sziec104_adresa;        // IEC 104 address as string "X.X.X.X.X"
    uint64_t   m_n104_Addr;             // IEC 104 address as 40-bit integer
    uint64_t   m_ndruh_mereni_id;       // FK to druh_mereni (measurement kind)
    uint64_t   m_ntyp_id;              // FK to typ (measurement type: P, Q, U, State, Branch...)
    int        m_npriorita;             // Priority level
    uint64_t   m_njednotka_id;          // FK to jednotka (unit)
    double     m_fnasobitel;            // Multiplier (scaling factor)
    wxString   m_szrozsah_min;          // Range minimum (string)
    wxString   m_szrozsah_max;          // Range maximum (string)
    wxString   m_sztrida_presnosti;     // Accuracy class
    double     m_fkoeficient_duveryhodnosti; // Reliability coefficient
    wxTimeSpan m_dtcasovy_limit;        // Time validity limit
    double     m_fmax;                  // Maximum valid value
    double     m_fmin;                  // Minimum valid value
    wxString   m_szBranchXlate;         // Branch translation table name

    // Runtime data (not persisted)
    double     m_fValue;                // Current value (after multiplier)
    uint32_t   m_nQuality;             // IEC 104 quality descriptor
    wxDateTime m_dtTime;               // Timestamp of last data
    bool       m_bActive;              // Whether item is registered for data
    bool       m_bHasNewData;          // Flag set when new data arrives

    std::vector<cl_dncalc_item*>  m_lstDNC_Obj;  // Linked DNCalc elements
    cl_BranchXlate  *m_pBranchXlate;             // Branch number translation
};
```

### IEC 104 Address Format

The address is a 5-component dotted string, e.g., `"1.0.0.100.5"`, parsed by `cl_104_item::StrToAddr()`:

```cpp
bool StrToAddr(wxString szStr, uint64_t &nValue, bool bHex = false) {
    // Tokenize by "."
    // Expects exactly 5 tokens
    // Packs into 40-bit integer: byte[4].byte[3].byte[2].byte[1].byte[0]
    // nVal += (uint64_t)nTmp << (8 * i);  // i = 4..0
}
```

This creates a 5-byte (40-bit) unique address packed into a `uint64_t`.

### cl_dncalc_item -- DNCalc Scheme Element Mapping

Represents the link between the IEC 104 world and the DNCalc scheme model. Stored in SQLite table `item_dn`.

```cpp
class cl_dncalc_item : public cl_SQLite_Object {
    uint64_t   m_ndncors_id;          // Matches cl_Scheme_Element::m_nID
    wxString   m_szoblast_vn;         // HV area name
    wxString   m_szcelek;             // Unit/group name
    wxString   m_szoznaceni_celku;    // Unit designation
    wxString   m_szhladina;           // Voltage level
    bool       m_benabled;            // Enabled for processing
    bool       m_bMonitored;          // PQ split accuracy check element
    bool       m_bControled;          // Output: control values sent via IEC 104
    bool       m_bVirtPQ;             // Part of virtual PQ diagram
    bool       m_bBoundaryElem;       // Boundary switch (must be open for calculation)

    cl_Scheme_Element        *m_pElem;        // Pointer to scheme element
    std::deque<cl_104_item*>  m_lst104_Obj;   // Linked IEC 104 items
};
```

### Relationship

The relationship between `cl_104_item` and `cl_dncalc_item` is **many-to-many**, linked via the `iec104_to_dncalc` SQLite table. A single scheme element (e.g., a transformer) can have multiple IEC 104 measurements (P, Q, U, tap position), and a single IEC 104 measurement can theoretically map to multiple scheme elements.

### cl_Main_104_Item -- System-Level Control Points

These are "main" IEC 104 items used for system-level control signals (not per-element data), stored in table `main_104`:

```cpp
class cl_Main_104_Item {
    uint64_t   m_nID;                  // Database primary key
    wxString   m_szSJZ;               // SJZ identifier
    wxString   m_szIEC104_Adress;     // Primary IEC 104 address
    wxString   m_szIEC104_ACK_Adress; // Acknowledgment address
    uint64_t   m_n104_Addr;           // Parsed primary address
    uint64_t   m_n104_ACK_Addr;       // Parsed ACK address
    wxString   m_szName;              // Descriptive name
    bool       m_bInput;              // true = input (from SCADA), false = output (to SCADA)
    uint8_t    m_n104Type;            // IEC 104 type ID

    // Runtime data
    double     m_fValue;
    uint32_t   m_nQuality;
    wxDateTime m_dtTime;
};
```

Main items use the `MAIN_104_Flag` (a high bit flag on the ID) to distinguish them from regular element items during registration and data exchange.

---

## 5. cl_104_DB -- IEC 104 Database

### Database Path

The SQLite3 database file is co-located with the scheme file:
```
<scheme_name>.db3
```

### SQLite Schema (Tables)

| Table | Columns | Purpose |
|---|---|---|
| `druh_mereni` | `id, name` | Measurement kind lookup (vyvodove, vyrobna, transformator, pripojnicove, usecnik, odpinac, ...) |
| `jednotka` | `id, name` | Unit lookup (kV, MW, MVAr, ...) |
| `typ` | `id, name, command, unit` | Type lookup with command flag. Known IDs: 1=State, 2=Branch, 3=U, 4=I, 5=Q, 6=P, 7=Branch2, 8=SetU, 9=SetQ, 10=SetBranch, 14=SetUopt |
| `item_104` | `id, sjz_mereni, sjz_gis, sjz_drs, iec104_adresa, druh_mereni_id, typ_id, priorita, jednotka_id, nasobitel, rozsah_min, rozsah_max, trida_presnosti, koeficient_duveryhodnosti, casovy_limit, min, max, branch_xlt` | IEC 104 measurement points |
| `item_dn` | `dncors_id, oblast_vn, celek, oznaceni_celku, hladina, enabled, monitored, controlled, virt_pq, boundary` | DNCalc scheme element mappings |
| `iec104_to_dncalc` | `id_104, id_dncalc` | Many-to-many link table |
| `main_104` | `id, sjz, iec104_adresa, input, name, ack_iec104_adresa, iec104_type` | System-level control points |

### Initialization Sequence

`cl_104_DB::Init(cl_Scheme *pScheme)`:

1. Set `m_szDB_Path` to `<scheme>.db3`
2. Load all lookup tables: `druh_mereni`, `jednotka`, `typ`
3. Load `item_104` -- all IEC 104 measurement definitions
4. Load `main_104` -- system control items
5. Load `item_dn` -- DNCalc element mappings (resolves `m_pElem` via `pScheme->FindID()`)
6. Load `iec104_to_dncalc` -- creates bidirectional links: `cl_104_item::m_lstDNC_Obj` <-> `cl_dncalc_item::m_lst104_Obj`

### CRUD Operations

All table sets provide:
- `LoadSet(szWhere)` -- SELECT and populate in-memory map
- `Create(item, pDB)` -- INSERT with auto-generated ID
- `Update(item, pDB)` -- UPDATE by primary key

SQL generation is automatic via `Get_SELECT_Stmt()`, `Get_INSERT_Stmt()`, `Get_UPDATE_Stmt()` based on `GetColumns()` and `GetSetName()`.

### Import from Tab-Separated Files

`cl_104_DB::DoImport(szFile)` imports from a tab-separated text file with 22+ columns per line:

```
Col A: dncalcname      Col B: dncalcid       Col C: Multiplier
Col D: iec104_adresa   Col E: sjz_mereni     Col F: sjz_gis
Col G: sjz_drs         Col H: oblast_vn      Col I: celek
Col J: oznaceni_celku  Col K: druh_mereni     Col L: typ
Col M: hladina         Col N: priorita       Col O: jednotka
Col P: rozsah_min      Col Q: rozsah_max     Col R: trida_presnosti
Col S: koef_duveryhodnosti  Col T: casovy_limit  Col U: max  Col V: min
```

The import clears all existing data (`DELETE FROM`) before inserting.

### Branch Translation (cl_BranchXlate)

Some transformers use different tap numbering between CEZ (utility) and DNCalc internal numbering. Translation tables are loaded from `.xlt` files (tab-separated, two columns: CEZ_branch, DNC_branch). A static map `cl_104_item::m_mapBranchXlate` caches loaded translation tables.

---

## 6. SendData_to_DRS() -- Sending Parameters to dncors_iec104

`cl_DNCoRS_Data::SendData_to_DRS()` sends the current regulation parameters to the dncors_iec104 service:

```cpp
void cl_DNCoRS_Data::SendData_to_DRS() {
    m_pParent->Poke_SP(ID_104_Active     | MAIN_104_Flag, m_bActive,       CS101_COT_SPONTANEOUS);
    m_pParent->Poke_SP(ID_104_RegBranch  | MAIN_104_Flag, m_bBrnch_Reg,    CS101_COT_SPONTANEOUS);
    m_pParent->Poke_DP(ID_104_RegMode    | MAIN_104_Flag, m_RegMode,       CS101_COT_SPONTANEOUS);
    m_pParent->Poke   (ID_104_UNet_max   | MAIN_104_Flag, m_fUnet[1],      CS101_COT_SPONTANEOUS);
    m_pParent->Poke   (ID_104_UNet_min   | MAIN_104_Flag, m_fUnet[0],      CS101_COT_SPONTANEOUS);
    m_pParent->Poke   (ID_104_Qvvn       | MAIN_104_Flag, m_fQvvn / 1.e6,  CS101_COT_SPONTANEOUS);
    m_pParent->Poke   (ID_104_Q_tor      | MAIN_104_Flag, m_fQtol / 1.e6,  CS101_COT_SPONTANEOUS);
}
```

### Poke Methods

Three variants exist for sending values:

```cpp
// Float value
void Poke(uint64_t n104_ID, double fValue, uint8_t nCOT = CS101_COT_SPONTANEOUS) {
    auto uCmd = make_unique<cl_Poke_Command>(n104_ID);
    uCmd->m_lstValue.push_back(make_unique<cl_Float_Poke_Value>(fValue, nCOT));
    Send(uCmd.get());
}

// Single-point (boolean) value
void Poke_SP(uint64_t n104_ID, bool bValue, uint8_t nCOT) {
    auto uCmd = make_unique<cl_Poke_Command>(n104_ID);
    uCmd->m_lstValue.push_back(make_unique<cl_Bool_Poke_Value>(bValue, nCOT));
    Send(uCmd.get());
}

// Double-point (2-bit state) value
void Poke_DP(uint64_t n104_ID, int nValue, uint8_t nCOT) {
    auto uCmd = make_unique<cl_Poke_Command>(n104_ID);
    uCmd->m_lstValue.push_back(make_unique<cl_4State_Poke_Value>(nValue & 0x03, nCOT));
    Send(uCmd.get());
}
```

All poke commands use `CS101_COT_SPONTANEOUS` as the IEC 104 Cause of Transmission.

### When SendData_to_DRS() is Called

Called in `cl_104_Connector::Reg_DoneOK()` -- after all elements have been successfully registered with the dncors_iec104 service.

---

## 7. Data Reception

### Command Flow: SCADA -> EVlivy3

1. **dncors_iec104** collects IEC 104 telemetry from SCADA RTUs
2. EVlivy3 sends `cl_Get_Data_Cmd` (with timestamp) to request data
3. dncors_iec104 responds with `cl_Data_Answer` containing a list of `cl_Elem_104_Value` objects

### cl_Data_Answer Processing (Commands_CliTst.cpp)

```cpp
void cl_Data_Answer::Exec(cl_Cmd_Dest *pDest) {
    cl_104_Connector *pDst = static_cast<cl_104_Connector*>(pDest);

    for (auto iter = m_Value.begin(); iter != m_Value.end(); ++iter) {
        cl_Elem_104_Value *pValue = (*iter).get();

        cl_104_item *p104 = pDst->m_104_DB.Find_104_Addr(pValue->m_nAddress);
        if (p104 != nullptr) {
            // Apply multiplier (nasobitel) -- if zero, use 1.0
            p104->m_fValue = pValue->m_fValue * (Double_EQ(p104->m_fnasobitel, 0.) ? 1. : p104->m_fnasobitel);
            p104->m_dtTime = pValue->m_dtLastData;
            p104->m_nQuality = pValue->m_nQuality;
            p104->m_bHasNewData = true;
        }
    }

    // Determine time range and signal DataOK
    pDst->SendEvent(NEXT_DATA_Answer, new cl_Repl_Init_Data(dtValFirst, dtValLast, !m_bFinal));
}
```

Key point: The multiplier `m_fnasobitel` is applied at receive time, scaling the raw IEC 104 value.

### Incoming Poke Commands (from SCADA via dncors_iec104)

```cpp
void cl_Poke_Command::Exec(cl_Cmd_Dest *pDest) {
    cl_104_Connector *pDst = static_cast<cl_104_Connector*>(pDest);
    // Strip MAIN_104_Flag and call SetParameter
    pDst->m_Data.SetParameter(m_nElementID & (~MAIN_104_Flag), pFPv->m_fValue, pFPv->m_nCOT);
}
```

This allows SCADA operators to remotely change DNCoRS parameters (activate/deactivate, change regulation mode, set voltage limits, etc.).

### Data Application to Scheme Elements

After data reception, `cl_104_Connector::UpdateValues()` iterates all `cl_dncalc_item` entries and calls `pDN_Item->m_pElem->Set104Values(pDN_Item, this)` to apply IEC 104 data to the scheme model. Values are always applied even if invalid -- the filter stage handles invalid data separately.

---

## 8. DNCoRS_Data -- Regulation System

### Regulation Modes (`Reg_Mode_T`)

| Enum | Name (Czech) | Description |
|---|---|---|
| `rm_BasicQ` (0) | Regulace Q | Basic reactive power regulation |
| `rm_MinTransfQ` (1) | Min pretok Q | Minimize Q transfer at HV/MV boundary |
| `rm_MinLoss` (2) | Min ztraty | Minimize active power losses |
| `rm_None` (3) | Neregulace | No regulation (passive monitoring) |

### System States (`State_T`)

| Enum | Name | Description |
|---|---|---|
| `st_Off` | Vypnut | System off |
| `st_OK` | OK | Normal operation |
| `st_Err` | Chyba | Error state |

### Calculation Stages (`Stage_T`)

| Enum | Description |
|---|---|
| `sg_CalcQmin` | Calculating minimum reactive power |
| `sg_CalcQmax` | Calculating maximum reactive power |
| `sg_CalcLoss` | Calculating system losses |
| `sg_CalcReg` | Calculating regulation values |

### Parameters

| Parameter | INI Key | Default | Unit | Description |
|---|---|---|---|---|
| `m_bActive` | `/Config/Active` | true | bool | DNCoRS active/inactive |
| `m_RegMode` | `/Config/Mode` | rm_BasicQ | enum | Regulation mode |
| `m_bBrnch_Reg` | `/Config/BrnchReg` | false | bool | Transformer tap regulation enabled |
| `m_fUnet[0]` | `/Config/Unetmin` | -8.0 | % | Minimum allowed network voltage deviation |
| `m_fUnet[1]` | `/Config/Unetmax` | 8.0 | % | Maximum allowed network voltage deviation |
| `m_fQvvn` | `/Config/Qvvn` | 1e6 | VAr | Reactive power setpoint at HV/MV boundary |
| `m_fQtol` | `/Config/Qtol` | 5e6 | VAr | Reactive power tolerance |

### Voltage Control Logic (in Perform_Calculation, DO_CALC_CONTROLL)

The control stage sends computed values back to SCADA:

1. **Virtual PQ** values (Qmin, Qmax, losses) are sent via `SendVirtPQ()`
2. **System state** (OK/Error) sent via `Poke_SP(ID_104_State)`
3. For each **controlled element** (`m_bControled == true`):
   - **Type 8 (SetU):** Sends calculated node voltage `m_fUcalc / m_fnasobitel`
   - **Type 14 (SetUopt):** Sends optimal voltage `m_fUopt / m_fnasobitel` with conditional logic based on regulation mode:
     - `rm_BasicQ`: Set if voltage deviation exceeds limits
     - `rm_MinLoss`: Set if optimization yields >delta_Ploss% loss reduction
     - `rm_MinTransfQ`: Set if actual Q deviates from Qvvn beyond tolerance
     - `rm_None`: Never set
   - **Type 9 (SetQ):** Sends operational reactive power value
   - **Type 10 (SetBranch):** Sends transformer tap position (with optional CEZ/DNC branch translation)

---

## 9. SMU Interface (Out of Scope)

**Note**: SMU (Station Management Unit) is a **separate, standalone** Modbus TCP server interface that is **NOT part of the IEC 104/DNCoRS SCADA communication system** and **does NOT affect DLL calculation data processing**. It provides a separate channel for external simulation tools to trigger calculations via Modbus TCP protocol.

**Files (excluded from IEC 104/SCADA analysis)**:
- `SMU_Interface.h` — Modbus TCP server class definitions
- `SMU_Interface.cpp` — Implementation (commands, register handling, calculation triggering)
- `SMU.ini` — Configuration (default port: 502)

**Key distinction**: While SMU can trigger `cl_OperCalc` calculations, it operates independently from the IEC 104 data flow and does not exchange data with dncors_iec104. It is a parallel interface for external control, not part of the SCADA integration architecture.

---

## 10. Configuration

### Scheme-Specific INI (`<scheme_name>.ini`)

| Section/Key | Type | Description |
|---|---|---|
| `/Config/Mode` | int | 0=Edit, 1=Offline calc, 2=Online (IEC 104) |
| `/Config/Server_104` | string | dncors_iec104 hostname |
| `/Config/Port_104` | int | dncors_iec104 TCP port |
| `/Config/Link_104` | string | IEC 104 link identifier |
| `/Config/Step_sec` | int | Data polling interval (seconds, multiplied by 1000 for ms) |
| `/Config/Calc_Kind` | int | Calculation sequence code (see below) |
| `/Config/Log_Level` | int | Log verbosity |
| `/Config/Save_Src_Data` | bool | Save source data snapshots |
| `/Config/GetVoltageSetpoint` | bool | Use AN3_Lib voltage setpoint calculation |
| `/Config/Do_Filter` | bool | Enable data quality filter |
| `/Config/Filter_104` | bool | Check IEC 104 quality bits |
| `/Config/Filter_Value` | bool | Check value range |
| `/Config/Filter_Time` | bool | Check data freshness |
| `/Config/Boundary_Check` | bool | Check boundary switches |
| `/Config/Simple_Check` | bool | Simple validity check |
| `/Config/U_OPT_Ctrl` | bool | Enable optimal voltage control |
| `/Config/dLoss` | string | Loss delta threshold (%) |
| `/Config/P_Rounds` | int | PQ split P iteration rounds |
| `/Config/Q_Rounds` | int | PQ split Q iteration rounds |
| `/Config/P_Accur` | string | PQ split P accuracy (%) |
| `/Config/Q_Accur` | string | PQ split Q accuracy (%) |
| `/Config/Delta_Q` | string | Q delta threshold (%) |
| `/Config/Q_threshold` | string | Q threshold (kVAr) |
| `/Config/PQ_Split_Section` | string | External PQ split section (if non-empty, skip internal PQ split) |
| `/Debug/Step_Result` | bool | Save every calculation step result |
| `/Debug/Change_FileNames` | bool | Use timestamped debug file names |
| `/Debug/Save_Result` | bool | Save results |

### Calc_Kind Values

| Value | Sequence | Description |
|---|---|---|
| 1 | OPER | Operation calculation only |
| 2 | OPER, SPLIT | PQ split then operation |
| 3 | OPTIMIZE | Optimization only |
| 4 | OPTIMIZE, SPLIT | PQ split then optimization |
| 5 | QMIN | Calculate Q minimum |
| 6 | QMAX | Calculate Q maximum |
| 7 | LOSS | Calculate losses |
| 8 | QMIN, QMAX, LOSS | All three |
| 9 | QMIN, QMAX, LOSS, OPTIMIZE, CONTROL | Full regulation cycle |
| 19 | SPLIT, QMIN, QMAX, LOSS, OPTIMIZE, CONTROL | Full with PQ split |
| 21 | OPER, CONTROL | Operation + send controls |
| 22 | OPTIMIZE, CONTROL | Optimization + send controls |
| 23 | SPLIT, OPTIMIZE, CONTROL | PQ split + optimization + controls |
| 24 | SPLIT, QMIN, QMAX, OPTIMIZE, CONTROL | PQ split + Qmin/max + optimization + controls |

### Scheme-Specific Data INI (`<scheme_name>.d104.ini`)

Managed by `cl_DNCoRS_Data`, persists regulation parameters:

| Key | Description |
|---|---|
| `/Config/Active` | DNCoRS active state |
| `/Config/Mode` | Regulation mode (0-3) |
| `/Config/BrnchReg` | Branch regulation enabled |
| `/Config/Unetmin` | Min voltage deviation (%) |
| `/Config/Unetmax` | Max voltage deviation (%) |
| `/Config/Qvvn` | Q setpoint (VAr) |
| `/Config/Qtol` | Q tolerance (VAr) |

### DNCoRS.ini (Application-Level)

This is the main application configuration file (used when compiled as DNCoRS). It contains general application settings (window positions, grid settings, color schemes, font settings, result display options, etc.) -- not specific to IEC 104 communication.

**Note**: `SMU.ini` exists but is for the separate Modbus TCP interface (see Section 9), not for IEC 104/SCADA communication.

---

## 11. Data Flow Diagrams

### Direction: SCADA -> dncors_iec104 -> EVlivy3 (Data Acquisition)

```
Step 1: Connection
    EVlivy3 cl_104_Rx::Run()
        -> socket(AF_INET, SOCK_STREAM, 0)
        -> connect(sockfd, server_addr)
        -> post wxEVT_104_ConnState(CON_STATE_CONNECTED)

Step 2: Initialization
    EVlivy3 On_104_ConnState()
        -> Create cl_Init_Cmd(m_szLink_104)
        -> Send(cl_Init_Cmd)              // TLV over TCP

    dncors_iec104
        -> Responds with cl_Init_Answer(bInit_OK, nMode)

    EVlivy3 cl_Init_Answer::Exec()
        -> m_nMode_104 = nMode            // Mode_IEC104 or Mode_Replay
        -> SendEvent(NEXT_Start_Elem_Reg)

Step 3: Element Registration (batched)
    EVlivy3 Elem_Reg(true)
        -> Create cl_Reg_Elems_Cmd with up to ELEM_REG_RECORDS_MAX cl_Elem_Stub entries
           Each stub: {nID, n104_Addr, bCommand, bMainItem}
           For main items also: n104_ACK_Addr, n104Type
        -> Send(cl_Reg_Elems_Cmd)

    dncors_iec104
        -> Registers requested IEC 104 addresses for monitoring
        -> Responds with cl_Reg_Elems_Answer(bOK)

    EVlivy3 cl_Reg_Elems_Answer::Exec()
        -> If more elements remain: SendEvent(NEXT_Continue_Elem_Reg)
        -> When all done: Reg_DoneOK()
            -> SendData_to_DRS()          // Send current regulation params
            -> If Mode_IEC104: GetData()  // Request current data
            -> If Mode_Replay: SendInit() // Initialize replay

Step 4: Data Request (periodic in Mode_IEC104)
    EVlivy3 GetData(dtTime)
        -> Do_GetData(true)
        -> Send(cl_Get_Data_Cmd(dtTime, bStart))

    dncors_iec104
        -> Queries stored IEC 104 data since dtTime
        -> Responds with cl_Data_Answer containing cl_Elem_104_Value list
           Each value: {m_nAddress (40-bit), m_fValue, m_dtLastData, m_nQuality}
           Multiple responses possible (m_bFinal flag)

    EVlivy3 cl_Data_Answer::Exec()
        -> For each value: find cl_104_item by address, apply multiplier, store
        -> SendEvent(NEXT_DATA_Answer)

Step 5: Calculation Pipeline
    EVlivy3 DataOK()
        -> UpdateValues()                 // Apply IEC 104 data to scheme elements
        -> Filter(pCalcScheme)            // Data quality check
        -> Check_Boundary(pCalcScheme)    // Boundary switch check
        -> Execute m_nCalc_Sequence:
            DO_CALC_SAVE -> DO_CALC_SPLIT -> DO_CALC_OPER -> DO_CALC_QMIN
            -> DO_CALC_QMAX -> DO_CALC_LOSS -> DO_CALC_OPTIMIZE -> DO_CALC_CONTROLL
        -> Start timer for next cycle (m_InStep_mSec)
```

### Direction: EVlivy3 -> dncors_iec104 -> SCADA (Control Output)

```
Step 1: Calculation Complete (DO_CALC_CONTROLL stage)
    EVlivy3 Perform_Calculation()
        -> FillVirtPQ(pCalcScheme)
        -> SendVirtPQ():
            Poke(ID_104_Q_min,  Qmin / 1e6)
            Poke(ID_104_Q_max,  Qmax / 1e6)
            Poke(ID_104_Losses, dP / 1e6)
        -> Poke_SP(ID_104_State, bCalcOK)

Step 2: Per-Element Control Values
    For each controlled element (m_bControled):
        For each IEC 104 item with command flag (pTyp->m_bCommand):
            - Type 8 (SetU):      Poke(id, Ucalc / nasobitel)
            - Type 14 (SetUopt):  Poke(id, Uopt / nasobitel)  [conditional]
            - Type 9 (SetQ):      Poke(id, Q_oper_sgn)
            - Type 10 (SetBranch): Poke(id, tap_position)    [with branch xlate]

Step 3: Wire Format
    Each Poke creates cl_Poke_Command:
        -> cl_Serializer.Serialize(pCommand)
        -> cl_Serializer.WriteTLVArchive(&pTx_Buff, 0)
        -> send(sockfd, pTx_Buff, Cmd_len, 0)

Step 4: dncors_iec104
    -> Deserializes cl_Poke_Command
    -> Translates to IEC 60870-5-104 commands
    -> Sends to SCADA RTU via IEC 104 protocol
```

---

## 12. DNCoRS Filter -- Data Quality Filtering

Implemented in `DNCoRS_Filter.cpp` as `cl_104_Connector::Filter()` and supporting methods.

### Filter Configuration

| Flag | INI Key | Default | Description |
|---|---|---|---|
| `m_bDo_Filter` | `Do_Filter` | true | Master filter enable |
| `m_bFilter_104` | `Filter_104` | true | Check IEC 104 quality descriptor (bit 7 = invalid) |
| `m_bFilter_Value` | `Filter_Value` | true | Check value within min/max range |
| `m_bFilter_Time` | `Filter_Time` | true | Check data freshness against `casovy_limit` |
| `m_bBoundary_Check` | `Boundary_Check` | true | Verify boundary switches are open |

### HasValidValue() -- Per-Item Validation

```cpp
bool cl_104_item::HasValidValue(cl_Scheme_Element *pElem, cl_104_Connector *pConnector, bool bReport) {
    // 1. Quality check: bit 7 (0x80) of m_nQuality = invalid
    if (m_bFilter_104 && (m_nQuality & 0x80))
        return false;

    // 2. Time check: if casovy_limit > 0 and data is older than limit
    if (m_bFilter_Time && (m_dtcasovy_limit.GetMinutes() > 0))
        if (dtNow > m_dtTime + m_dtcasovy_limit)
            return false;

    // 3. Value range check
    if (m_bFilter_Value) {
        if (element is switch):
            // value > 2.05 is error state
            if (m_fValue > 2.05) return false;
        else:
            // Check against m_fmin/m_fmax (scaled by |m_fnasobitel|)
            if (value outside [min*mult, max*mult]) return false;
    }
    return true;
}
```

### Check_Boundary() -- Boundary Switch Verification

Iterates `m_lstBoundary_Elems` and verifies all boundary switches are OFF (open). If any boundary switch is closed, calculation is aborted. This prevents calculation errors when the network topology does not match the expected configuration.

### Filter() -- Two-Stage Data Quality Filter

The main filter in `DNCoRS_Filter.cpp` implements a sophisticated two-stage approach:

**Stage A1: Switch State Validation**
1. Check all switch elements for valid IEC 104 data
2. For switches with invalid data: force switch ON (closed)
3. Run operational calculation
4. Check if forced-ON switches have voltage on their nodes
5. If voltage present on an invalid switch: **FAIL** (switch state matters but is unknown)

**Stage A2: Measurement Validation**
1. For all non-switch, non-power-element measurements:
   - If branch/tap data invalid: **FAIL** (critical for transformers)
   - If U/P/Q data invalid:
     - Run calc if not done yet
     - Check if the node has voltage (> 2V)
     - If no voltage: skip (dead section, data doesn't matter)
     - If has voltage: look up in `mapFilter` table

### mapFilter -- Static Filter Decision Table

A hardcoded lookup table maps `{druh_mereni, typ, priorita}` to pass/fail decisions:

```cpp
uint64_t nKey = (druh_mereni_id << 32) | (typ_id << 16) | priorita;
```

Key entries (false = fail, true = pass):

| Measurement Kind | Type | Priority | Decision |
|---|---|---|---|
| pripojnicove (busbar) | U | 1 | FAIL |
| pripojnicove | U | 3 | PASS |
| transformator | Branch | 1 | FAIL |
| transformator | P | 3 | PASS |
| transformator | Q | 3 | PASS |
| vyvodove (feeder) | P | 1 | FAIL |
| vyvodove | Q | 1 | FAIL |
| vyrobna (generator) | P | 2 | FAIL |
| vyrobna | Q | 2 | FAIL |
| vyrobna | U | 3 | PASS |
| vyrobna | U | 2 | FAIL |
| vypinac (breaker) | State | 1 | FAIL |
| odpinac (disconnector) | State | 1 | FAIL |
| odpojovac (isolator) | State | 1 | FAIL |
| usecnik (sectionalizer) | State | 1 | FAIL |

The logic is: high-priority measurements of critical types cause calculation failure; lower-priority or busbar-level measurements can be tolerated as invalid.

---

## Summary of Key Constants

| Constant | Value | Description |
|---|---|---|
| `RX_BUFF_LEN` | 128 * 1024 (131072) | Receive buffer size |
| `ELEM_REG_RECORDS_MAX` | Not visible in analyzed files, used for batching | Max elements per registration command |
| `MAIN_104_Flag` | High bit on ID | Distinguishes main_104 items from regular items |
| `CS101_COT_SPONTANEOUS` | IEC 104 standard | Cause of transmission for spontaneous data |
| `CS101_COT_ACTIVATION` | IEC 104 standard | Cause of transmission for activation commands |
| `tlv_head_t` size | 8 bytes | TLV header: 4-byte tag + 4-byte length |
| Socket timeout | 100ms | select() timeout in receive loop |
| Connect retry | 200ms | Delay between connection attempts |

---

## Conclusion

The EVlivy3/DNCoRS IEC 104 communication system is a comprehensive SCADA integration layer that bridges electrical network calculations with real-time control systems. Key architectural characteristics:

### Communication Architecture
- **Protocol**: Custom TLV-over-TCP (not standard IEC 104 frames, but IEC 104 addressing/semantics)
- **Topology**: Client (EVlivy3) → Server (dncors_iec104) → SCADA master
- **Threading**: Blocking socket I/O with 100ms select() timeout in dedicated receive thread
- **Reliability**: Automatic reconnection with 200ms retry interval, connection status monitoring

### Data Model
- **Mapping Database**: SQLite-based cl_104_DB maps scheme elements to IEC 104 addresses (ASDU, IOA)
- **Dual Item Types**: main_104 (real SCADA) vs regular items (internal/calculated)
- **Address Translation**: Element IDs ↔ IEC 104 addresses via translation tables
- **Measurement Types**: Analog (M_ME_NC_1), Digital (M_SP_NA_1), Commands (C_SC_NA_1, C_DC_NA_1)

### Data Flow
**EVlivy3 → dncors_iec104** (SendData_to_DRS):
1. Calculation completes (power flow, voltages, currents)
2. Results packed to TLV format (TAG_CLASS_SNDDATA_to_DRS)
3. Contains: calculation ID, timestamp, element parameters (U, P, Q, I), switch states
4. Sent via TCP socket
5. dncors_iec104 forwards to SCADA master using standard IEC 104 frames

**dncors_iec104 → EVlivy3** (Data Reception):
1. SCADA measurements received by dncors_iec104
2. Packed to TLV format (TAG_CLASS_DN_DATA)
3. Sent via TCP to EVlivy3
4. Parsed and applied to scheme elements
5. Can trigger automatic recalculation with updated measurements

### Integration Points
- **DNCoRS Filter**: Data quality validation based on element type and measurement priority
- **Regulation System**: Voltage control modes (auto, manual, RO, voltage, cosφ, Q control)
- **Note**: SMU (Modbus TCP) is a separate parallel interface, not part of the IEC 104/SCADA data flow

### Use Cases
1. **Real-time Monitoring**: Display live SCADA measurements on diagram
2. **State Estimation**: Calculation driven by real measurements
3. **Voltage Control**: Automated tap changer and reactive power control
4. **Remote Control**: Issue switch commands via SCADA
5. **Recording/Replay**: Log measurement sequences for offline analysis

The system enables EVlivy3 to function not just as an offline calculation tool, but as an active participant in network control and monitoring infrastructure.
