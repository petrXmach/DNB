# Local Communication Analysis

## Overview

The "Local Communication" channel handles interaction between the `DNCors_IEC104` service and local clients (e.g., UI, data collectors).

**Mechanism**:

- **Transport**: TCP/IP
- **Threading Model**: Thread-per-client (Worker Thread)
  - Although `main.cpp` has handlers for `wxEVT_SOCKET`, the code that would bind these events to individual client sockets is **commented out** (Lines 271-274 of `main.cpp`).
  - Therefore, the actual data reception is handled by a dedicated background thread: `cl_Client_Rx::Run`.

## Code Structure

### 1. `cl_Client` (The Session Manager)

- **File**: `cl_Client.cpp`, `cl_Client.h`
- **Role**: Represents a single connected local client.
- **Key Members**:
  - `m_pSocket`: The underlying `wxSocketBase`.
  - `m_Client_Rx`: Helper object encapsulating the receiver thread logic.
  - `m_pClient_Rx_Thread`: `std::thread` running the receive loop.
- **Lifecycle**: Created in `main.cpp` -> `OnServerEvent` when a new connection is accepted.

### 2. `cl_Client_Rx` (The Receiver Thread)

- **File**: `cl_Client.cpp` (Lines 182-383)
- **Role**: Active loop that blocks on `select` and `recv` to read incoming data.
- **Logic**:
  1.  Wait for data (`select`).
  2.  Read Header (`SockRcv` reading `sizeof(tlv_head_t)`).
  3.  Read Body (`SockRcv` reading specified `nLength`).
  4.  **Deserialize**: Converts raw bytes into a `cl_Command` object using `cl_Serializer`.
  5.  **Execute**: Calls `pCommand->Exec(m_pParent)`.
  6.  **Reply**: If the command generates an answer (`m_uAnswer`), it is serialized and sent back immediately via `Send()`.

## Protocol (The "What")

The data exchange uses a custom **TLV (Type-Length-Value)** binary protocol.

### Packet Structure

- **Header**: `tlv_head_t` (8 bytes)
  - `nTag` (uint32): Object Type ID.
  - `nLength` (uint32): Length of the following data body.
- **Body**: Binary payload corresponding to the Tag.

### Command Classes (`Commands.h`)

Data is "object-oriented" on the wire. The `nTag` determines which C++ class is instantiated.

| Command Class       | Tag ID                    | Purpose                                                              |
| :------------------ | :------------------------ | :------------------------------------------------------------------- |
| `cl_Init_Cmd`       | `TAG_CLASS_CMD_INIT`      | Handshake. Sends "Server Name" (e.g. which IEC 104 node to talk to). |
| `cl_Reg_Elems_Cmd`  | `TAG_CLASS_CMD_REG_ELEMS` | Register a list of IEC 104 addresses to monitor (subscription).      |
| `cl_Get_Data_Cmd`   | `TAG_CLASS_CMD_GET_DATA`  | Request for current values (polling).                                |
| `cl_Poke_Command`   | `TAG_CLASS_POKE_CMD`      | Send a control value (e.g., switch ON/OFF) to the IEC 104 device.    |
| `cl_Replay_Command` | `TAG_CLASS_REPLAY_CMD`    | Controls for Replay mode.                                            |

### Data Payload (`Serializable.h`)

The "Value" part of TLV supports standard types:

- Integers (8, 16, 32, 64 bit)
- Double
- `wxDateTime`
- Strings (UTF-8, UTF-16)
- Recursive Objects (Serialized objects inside other objects)

## Processing Flow

1.  **Client connects**: `main.cpp` creates `cl_Client`.
2.  **Thread Start**: `cl_Client` starts `cl_Client_Rx::Run`.
3.  **Data Arrives**:
    - `cl_Client_Rx::Run` reads bytes.
    - `cl_Serializer` instantiates the correct `cl_Command` subclass based on the Tag.
    - `cl_Serializer` populates the object members (Deserialize).
4.  **Action**: `pCommand->Exec(cl_Client*)` is called.
    - _Example_: `cl_Init_Cmd::Exec` finds the requested `cl_104_Client` and links the local session to it.
5.  **Response**: If `Exec` sets `m_uAnswer` (e.g., `cl_Init_Answer`), the thread serializes it and sends it back to the local client.
