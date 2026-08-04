# Analysis of TLV Data Receiving and Deserialization

## Overview
The selected code block in `cl_Client_Rx::Run()` (`cl_Client.cpp`) handles the processing of data received from the TCP/IP socket. It involves three main stages:
1.  **Buffering & Preparation**: `TLV_Rx.ReadTLV`
2.  **Deserialization**: `TLV_Rx.Deserialize`
3.  **Execution**: `pRxCmd->Exec`

## 1. TLV_Rx.ReadTLV
**Functionality**: Data movement and buffer management.

*   **Source**: It reads raw bytes from `m_pParent->m_pRxBuffer` (which was filled by `SocketRcv` just before).
*   **Destination**: It copies the data into the internal buffer of the `cl_Serializer` object (`TLV_Rx.m_pData`).
*   **Logic**:
    1.  Reads the `tlv_head_t` header (Tag + Length).
    2.  Checks if the data is a compressed archive (`TAG == COMPRESSED_ARCHIVE_MAGIC`).
    3.  **If Compressed**: It allocates a temporary buffer, reads/copies the compressed data, and uses `BZ2_bzDecompress` to decompress it into the `cl_Serializer`'s internal buffer.
    4.  **If Uncompressed**: It simply `memcpy`s the data payload from `RxBuffer` to the `cl_Serializer`'s internal buffer.
*   **Purpose**: To ensure the `cl_Serializer` has a complete, valid, and decompressed data stream ready for structured parsing.

## 2. TLV_Rx.Deserialize()
**Functionality**: Reconstructs C++ objects from the binary TLV stream.

*   **Mechanism**:
    It reads the first Tag from the stream and calls a factory function (internally `CreateObjectByTag`) to instantiate the correct class. It then calls `Deserialize` recursively to populate the object's fields.

*   **Possible Data Objects (Commands)**:
    Based on `IEC104_CreateObjectByTag` in `Commands.cpp`, the following top-level objects (derived from `cl_Command`) can be deserialized:

    | Tag ID | Class Name | Description |
    | :--- | :--- | :--- |
    | `TAG_CLASS_CMD_INIT` | `cl_Init_Cmd` | Initialization command. |
    | `TAG_CLASS_CMD_REG_ELEMS` | `cl_Reg_Elems_Cmd` | Register elements for monitoring. |
    | `TAG_CLASS_CMD_GET_DATA` | `cl_Get_Data_Cmd` | Request for current data. |
    | `TAG_CLASS_POKE_CMD` | `cl_Poke_Command` | Send control value (SetPoint/Command). |
    | `TAG_CLASS_REPLAY_CMD` | `cl_Replay_Command` | Control replay mode (Init, Move). |
    | `TAG_CLASS_COMMAND` | `cl_Command` | Generic base command (unused as top-level usually). |

    *Note: Answer classes (`cl_Init_Answer`, `cl_Data_Answer`, etc.) are also registered but conceptually flow in the opposite direction (Server -> Client).*

*   **Data Content & Types**:
    *   **All Commands**: Contain `m_nSessionID` (Client ID).
    *   **cl_Init_Cmd**: `m_sz104_Server` (String, e.g., IP/Hostname).
    *   **cl_Reg_Elems_Cmd**: List of `cl_Elem_Stub` objects. Each stub has:
        *   `m_nID` (Internal ID)
        *   `m_n104Addr` (IEC104 Address, 64-bit)
        *   `m_n104_ACK_Adress` (ACK Address)
        *   `m_bSetPoint` (Boolean)
        *   `m_bPropagate` (Boolean)
    *   **cl_Get_Data_Cmd**: `m_NewerThan` (DateTime), `m_bStart` (Boolean).
    *   **cl_Poke_Command**: `m_nElementID` (Target Element ID) and a list of `cl_Poke_Value` objects:
        *   `cl_Float_Poke_Value` (Float value, COT)
        *   `cl_Bool_Poke_Value` (Boolean value, COT)
        *   `cl_4State_Poke_Value` (4-state Value, COT)
    *   **cl_Replay_Command**: `m_nCommand` (Init/Move code), `m_dtTime` (Timestamp).

## 3. pRxCmd->Exec(m_pParent)
**Functionality**: Polimorphic execution of the received command logic.

*   **Context**: `m_pParent` is `cl_Client*`. This acts as the "Destination" interface for the execution logic.
*   **Execution Logic (`Commands_Srv.cpp`)**:

    | Command Class | Action Performed | Parameters Processed |
    | :--- | :--- | :--- |
    | **cl_Init_Cmd** | Initializes the IEC104 connection. | **Server Address** (`m_sz104_Server`): Finds the corresponding `cl_104_Client` instance. |
    | **cl_Reg_Elems_Cmd** | Maps internal app elements to IEC104 IOA/CA addresses. | **Elements List**: Iterates through `m_Elems`, finds corresponding `cl_104_Element` in the 104 client, and links them (`m_Element_IDs` map). Configures `SetPoint` and `Propagate` flags. |
    | **cl_Get_Data_Cmd** | Fetches current values for subscribed elements. | **Timestamp** (`m_NewerThan`): Iterates through monitored elements and returns values (`cl_Elem_104_Value`) only if data is newer than this timestamp. |
    | **cl_Poke_Command** | Sends a control command/setpoint to the SCADA system. | **Element ID** (`m_nElementID`): Finds the mapped `cl_104_Element`. <br>**Value**: Sends the value (Float, Bool, or 4-State) via `Send_MeasuredValueShort`, `SinglePointInformation`, or `DoublePointInformation`. |
    | **cl_Replay_Command** | Controls historical replay (if supported). | **Command** (`Initialize`/`Move`): Sets up replay time range or moves current replay time marker. |

## Parameter Summary (Local App -> Here)
The following parameters can be sent from the local application:

1.  **Connection Info**: Server Name/IP (`string`).
2.  **Configuration**: List of Elements to map (ID, 104 Address, Attribute flags).
3.  **Data Request**: Timestamp (`wxDateTime`) for differential updates.
4.  **Control Values (Poke)**:
    *   Target Element ID.
    *   **Float**: Value (`double`), Cause of Transmission (`u8`).
    *   **Boolean**: Value (`bool`), COT (`u8`).
    *   **4-State**: Value (`int` 0-3), COT (`u8`).
5.  **Replay Control**: Command Code (`u32`), Target Time (`wxDateTime`).
