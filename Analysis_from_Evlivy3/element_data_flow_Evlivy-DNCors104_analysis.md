# Element Data Flow Analysis

## EVlivy3 ↔ dncors_iec104

---

## Overview: Two Independent Sources, One Merge Point

The critical insight is that element configuration lives in **two separate places** — one in each application — and they are **merged during the TCP RegisterElements handshake** at startup.

```
EVlivy3 (SQLite DB)                  dncors_iec104 (XChng.cfg)
     │                                        │
     │       ┌──────────────────────┐         │
     └──────►│   RegisterElements   │◄────────┘
             │    (TLV over TCP)    │
             │   EVlivy3 sends,     │
             │  dncors receives     │
             └──────────────────────┘
```

Both applications must agree on: IEC104 addresses, element IDs, and flags. **If XChng.cfg and the DB are inconsistent, elements silently fail.**

---

## Part 1: EVlivy3 — Element Sources

### 1.1 The SQLite Database (`<scheme>.db3`)

All element configuration in EVlivy3 comes from a SQLite database co-located with the scheme file. The database is initialized by `cl_104_DB::Init()` ([DB_104.cpp:1005](file:///c:\DNCalc\EVlivy\EVlivy3\DB_104.cpp#L1005)):

```cpp
m_szDB_Path = m_pScheme->m_szFileName.BeforeLast('.') + ".db3";
m_druh_mereni.LoadSet();   // lookup: measurement kinds
m_jednotka.LoadSet();      // lookup: units
m_typ.LoadSet();           // lookup: types (incl. command flag)
m_104_item.LoadSet();      // ← REGULAR IEC104 items (table: item_104)
m_Main_104_item.LoadSet(); // ← MAIN104 items        (table: main_104)
m_dn_item.LoadSet();       // ← DNCalc element links (table: item_dn)
cl_104_to_dn_set.LoadSet();// ← many:many links      (table: iec104_to_dncalc)
```

### 1.2 Regular Items (`item_104` table ↔ `cl_104_item`)

Columns: `id, sjz_mereni, sjz_gis, sjz_drs, iec104_adresa, druh_mereni_id, typ_id, priorita, jednotka_id, nasobitel, rozsah_min, rozsah_max, trida_presnosti, koeficient_duveryhodnosti, casovy_limit, min, max, branch_xlt`

Key fields used in registration:
| Field | Used for |
|---|---|
| `m_nID` | Unique item DB id, sent as `cl_Elem_Stub.m_nID` |
| `m_n104_Addr` | 5-byte packed IEC104 address, sent as `cl_Elem_Stub.m_n104Addr` |
| `m_ntyp_id` → `typ.command` | If `typ.command=true` → `bSetPoint=true` in the stub |
| `m_lstDNC_Obj` | Linked DNCalc scheme elements (via `iec104_to_dncalc`) |

**Active filter in [cl_104_Connector.cpp:657-674](file:///c:\DNCalc\EVlivy\EVlivy3\cl_104_Connector.cpp#L657):** Only items linked to **at least one non-passive, enabled** `cl_dncalc_item` are sent. Items with no valid scheme element are skipped silently.

### 1.3 Main104 Items (`main_104` table ↔ `cl_Main_104_Item`)

Columns: `id, sjz, iec104_adresa, input, name, ack_iec104_adresa, iec104_type`

These are **system-level control/regulation parameters** (not per-grid-element data):

| `id` | `#define`           | Meaning                  |
| ---- | ------------------- | ------------------------ |
| 1    | `ID_104_Active`     | Regulation on/off        |
| 2    | `ID_104_RegMode`    | Regulation mode          |
| 3    | `ID_104_RegBranch`  | Regulated branch         |
| 4    | `ID_104_UNet_max`   | Max voltage limit        |
| 5    | `ID_104_UNet_min`   | Min voltage limit        |
| 6    | `ID_104_Qvvn`       | Reactive power reference |
| 7    | `ID_104_Q_tor`      | Reactive power tolerance |
| 101  | `ID_104_Active_ACK` | Status acknowledgment    |
| 102  | `ID_104_State`      | Current state            |
| 103  | `ID_104_Q_min`      | Q min limit              |
| 104  | `ID_104_Q_max`      | Q max limit              |
| 105  | `ID_104_Losses`     | Losses                   |
| 106  | `ID_104_Weak`       | Weak node                |

Key fields for registration:

- `m_n104_Addr` → primary IEC104 address
- `m_n104_ACK_Addr` → acknowledgment address (sent on ACK channel after SCADA command)
- `m_bInput` → `true` = SCADA→DNC (input), `false` = DNC→SCADA (output/setpoint)
- `m_n104Type` → IEC104 type identifier (M_SP_NA_1, M_ME_NC_1, etc.)

### 1.4 How EVlivy3 Builds the RegisterElements Message

`cl_104_Connector::Elem_Reg()` ([cl_104_Connector.cpp:627](file:///c:\DNCalc\EVlivy\EVlivy3\cl_104_Connector.cpp#L627)) iterates both sets and creates `cl_Elem_Stub` objects in batches of up to `ELEM_REG_RECORDS_MAX=30`:

**Regular items** (from `item_104`):

```cpp
// bCmd = (typ.command == true)
pRegElems->m_Elems.push_back(
    new cl_Elem_Stub(p104->m_nID, p104->m_n104_Addr, bCmd, false)
    //               ID           104addr            bSetPoint  bPropagate=false
);
```

**Main104 items** (from `main_104`):

```cpp
cl_Elem_Stub *pElemStub = new cl_Elem_Stub(
    pMain_104->m_nID | MAIN_104_Flag,   // ID with flag (0xC0000000 OR-ed)
    pMain_104->m_n104_Addr,             // IEC104 address
    !pMain_104->m_bInput,               // output items are setpoints
    true                                // bPropagate=TRUE for all main items
);
pElemStub->m_n104_ACK_Adress = pMain_104->m_n104_ACK_Addr;
pElemStub->m_n104Type = pMain_104->m_n104Type;
```

> [!IMPORTANT]
> `bPropagate` is **always `false`** for regular items and **always `true`** for main104 items. This difference is **important** — it controls whether dncors_iec104 immediately pushes SCADA→DNC data without waiting for a GetData poll.

---

## Part 2: dncors_iec104 — Element Sources

### 2.1 XChng.cfg (Startup Configuration)

Loaded in `cl_MainApp::Init_Servers()` ([main.cpp:529](file:///c:\DNCalc\EVlivy\dncors_iec104\main.cpp#L529)) for each SCADA server subdirectory:

```
<exe>/Server/<server_name>/XChng.cfg
```

Parsed by `cl_104_Client::GetXChngCfg()`. Format (tab-separated):

```
IEC104_Address   ID   Type
1.0.0.100.5      1    30
1.0.0.100.6      2    13
```

**What it does:**

1. `FindElement(nAddr, true, true)` — creates a `cl_104_Element` in `m_Elements` map
2. `pElem->m_nCtrl_ID = nID | MAIN_104_Flag` — stamps with MAIN flag
3. `pElem->m_nType = nType` — sets IEC104 type (for interrogation responses)
4. `m_Interrog_Elements.push_back(pElem)` — add to interrogation list
5. Special: if `nID == ID_104_Active_ACK` → stored as `m_pStatus_Element` (sent to SCADA when DNC disconnects)

**What it does NOT do:** Does not set `m_bPropagate`, `m_nACK_Address`, `m_bSetPoint`. These come later from EVlivy3 via RegisterElements.

### 2.2 Runtime Elements (Created on Demand)

Regular elements (for SCADA telemetry) are created dynamically in `cl_104_Client::ASDU_ReceivedHandler()` when new SCADA data arrives for an address not yet known. `FindElement(nAddr, true, false)` creates them with `bParameter=false`.

---

## Part 3: The RegisterElements Merge (TCP Handshake)

When EVlivy3 connects and sends `cl_Reg_Elems_Cmd`, `Commands_Srv.cpp::cl_Reg_Elems_Cmd::Exec()` processes each `cl_Elem_Stub`:

```
For each stub:
    if (bSetPoint || (ID & MAIN_104_Flag)):
        pElem = FindElement(stub.m_n104Addr, true, true)  // find or create
        pElem->m_bPropagate  = stub.m_bPropagate           // ← MERGES from EVlivy3
        pElem->m_nACK_Address = stub.m_n104_ACK_Adress     // ← MERGES from EVlivy3
        pElem->m_bSetPoint = true
        m_CmdElements[stub.ID] = pElem
        m_CmdElement_IDs[pElem] = stub.ID
        if (ID & MAIN_104_Flag):
            AddInterrog(pElem)    // add to interrogation list (deduplicates)
    else:
        pElem = FindElement(stub.m_n104Addr, true, false)  // find or create
        pElem->m_bPropagate = stub.m_bPropagate   // always false for regular
        pElem->m_nACK_Address = stub.m_n104_ACK_Adress  // always 0 for regular
        m_Elements[stub.ID] = pElem
        m_Element_IDs[pElem] = stub.ID
```

**The merge result for Main104 elements:**

- XChng.cfg set: `m_nCtrl_ID`, `m_nType`, added to `m_Interrog_Elements`
- RegisterElements adds: `m_bPropagate=true`, `m_nACK_Address`, `m_bSetPoint=true`, adds to `m_CmdElements` + `m_CmdElement_IDs`

> [!NOTE]
> This is only a complete merge if **both** XChng.cfg and RegisterElements contain the same address. If an address is in XChng.cfg but not sent by EVlivy3 (or vice versa), registration is partial. The element exists in one lookup table but not the other.

---

## Part 4: Complete Runtime Data Flow

### 4.1 SCADA → dncors_iec104 → EVlivy3: Regular Items

```
SCADA RTU
  ↓ (IEC104 spontaneous / interrogation)
cl_104_Client::ASDU_ReceivedHandler()
  → FindElement(address) → pElem
  → pElem->NewData(pData)  // stores value + timestamp in element
  → if (pElem->m_bPropagate): [NOT triggered for regular items]

EVlivy3 periodically calls GetData:
  → cl_Get_Data_Cmd::Exec()
  → iterates m_Elements (monitor map only)
  → for each element with new data since last poll:
      → GetData() → sends cl_Data_Answer back
  → EVlivy3 applies values to scheme elements
```

### 4.2 SCADA → dncors_iec104 → EVlivy3: Main104 Items (Real-time Push)

```
SCADA RTU
  ↓ (IEC104 - setpoint command or measured value)
cl_104_Client::ASDU_ReceivedHandler()
  → FindElement(address) → pElem
  → pElem->NewData(pData)
  → if (pElem->m_bPropagate): [TRUE for main104 items!]
      → for each connected EVlivy3 client:
          → Find_CmdElement_ID(pElem) → nID (from m_CmdElement_IDs reverse map)
          → create cl_Poke_Command(nID)
          → pClient->m_Client_Rx.Send(poke)  // push immediately to EVlivy3
      → if (pElem->m_nACK_Address != 0):
          → echo command back to SCADA on ACK address  // IEC104 confirm protocol
```

> [!IMPORTANT]
> Main104 data goes via **reverse Poke** (immediate push), NOT via GetData polling. This is because main104 items are in `m_CmdElements`, not in `m_Elements`, so GetData never sees them.

### 4.2.1 EVlivy3 Side: What Happens When Reverse Poke Arrives

The incoming `cl_Poke_Command` is deserialized and executed on the EVlivy3 side by `cl_Poke_Command::Exec()` ([Commands_CliTst.cpp:162](file:///c:\DNCalc\EVlivy\EVlivy3\Commands_CliTst.cpp#L162)):

```cpp
void cl_Poke_Command::Exec(cl_Cmd_Dest *pDest)
{
    cl_104_Connector *pDst = static_cast<cl_104_Connector*>(pDest);

    for each value in m_lstValue:
        if TAG_CLASS_POKE_FLT:
            pDst->m_Data.SetParameter(
                m_nElementID & (~MAIN_104_Flag),  // strip flag → bare ID (1..7, 101..)
                pFPv->m_fValue,
                pFPv->m_nCOT
            );
        // TAG_CLASS_POKE_BOOL and TAG_CLASS_POKE_4STATE:
        //   received but NOT processed (commented out) — only float is handled
}
```

`SetParameter()` in [DNCoRS_Data.cpp:93](file:///c:\DNCalc\EVlivy\EVlivy3\DNCoRS_Data.cpp#L93) dispatches by bare ID:

| ID                     | Action                             | Side effect                                                                                                                        |
| ---------------------- | ---------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------- |
| `ID_104_Active` (1)    | `m_bActive = fValue > 0.1`         | Sends **Poke back** `ID_104_Active_ACK\|FLAG` to dncors_iec104 (status echo). Writes to `.d104.ini`. Logs activation/deactivation. |
| `ID_104_RegBranch` (3) | `m_bBrnch_Reg = fValue > 0.1`      | Writes to `.d104.ini`. Logs mode change.                                                                                           |
| `ID_104_RegMode` (2)   | `m_RegMode = round(fValue) & 0x03` | Writes to `.d104.ini`. Logs mode name.                                                                                             |
| `ID_104_UNet_max` (4)  | `m_fUnet[1] = fValue`              | Writes to `.d104.ini`. Logs value in %.                                                                                            |
| `ID_104_UNet_min` (5)  | `m_fUnet[0] = fValue`              | Writes to `.d104.ini`. Logs value in %.                                                                                            |
| `ID_104_Qvvn` (6)      | `m_fQvvn = fValue * 1e6`           | Writes to `.d104.ini`. Logs in MVAr.                                                                                               |
| `ID_104_Q_tor` (7)     | `m_fQtol = fValue * 1e6`           | Writes to `.d104.ini`. Logs in MVAr.                                                                                               |

**After every `SetParameter` call (regardless of ID):**

```cpp
m_pParent->m_pParent->m_pDNCoRS_Pnl->Fill();  // refresh the GUI control panel
```

> [!NOTE]
> **Persistence:** Every parameter change is immediately written to `<scheme>.d104.ini` via `wxFileConfig`. On next startup, `cl_DNCoRS_Data::Init()` reads these values back, so SCADA-driven parameter changes survive application restarts.

> [!NOTE]
> **Special case for `ID_104_Active`:** When SCADA activates regulation (`ID_104_Active = 1`), EVlivy3 immediately **echoes back** `Poke_SP(ID_104_Active_ACK | MAIN_104_Flag, true)` to dncors_iec104 → which then forwards it to SCADA as the acknowledgment status bit. This creates a status feedback loop: SCADA sends command → dncors_iec104 propagates → EVlivy3 stores + echoes ACK → dncors_iec104 forwards ACK → SCADA.

> [!WARNING]
> **Bool/4-State values ignored:** Only `TAG_CLASS_POKE_FLT` is acted upon. If dncors_iec104 sends a `POKE_BOOL` or `POKE_4STATE` reverse Poke (e.g., for a switch state), the value is silently discarded in the current implementation.

**Full round-trip for `ID_104_Active` (regulation on/off):**

```
SCADA sends C_SC_NA_1 (single command) to IEC104 address of ID_104_Active
  ↓
dncors_iec104: ASDU_ReceivedHandler → pElem->NewData()
  → m_bPropagate=true → reverse Poke (ID_104_Active|FLAG, value=1.0, COT)
  → ACK: Send_SingleCommand to ACK address (if configured)
  ↓
EVlivy3: cl_Poke_Command::Exec → SetParameter(ID_104_Active=1, 1.0)
  → m_bActive = true
  → Poke_SP(ID_104_Active_ACK|FLAG, true) → dncors_iec104 → SCADA (status)
  → write ".d104.ini /Config/Active = true"
  → m_pDNCoRS_Pnl->Fill()  ← GUI updates
```

#### 4.2.1.1 `.d104.ini` Persistence Layer

Every parameter change received via reverse Poke is **immediately persisted** to `<scheme>.d104.ini` using `wxFileConfig`. The file is located alongside the scheme file:

```cpp
// DNCoRS_Data.cpp:42
wxString szINI_File = pScheme->m_szFileName.BeforeLast('.') + ".d104.ini";
```

**All values stored in `.d104.ini`** (7 keys under `/Config/`):

| INI Key | Type | Default | Variable | Description |
|---|---|---|---|---|
| `/Config/Active` | bool | `true` | `m_bActive` | DNCoRS regulation on/off |
| `/Config/Mode` | int (0–3) | `0` (rm_BasicQ) | `m_RegMode` | Regulation mode |
| `/Config/BrnchReg` | bool | `false` | `m_bBrnch_Reg` | Transformer tap regulation enabled |
| `/Config/Unetmin` | double | `-8.0` (%) | `m_fUnet[0]` | Min allowed voltage deviation |
| `/Config/Unetmax` | double | `8.0` (%) | `m_fUnet[1]` | Max allowed voltage deviation |
| `/Config/Qvvn` | double | `1e6` (VAr) | `m_fQvvn` | Reactive power setpoint at HV/MV boundary |
| `/Config/Qtol` | double | `5e6` (VAr) | `m_fQtol` | Reactive power tolerance |

**Startup read** — `cl_DNCoRS_Data::Init()` ([DNCoRS_Data.cpp:40](file:///c:\DNCalc\EVlivy\EVlivy3\DNCoRS_Data.cpp#L40)):

```cpp
m_uConfig->Read(wxT("/Config/Active"), &m_bActive);
m_uConfig->Read(wxT("/Config/Mode"), &nTmp);       // cast to Reg_Mode_T
m_uConfig->Read(wxT("/Config/BrnchReg"), &m_bBrnch_Reg);
m_uConfig->Read(wxT("/Config/Unetmin"), &m_fUnet[0]);
m_uConfig->Read(wxT("/Config/Unetmax"), &m_fUnet[1]);
m_uConfig->Read(wxT("/Config/Qvvn"), &m_fQvvn);
m_uConfig->Read(wxT("/Config/Qtol"), &m_fQtol);
```

If the file doesn't exist or a key is missing, the constructor defaults (shown in the table above) are kept.

**Runtime write** — `SetParameter()` ([DNCoRS_Data.cpp:93](file:///c:\DNCalc\EVlivy\EVlivy3\DNCoRS_Data.cpp#L93)) writes the corresponding key immediately on each SCADA command. Example:

```cpp
case ID_104_Qvvn:
    m_fQvvn = fValue * 1.e6;                          // MVAr → VAr conversion
    m_uConfig->Write(wxT("/Config/Qvvn"), m_fQvvn);   // persist immediately
```

**On reconnect** — `SendData_to_DRS()` ([DNCoRS_Data.cpp:156](file:///c:\DNCalc\EVlivy\EVlivy3\DNCoRS_Data.cpp#L156)) pushes all 7 parameters back to dncors_iec104 as Poke commands when the TCP connection is (re)established, so SCADA receives the persisted state:

```cpp
m_pParent->Poke_SP(ID_104_Active | MAIN_104_Flag, m_bActive, CS101_COT_SPONTANEOUS);
m_pParent->Poke_SP(ID_104_RegBranch | MAIN_104_Flag, m_bBrnch_Reg, CS101_COT_SPONTANEOUS);
m_pParent->Poke_DP(ID_104_RegMode | MAIN_104_Flag, m_RegMode, CS101_COT_SPONTANEOUS);
m_pParent->Poke(ID_104_UNet_max | MAIN_104_Flag, m_fUnet[1], CS101_COT_SPONTANEOUS);
m_pParent->Poke(ID_104_UNet_min | MAIN_104_Flag, m_fUnet[0], CS101_COT_SPONTANEOUS);
m_pParent->Poke(ID_104_Qvvn | MAIN_104_Flag, m_fQvvn / 1.e6, CS101_COT_SPONTANEOUS);
m_pParent->Poke(ID_104_Q_tor | MAIN_104_Flag, m_fQtol / 1.e6, CS101_COT_SPONTANEOUS);
```

> [!NOTE]
> `Qvvn` and `Qtol` are stored internally in **VAr** but transmitted over IEC 104 in **MVAr** (divided by 1e6 on send, multiplied by 1e6 on receive).

#### 4.2.1.2 `.d104.ini` vs Main104 Elements — Comparison

The `.d104.ini` file persists only the **configurable input subset** of main104 elements. The full main104 list has 13 elements, of which only 7 are persisted:

| Bare ID | Define | Persisted in `.d104.ini`? | INI Key | Direction |
|---|---|---|---|---|
| 1 | `ID_104_Active` | **Yes** | `/Config/Active` | Bidirectional (SCADA ↔ DNC) |
| 2 | `ID_104_RegMode` | **Yes** | `/Config/Mode` | Bidirectional |
| 3 | `ID_104_RegBranch` | **Yes** | `/Config/BrnchReg` | Bidirectional |
| 4 | `ID_104_UNet_max` | **Yes** | `/Config/Unetmax` | Bidirectional |
| 5 | `ID_104_UNet_min` | **Yes** | `/Config/Unetmin` | Bidirectional |
| 6 | `ID_104_Qvvn` | **Yes** | `/Config/Qvvn` | Bidirectional |
| 7 | `ID_104_Q_tor` | **Yes** | `/Config/Qtol` | Bidirectional |
| 101 | `ID_104_Active_ACK` | No | — | Output only (DNC → SCADA) |
| 102 | `ID_104_State` | No | — | Output only |
| 103 | `ID_104_Q_min` | No | — | Output only |
| 104 | `ID_104_Q_max` | No | — | Output only |
| 105 | `ID_104_Losses` | No | — | Output only |
| 106 | `ID_104_Weak` | No | — | Output only |

**The lists are NOT equal.** The 6 output-only elements (IDs 101–106) are **calculated values** sent to SCADA each regulation cycle — they don't need persistence because they are recomputed. The `.d104.ini` file stores exactly the 7 operator-settable parameters that must survive application restarts.

#### 4.2.1.3 Alternative Parameter Source: Scheme File (`LoadFrom` / `SaveTo`)

The same 6 regulation parameters (all except `Active`) can also be loaded from and saved to the scheme file via `cl_DNCoRS_Data::LoadFrom()` / `SaveTo()` ([DNCoRS_Data.cpp:178-196](file:///c:\DNCalc\EVlivy\EVlivy3\DNCoRS_Data.cpp#L178)):

```cpp
void cl_DNCoRS_Data::LoadFrom(cl_Scheme *pScheme) {
    m_RegMode   = (Reg_Mode_T)pScheme->m_nRegMode;
    m_bBrnch_Reg = pScheme->m_bBrnch_Reg;
    m_fUnet[0]  = pScheme->m_fUnet[0];
    m_fUnet[1]  = pScheme->m_fUnet[1];
    m_fQvvn     = pScheme->m_fQvvn;
    m_fQtol     = pScheme->m_fQtol;
}
```

> [!NOTE]
> **Priority:** `.d104.ini` values (from SCADA commands) take priority over scheme defaults at runtime — the scheme values are only used as initial configuration or when resetting to defaults. The `Init()` method reads `.d104.ini` after construction, overriding constructor defaults.

### 4.3 EVlivy3 → dncors_iec104 → SCADA: Command/SetPoint Items

```
EVlivy3 calculation engine
  → sends cl_Poke_Command(ID, value)
  → cl_Poke_Command::Exec():
      → lookup in m_CmdElements → pElem
      → for float: Send_MeasuredValueShort(pElem, value, COT, false)
      → for bool:  SinglePointInformation(pElem, value, COT, false)
      → for dblpt: DoublePointInformation(pElem, value, COT, false)
      → update pElem->m_fValue and m_nType
  → IEC104 message sent to SCADA RTU
```

Both **setpoint elements** and **main104 elements** go through Poke → `m_CmdElements` lookup.

### 4.4 SCADA General Interrogation Response

```
SCADA sends C_IC_NA_1 (General Interrogation)
→ cl_104_Client::Rx_Interrogation()
→ iterate m_Interrog_Elements (contains main104 elements from BOTH XChng.cfg + RegisterElements)
→ for each element (type != 0):
    → use ACK address if set, else primary address
    → send current m_fValue as IEC104 ASDU (M_SP_NA_1 / M_DP_NA_1 / M_ME_NC_1)
```

This is the mechanism by which SCADA "pulls" the current state of regulation parameters (e.g., current voltage limits) from dncors_iec104.

---

## Part 5: Element Map Summary

| Map in `cl_Client` | What's in it             | Populated by                          | Used by                                |
| ------------------ | ------------------------ | ------------------------------------- | -------------------------------------- |
| `m_Elements`       | Regular monitor items    | RegisterElements (no setpoint flag)   | GetData polling                        |
| `m_Element_IDs`    | Reverse: element→ID      | RegisterElements                      | —                                      |
| `m_CmdElements`    | Setpoint + Main104 items | RegisterElements (setpoint/main flag) | Poke (DNC→SCADA)                       |
| `m_CmdElement_IDs` | Reverse: element→ID      | RegisterElements                      | Reverse Poke (SCADA→DNC via propagate) |

| Map/List in `cl_104_Client` | What's in it                       | Populated by                                |
| --------------------------- | ---------------------------------- | ------------------------------------------- |
| `m_Elements` (104 client)   | ALL elements by 104 address        | XChng.cfg + RegisterElements + ASDU receive |
| `m_Interrog_Elements`       | Main104 elements for interrogation | XChng.cfg + RegisterElements (main flag)    |
| `m_pStatus_Element`         | Special: ID_104_Active_ACK element | XChng.cfg                                   |

---

## Part 6: Data Sources Comparison

| Aspect                       | Regular Items                               | Main104 Items                    |
| ---------------------------- | ------------------------------------------- | -------------------------------- |
| **EVlivy3 source**           | SQLite `item_104` table                     | SQLite `main_104` table          |
| **Address source (EVlivy3)** | `m_sziec104_adresa` column                  | `m_szIEC104_Adress` column       |
| **ACK address**              | None (always 0)                             | `m_szIEC104_ACK_Adress` column   |
| **bPropagate sent**          | Always `false`                              | Always `true`                    |
| **bSetPoint**                | From `typ.command` flag in DB               | `!m_bInput` (output items)       |
| **dncors_iec104 source**     | Not pre-configured; created by ASDU receive | XChng.cfg (pre-startup)          |
| **ID flag**                  | Plain `p104->m_nID`                         | `pMain->m_nID \| MAIN_104_Flag`  |
| **Runtime SCADA→DNC**        | GetData polling (periodic)                  | Reverse Poke (immediate push)    |
| **Runtime DNC→SCADA**        | Only if setpoint/command type               | Poke (DNC calculation results)   |
| **Interrogation response**   | Never                                       | Yes (from `m_Interrog_Elements`) |

---

## Part 7: Import / Edit Workflows

### Regular Items: Import from Tab File (`DoImport`)

EVlivy3 has a GUI import tool (`cl_104_DB::DoImport()`) that reads a **22-column tab-separated** text file and:

1. Clears all existing `item_104`, `item_dn`, `iec104_to_dncalc` table records
2. Creates new `cl_104_item` and `cl_dncalc_item` records
3. Links them in `iec104_to_dncalc`
4. Rebuilds in-memory maps

This is the **primary way** to bulk-update the element configuration (exported from SCADA documentation, GIS, etc.).

### Main104 Items: Manual DB Edit

The `main_104` table rows are managed via `cl_104_Ctrl_Dlg.cpp` (a GUI dialog) which:

- Shows current main104 items from `m_104_DB.m_Main_104_item.m_mapItems`
- Allows editing values and persisting them with `m_Main_104_item.Update(pMainItem)`

### dncors_iec104: Manual XChng.cfg Edit

XChng.cfg is a plain text file. It must be kept **in sync** with the `main_104` SQLite table in EVlivy3 by hand — there is no automatic synchronization between them. Both sides must have the same addresses and IDs.

---

## Part 8: Failure Modes and Known Gaps

> [!WARNING]
> **Partial registration failure:** If `bOK=false` at the end of RegisterElements (e.g., `m_p104Client == nullptr`), EVlivy3 re-receives a negative answer. EVlivy3 calls `m_Elements.clear()` on the server side, making all monitoring non-functional.

> [!NOTE]
> **XChng.cfg / DB sync required:** Main104 elements exist in two independent configurations. If XChng.cfg has an address that's in the DB but EVlivy3 doesn't send it via RegisterElements (e.g., due to an active/passive filter, or a scheme load error), that element will exist in `m_Interrog_Elements` but NOT in `m_CmdElements`. Poke and reverse Poke silently fail for it.

> [!NOTE]
> **Batching:** RegisterElements is sent in batches of 30 (`ELEM_REG_RECORDS_MAX`). The state machine iterates using `m_itElements` and `m_itMainElements` persistent iterators. On reply OK, `Reg_DoneOK()` checks if more items remain and sends `NEXT_Continue_Elem_Reg` event to continue.

---

## Summary Diagram

```
┌────────────────────────────────────────────────────────┐
│                        EVlivy3                         │
│                                                        │
│  <scheme>.db3 (SQLite)                                 │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐ │
│  │  item_104    │  │  main_104    │  │  item_dn     │ │
│  │  (regular    │  │  (system     │  │  (scheme     │ │
│  │   IEC104)    │  │   params)    │  │   elements)  │ │
│  └──────┬───────┘  └──────┬───────┘  └──────────────┘ │
│         │                 │                            │
│   cl_104_DB::Init()       │                            │
│         │                 │                            │
│   cl_104_Connector::Elem_Reg()                         │
│     ├── Regular items → cl_Elem_Stub(ID, addr, bCmd, false)
│     └── Main items   → cl_Elem_Stub(ID|FLAG, addr, !input, true)
│                            + ACK addr + 104 type       │
└────────────────────┬───────────────────────────────────┘
                     │
              RegisterElements (TLV over TCP)
                     │
┌────────────────────▼───────────────────────────────────┐
│                   dncors_iec104                         │
│                                                        │
│  Startup: XChng.cfg → m_Interrog_Elements              │
│         (pre-populates main104 type & ctrl_ID)         │
│                                                        │
│  RegisterElements → cl_Reg_Elems_Cmd::Exec()           │
│   Regular → m_Elements[ID] = element                   │
│   Main/SP  → m_CmdElements[ID|FLAG] = element          │
│            + m_bPropagate, m_nACK_Address merged in    │
│            + AddInterrog() (deduplicates)              │
│                                                        │
│  Runtime:                                              │
│   SCADA→IEC104→element.NewData()                       │
│     regular → waits for GetData poll                   │
│     main104 → immediate reverse Poke to EVlivy3        │
│   Poke(DNC)→m_CmdElements→IEC104→SCADA                │
│   Interrogation→m_Interrog_Elements→IEC104→SCADA       │
└────────────────────────────────────────────────────────┘
```
