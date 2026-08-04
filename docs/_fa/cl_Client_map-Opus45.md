# cl_Client.cpp / cl_Client.h - File Map

## Summary
DNCors TCP client handler for DNCoRS (IEC 104) server application. Manages client socket connections, receives/deserializes commands via TLV protocol, executes them, and sends responses.

---

## Key Logic

### Two Main Classes

| Class | Purpose |
|-------|---------|
| `cl_Client` | Main client connection manager. Owns socket, Rx buffer, and links to IEC 104 elements |
| `cl_Client_Rx` | Dedicated Rx thread for async socket reception using raw Winsock `recv()` |

### Connection Lifecycle
1. **Constructor** ([cl_Client::cl_Client](file:///c:/_EGC/dncors_iec104/cl_Client.cpp#L23-L33)): Creates Rx buffer, starts `cl_Client_Rx` thread
2. **104 Setup** ([cl_Client::Setup_104](file:///c:/_EGC/dncors_iec104/cl_Client.cpp#L145-L157)): Links to `cl_104_Client` via `m_pParent->Find104Client()`
3. **Destructor** ([cl_Client::~cl_Client](file:///c:/_EGC/dncors_iec104/cl_Client.cpp#L35-L65)): Stops Rx thread, joins, destroys socket, frees buffer

### Command Execution Pattern
Both `Receive()` and `cl_Client_Rx::Run()` follow the same pattern:
```
Read TLV header → Read TLV body → Deserialize to cl_Command → Exec() → Send answer if available
```

---

## Data Flow

### Receiving Data

| Method | Context | Mechanism |
|--------|---------|-----------|
| [cl_Client::Receive()](file:///c:/_EGC/dncors_iec104/cl_Client.cpp#L67-L143) | wxWidgets event-driven | `wxSocketBase::Read()` |
| [cl_Client_Rx::Run()](file:///c:/_EGC/dncors_iec104/cl_Client.cpp#L228-L370) | Async thread (main path) | `select()` + `recv()` via [SockRcv()](file:///c:/_EGC/dncors_iec104/cl_Client.cpp#L195-L226) |

### Sending Data

| Method | Context |
|--------|---------|
| `wxSocketBase::Write()` | In `Receive()` (L122-124) |
| [cl_Client_Rx::Send()](file:///c:/_EGC/dncors_iec104/cl_Client.cpp#L372-L383) | In `Run()` thread (L346) |

### Related Classes (referenced)
- `cl_MainApp` - Parent application, manages `cl_104_Client` registry
- `cl_104_Client` - IEC 104 protocol client, notified via `ClientConnected()`
- `cl_104_Element` - IEC 104 data point, linked in element maps
- `cl_Command` - Base command class with `Exec()` and `m_uAnswer`
- `cl_Serializer` - TLV serialization/deserialization

---

## Data Formats

### TLV Protocol Structure
```cpp
cl_Serializer::tlv_head_t  // Header: contains nLength field
[payload of nLength bytes] // Body
```

### Rx Buffer
- Size: `RX_BUFF_LEN = 128 * 1024` bytes (128 KB)
- Allocated in constructor, freed in destructor

### Serialization Pipeline
```
cl_Serializer::ReadTLV() → cl_Serializer::Deserialize() → cl_Command*
cl_Serializer::Serialize() → cl_Serializer::WriteTLVArchive() → uint8_t* buffer → send()
```

---

## Exports/Public API

### cl_Client : cl_Cmd_Dest
```cpp
cl_Client(cl_MainApp *pParent, wxSocketBase *pSocket);
~cl_Client();
void Receive();
bool Setup_104(cl_Init_Cmd *pCmd);
uint64_t Find_Element_ID(cl_104_Element *pElement);
uint64_t Find_CmdElement_ID(cl_104_Element *pElement);
```

### cl_Client_Rx
```cpp
cl_Client_Rx(cl_Client *pParent);
virtual ~cl_Client_Rx();
void Run();
bool SockRcv(uint8_t *pBuffer, int nBytes);
bool Send(cl_Command *pCmd);
```

---

## Dependencies

### Includes
- `wx_pch.h` - Precompiled header
- `cl_Client.h` / `cl_104_Client.h`
- `main.h`, `Log.h`
- `common.h`, `Commands.h` (from [common/](file:///c:/_EGC/dncors_iec104/common/Commands.h))
- `<wx/socket.h>`, `<thread>`, `<map>`, etc.

### External Calls
- `cl_MainApp::Find104Client()` - Resolve IEC 104 server name
- `cl_104_Client::ClientConnected()` - Register client
- `cl_Command::Exec()` - Execute deserialized command
- `cl_Serializer::ReadTLV()`, `Deserialize()`, `Serialize()`, `WriteTLVArchive()`

---

## State

### cl_Client Members
| Member | Type | Purpose |
|--------|------|---------|
| `m_pSocket` | `wxSocketBase*` | TCP socket handle |
| `m_pRxBuffer` | `uint8_t*` | Receive buffer (128KB) |
| `m_sz104Server` | `wxString` | IEC 104 server name |
| `m_p104Client` | `cl_104_Client*` | Linked 104 client |
| `m_Elements` | `map<uint64_t, cl_104_Element*>` | Element by ID lookup |
| `m_Element_IDs` | `map<cl_104_Element*, uint64_t>` | ID by element lookup |
| `m_CmdElements/IDs` | maps | Same for command elements |

### cl_Client_Rx Members
| Member | Type | Purpose |
|--------|------|---------|
| `m_bRunning` | `bool` | Thread is active |
| `m_bStop` | `volatile bool` | Signal to stop thread |
| `m_Sockfd` | `int` | Raw socket descriptor |
| `m_lstCommand` | `deque<cl_Command*>` | Command queue (protected by `m_csCmdLst`) |

---

## Events
- `wxEVT_Client_Closed` - Posted when Rx thread exits ([L366-368](file:///c:/_EGC/dncors_iec104/cl_Client.cpp#L366-L368))
