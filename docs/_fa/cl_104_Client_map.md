# cl_104_Client.cpp - Token Saver Map

## Summary
IEC 60870-5-104 protocol client implementation that manages TCP connections to SCADA servers, handles ASDU (Application Service Data Unit) receiving/sending, and manages data elements (measurements, commands, status points) with support for replay functionality.

---

## Key Logic

### Connection Management (`cl_104_Client` class, L24-86)
- **Constructor** (`cl_104_Client::cl_104_Client` L24-28): Initializes with parent app pointer, connection name, sets initial states.
- **Connect()** (L37-66): Creates `CS104_Connection`, sets handlers (`ConnectionHandler`, `ASDU_ReceivedHandler`), attempts connection to server.
- **CloseConnection()** (L68-78): Sets disconnect flag and signals semaphore to trigger `cl_ConnectServer`.
- **SetNextServer()** (L80-85): Round-robin server switching for redundancy.

### Connection Handler (L402-459)
- `ConnectionHandler()`: Static callback for connection events:
  - `CS104_CONNECTION_OPENED`: Sends STARTDT, sends general interrogation.
  - `CS104_CONNECTION_CLOSED`: Calls parent's `ConnectionClosed()`, initiates close.

### ASDU Receiving - **Main Data Flow** (L461-582)
- `ASDU_ReceivedHandler()`: **Critical function** - parses incoming ASDU:
  1. Checks for interrogation command (`C_IC_NA_1` → `Rx_Interrogation()`).
  2. Loops through all information objects in ASDU.
  3. Finds/creates `cl_104_Element` via `FindElement()`.
  4. Creates data object via `cl_104_Data::Create_from_ASDU()`.
  5. Calls `pElem->NewData()` to store value → **DATA IS DELETED INSIDE NewData()**.
  6. If `m_bPropagate` flag set → forwards to connected `cl_Client` instances via `cl_Poke_Command`.
  7. Sends ACK back if `m_nACK_Address` is configured.

### Interrogation Handling (L584-676)
- `Rx_Interrogation()`: Responds to general interrogation requests by iterating `m_Interrog_Elements` and sending current values.

---

## Data Flow

### Receiving Data
```
TCP/IEC104 → CS104_Connection → ASDU_ReceivedHandler() → FindElement() → cl_104_Data::Create_from_ASDU() → pElem->NewData() → value stored + logged
```

### Sending Data (Commands)
| Function | IEC104 Type | Line |
|----------|-------------|------|
| `Send_MeasuredValueShort()` | M_ME_NC_1 | L775-789 |
| `Send_SetPoint_ShrtFP()` | C_SE_NC_1 | L791-803 |
| `SinglePointInformation()` | M_SP_NA_1 | L805-817 |
| `Send_SingleCommand()` | C_SC_NA_1 | L819-831 |
| `DoublePointInformation()` | M_DP_NA_1 | L833-845 |
| `Send_DoubleCommand()` | C_DC_NA_1 | L847-859 |

All use `CS104_Connection_sendProcessCommandEx()` with address masking: `nAddr & 0x00FFFFFF` for IOA, `(nAddr >> 24) & 0x0000FFFF` for CA.

### Related Classes/Files
- **`cl_Client`** (cl_Client.h/cpp): DNCors internal client, receives propagated commands via `m_Client_Rx.Send()`.
- **`cl_MainApp`** (main.h/cpp): Parent application, owns `m_104_Clients` list, manages `m_Config`.
- **`cl_ConnectServer`** (this file L1119-1188): Runs connection management loop in separate thread, handles reconnection logic.
- **`cs104_connection.h`**: lib60870 library - provides `CS104_Connection_*` functions.

---

## Data Formats

### Address Format (5-byte, 40-bit)
```
uint64_t nAddress = [CA_high:8][CA_low:8][IOA_high:8][IOA_mid:8][IOA_low:8]
```
- **CA (Common Address)**: bytes 3-4 (bits 24-39)
- **IOA (Information Object Address)**: bytes 0-2 (bits 0-23)

Key functions:
| Function | Purpose | Line |
|----------|---------|------|
| `cl_104_Element::GetCA()` | `(nAddress >> 24) & 0x0000FFFF` | L1216-1218 |
| `cl_104_Element::GetIOA()` | `nAddress & 0x00FFFFFF` | L1211-1214 |
| `AddrToStr()` | Converts to `"XXX.XXX.XXX.XXX.XXX"` | L1296-1305 |
| `StrToAddr()` | Parses from dot-notation string | L1275-1294 |

### Data Classes (cl_104_Data hierarchy)
| Class | IEC104 Type | Value Type | Line |
|-------|-------------|------------|------|
| `cl_104_Meas_Int` | M_ME_NB_1 (11) | `int m_nValue` | L969-987 |
| `cl_104_Single_Info` | M_SP_NA_1 (1), M_SP_TB_1 (30), C_SC_NA_1 (45) | `bool m_bValue` | L989-1010 |
| `cl_104_Double_Info` | M_DP_NA_1 (3), M_DP_TB_1 (31), C_DC_NA_1 (46) | `int m_nValue` (0-3) | L1012-1039 |
| `cl_104_Meas_Float` | M_ME_NC_1 (13), M_ME_TF_1 (36), C_SE_NC_1 (50) | `float m_fValue` | L1041-1068 |
| `cl_104_SetPoint_Float` | C_SE_NC_1 | `float m_fValue` | L1070-1096 |
| `cl_104_Step_Pos_Info` | M_ST_TB_1 (32) | `int m_nValue` | L1098-1117 |

Factory: `cl_104_Data::Create_from_ASDU()` (L919-949)

---

## Exports/Public API

### cl_104_Client (L342-405 in .h)
```cpp
cl_104_Client(cl_MainApp *pParent, wxString szName);
~cl_104_Client();

bool Connect();
void CloseConnection();
void SetNextServer();

void ClientConnected(cl_Client *pClient);
void ClientClosed(cl_Client *pClient);

bool GetIP(wxString szFile);               // Parse IP config file
bool GetXChngCfg(wxString szFile);         // Parse exchange config

cl_104_Element *FindElement(uint64_t nAddress, bool bAutoInsert = true, bool bParameter = false);

static void ConnectionHandler(void* parameter, CS104_Connection connection, CS104_ConnectionEvent event);
static bool ASDU_ReceivedHandler(void* parameter, int address, CS101_ASDU asdu);

bool OpenReplay(wxString szPath);
bool Rx_Interrogation(CS101_ASDU asdu);
void AddInterrog(cl_104_Element *pElem);

void Send_MeasuredValueShort(cl_104_Element *pElem, double fValue, uint8_t nCOT, bool bAckAddr);
void Send_SetPoint_ShrtFP(cl_104_Element *pElem, double fValue, uint8_t nCOT, bool bAckAddr);
void SinglePointInformation(cl_104_Element *pElem, bool bValue, uint8_t nCOT, bool bAckAddr);
void Send_SingleCommand(cl_104_Element *pElem, bool bValue, uint8_t nCOT, bool bAckAddr);
void DoublePointInformation(cl_104_Element *pElem, int nValue, uint8_t nCOT, bool bAckAddr);
void Send_DoubleCommand(cl_104_Element *pElem, int nValue, uint8_t nCOT, bool bAckAddr);
```

### cl_104_Element (L267-331 in .h)
```cpp
cl_104_Element(cl_104_Client *pClient, uint64_t nAddress, bool bIsParameter);
void NewData(cl_104_Data *pData);              // Store incoming data, deletes pData
bool HaveNewData(wxDateTime dtNewerThan);
cl_Elem_104_Value *GetData();
uint32_t GetIOA();
uint32_t GetCA();
static bool StrToAddr(wxString szStr, uint64_t &nValue, bool bHex);
static wxString AddrToStr(uint64_t nAddress);
```

---

## Dependencies

### Includes
```cpp
#include "wx_pch.h"
#include <wx/tokenzr.h>
#include <wx/dir.h>
#include "cl_104_Client.h"
#include "cl_Client.h"
#include "Log.h"
#include "main.h"
#include "cs104_connection.h"   // lib60870 library
#include "Commands.h"
```

### External lib60870 Calls
- `CS104_Connection_create()`, `CS104_Connection_destroy()`, `CS104_Connection_connect()`, `CS104_Connection_close()`
- `CS104_Connection_setConnectionHandler()`, `CS104_Connection_setASDUReceivedHandler()`
- `CS104_Connection_sendStartDT()`, `CS104_Connection_sendInterrogationCommand()`
- `CS104_Connection_sendASDU()`, `CS104_Connection_sendProcessCommandEx()`
- `CS101_ASDU_*` functions for ASDU manipulation
- `InformationObject_*`, `SinglePointInformation_*`, `DoublePointInformation_*`, `MeasuredValueShort_*`, etc.

---

## State

### cl_104_Client Key Members
| Member | Type | Purpose |
|--------|------|---------|
| `m_104_Connection` | `CS104_Connection` | Active IEC104 connection handle |
| `m_bConnected` | `bool` | Connection state flag |
| `m_bDisConnect` | `bool` | Disconnect request flag |
| `m_nAct_Srv_Index` | `int` | Current server index for redundancy |
| `m_Server_Addr` | `vector<wxIPV4address>` | List of server addresses |
| `m_Elements` | `map<uint64_t, cl_104_Element_UPtr>` | Data elements by address |
| `m_Interrog_Elements` | `list<cl_104_Element*>` | Elements to include in interrogation response |
| `m_Clients` | `list<cl_Client*>` | Connected DNCors clients |
| `m_pStatus_Element` | `cl_104_Element*` | Special element for `ID_104_Active_ACK` |

### cl_104_Element Key Members
| Member | Type | Purpose |
|--------|------|---------|
| `m_nAddress` | `uint64_t` | 5-byte IEC104 address |
| `m_nACK_Address` | `uint64_t` | ACK destination address (if propagating) |
| `m_bPropagate` | `bool` | Forward data to connected clients |
| `m_fValue` | `double` | Current value |
| `m_nQuality` | `uint32_t` | Quality descriptor |
| `m_dtLastData` | `wxDateTime` | Timestamp of last update |
| `m_nCtrl_ID` | `uint64_t` | DNCors control ID |
