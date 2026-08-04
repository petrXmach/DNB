# Analysis: `cl_104_Data` and `cl_104_Element`

**Context**: IEC 104 Client Implementation (`cl_104_Client.h`, `cl_104_Client.cpp`)

## 1. `cl_104_Data`
**Purpose**:
This is a **transient data wrapper** representing a single parsed value from an incoming IEC 104 ASDU (Application Service Data Unit). It abstracts the differences between various IEC 104 data types (Measured Values, Single Points, Double Points, etc.) into a common interface. It exists only briefly during the processing of a received packet.

**Structure**:
*   **Type**: Abstract Base Class.
*   **Key Members**:
    *   **Addressing**: `m_nCA` (Common Address), `m_nIOA` (Information Object Address).
    *   **Data**: `m_nType` (ASDU Type ID), `m_nCOT` (Cause of Transmission), `m_nQualDesc` (Quality), `m_dtTime` (Timestamp).
*   **Key Methods**:
    *   `Create_from_ASDU(...)`: Static factory method that instantiates the correct subclass (e.g., `cl_104_Meas_Float`, `cl_104_Single_Info`) based on the ASDU type.
    *   `SetValue(...)`: Parses the raw lib60870 `InformationObject` into member variables.
    *   `GetFltValue()`, `GetStrValue()`: Virtual methods to access the value uniformly.
*   **Subclasses**:
    *   `cl_104_Meas_Int`
    *   `cl_104_Single_Info`
    *   `cl_104_Double_Info`
    *   `cl_104_Meas_Float`
    *   `cl_104_Step_Pos_Info`

**Where it is used**:
*   **Instantiation**: Created inside `cl_104_Client::ASDU_ReceivedHandler` immediately after a packet is received.
*   **Processing**: passed to `cl_104_Element::NewData(cl_104_Data *pData)`.
*   **Lifecycle**: It is **deleted** inside `cl_104_Element::NewData` immediately after its data is extracted and logged. It does not persist.

---

## 2. `cl_104_Element`
**Purpose**:
This represents a **persistent configured data point** (a "Tag") in the client. It maintains the **state** (current value, quality, timestamp) of a specific IEC 104 address (CA + IOA) over time. Its functionality includes logging data to disk, handling historical replay, and mapping IEC 104 points to the external DNCors system.

**Structure**:
*   **Type**: Concrete Class.
*   **Key Members**:
    *   **Identity**: `m_nAddress` (Combined 64-bit CA+IOA), `m_nACK_Address` (Address to send acks/commands to).
    *   **State**: `m_fValue` (Current Value), `m_nQuality`, `m_dtLastData` (Time of last update).
    *   **Features**: `m_FileTxt` (Log file handle), `m_ReplayFile` (Replay source), `m_bPropagate` (Flag to forward data to other clients).
*   **Key Methods**:
    *   `NewData(...)`: Updates the element's state from a `cl_104_Data` object and writes to the log.
    *   `GetData()`: Returns a snapshot of the current value (as `cl_Elem_104_Value`).
    *   `InitReplay()` / `ReplayMove()`: Manages reading historical data from files for replay mode.
    *   `CreateLog(...)`: Manages the text file logging for this specific data point.

**Where it is used**:
*   **Storage**: Stored in `cl_104_Client::m_Elements` (a map of Address -> Element Unique Pointer).
*   **Lookup**: Found by address in `ASDU_ReceivedHandler` to update state when new data arrives.
*   **DNCors Integration**: 
    *   Referenced in `cl_Client::m_Elements` and `m_CmdElements` to map external IDs to IEC 104 points.
    *   Used in `Commands_Srv.cpp` for:
        *   `cl_Reg_Elems_Cmd`: Registering points to be monitored.
        *   `cl_Get_Data_Cmd`: Retrieving the latest values.
        *   `cl_Poke_Command`: Sending commands/setpoints (uses `Send_MeasuredValueShort`, `SinglePointInformation`, etc. via the element).

---

## Summary of Data Flow Interaction
1.  **Packet Arrives**: `cl_104_Client` receives an ASDU via `ASDU_ReceivedHandler`.
2.  **Wrap Data**: `cl_104_Data::Create_from_ASDU` factory creates a temporary `cl_104_Data` object (e.g. `cl_104_Meas_Float`).
3.  **Find Tag**: The corresponding `cl_104_Element` is looked up by address (`m_nCA` | `m_nIOA`).
4.  **Update**: `cl_104_Element::NewData(pData)` is called. 
    *   The element updates its persistent state (`m_fValue`, `m_dtLastData`).
    *   It logs the change to a text file via `LogValue`.
    *   **Crucial Step**: It deletes the `pData` object.
5.  **Propagate**: If `m_bPropagate` is true, the element forwards the new value to connected `cl_Client` instances via `cl_Poke_Command` (effectively bridging IEC 104 to the internal DNCors protocol).
