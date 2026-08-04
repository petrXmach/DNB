# FA: cl_Client Map

## Summary
`cl_Client` manages a TCP network client connection for DNCors, handling asynchronous command reception and transmission via a dedicated native socket thread (`cl_Client_Rx`) while integrating with the main application and IEC 104 protocol handlers (`cl_104_Client`).

## Key Logic
*   **Threaded Socket Handling**: Offloads socket reception to `cl_Client_Rx` thread to avoid blocking the main UI/application thread.
    *   **RX Thread**: Uses raw Windows Sockets (`select`, `recv`) for robust blocking/non-blocking I/O monitoring.
    *   **Initialization**: Extracts the raw socket handle (`m_Sockfd`) from the provided `wxSocketBase` and starts the `Run` loop.
*   **Command Dispatching**:
    *   Receives raw bytes, deserializes them into `cl_Command` objects using `cl_Serializer` (TLV format).
    *   Executes commands via `pCommand->Exec(this)`.
    *   Handles exceptions during deserialization or execution gracefully.
*   **Response Handling**: Checks if an executed command generated an answer (`m_uAnswer`). If so, serializes and sends it back immediately.
*   **Protocol Linkage**: `Setup_104` links the generic client connection to a specific IEC 104 server logic (`cl_104_Client`) based on an initialization handshake.
*   **ID Mapping**: Maintains bidirectional maps (`m_Elements`, `m_Element_IDs`, etc.) to translate between runtime `cl_104_Element` pointers and persistent IDs used in communication.

## Data Flow
1.  **Receive**:
    *   `cl_Client_Rx::Run` (Background Thread) waits on `select`.
    *   `cl_Client_Rx::SockRcv` reads header (`tlv_head_t`) then body into `m_pParent->m_pRxBuffer`.
    *   `cl_Serializer` deserializes buffer -> `cl_Command*`.
2.  **Process**:
    *   `cl_Command::Exec(cl_Client*)` is called.
    *   This logic likely acts on `cl_104_Client` or `cl_MainApp` (e.g., `m_p104Client->ClientConnected(this)`).
3.  **Send**:
    *   If `pCommand->m_uAnswer` is present after execution:
    *   `cl_Serializer` serializes response -> `pTx_Buff`.
    *   `cl_Client_Rx::Send` sends buffer via native `send()`.

## Data Formats
*   **TLV Protocol**:
    *   **Header**: `cl_Serializer::tlv_head_t` (contains `nLength`).
    *   **Body**: Binary payload determined by `nLength`.
    *   **Serialization**: Custom `cl_Serializer` class handles object<->binary conversion.
*   **Commands**:
    *   Type: `cl_Command` (abstract base).
    *   Subtypes: `cl_Init_Cmd` (for setup), and others implied by strict casting/deserialization.

## Exports/Public API
*   **`cl_Client(cl_MainApp *pParent, wxSocketBase *pSocket)`**: Constructor taking parent app and socket connection.
*   **`Setup_104(cl_Init_Cmd *pCmd)`**: Configures the client specifically for an IEC 104 server context.
*   **`Find_Element_ID(cl_104_Element *pElement)`**: Helper to retrieve ID for a data element.
*   **`Find_CmdElement_ID(cl_104_Element *pElement)`**: Helper to retrieve ID for a command element.
*   **`cl_Client_Rx::Send(cl_Command *pCmd)`**: Sends a command object over the socket.

## Dependencies
*   **Internal**:
    *   `cl_104_Client`, `cl_104_Element`: Associated logic and data structures.
    *   `cl_Serializer`, `cl_Command`: Data transport layer.
    *   `cl_MainApp`: Root application object.
    *   `Log.h`: Logging facilities.
*   **External**:
    *   `wxWidgets`: `wxSocketBase`, `wxThreadEvent`, `wxLogDebug`.
    *   `WinSock2` (implied): `recv`, `send`, `select`, `WSAStartup`, `WSAGetLastError`.

## State
*   **Thread State**: `m_bRunning` (active), `m_bStop` (signal to terminate).
*   **Connection State**: `m_Sockfd` (native socket), `m_sz104Server` (target server name).
*   **Component Links**: `m_p104Client` (logic handler), `m_Elements` (mapped data points).
