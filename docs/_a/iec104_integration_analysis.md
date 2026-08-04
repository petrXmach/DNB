# IEC 104 Integration Analysis

## Overview

This document analyzes how the **Local Communication** channel (serving local applications) interacts with the **IEC 104 Communication** channel (serving external RTUs).

## Core Interaction Model

The application acts as a **Gateway** or **Proxy**.

- **Downstream**: It listens for local clients (`cl_Client`).
- **Upstream**: It connects to remote IEC 104 Servers (`cl_104_Client`).

### Data Flow Direction

The flow is primarily **Event-Driven**, not "Request-Response".

#### 1. Incoming Data (IEC 104 -> Local App)

- **Source**: IEC 104 Server (RTU) sends spontaneous data (e.g., a measurement change).
- **Listener**: `cl_104_Client::ASDU_ReceivedHandler` (Callback from lib60870).
- **Processing**:
  1.  **Iterate**: The handler iterates through **every single data point** in the message (Lines 483+). Even if the incoming packet contains multiple values, they are processed one by one.
  2.  **Update**: Finds the corresponding `cl_104_Element` and updates cached value.
  3.  **Propagation Loop** (Lines 525-542):
      - If `pElem->m_bPropagate` is true:
      - It iterates through all local clients.
      - **Creation**: It instantiates a new `cl_Poke_Command` object for **just this single value**.
      - **Sending**: It calls `pClient->m_Client_Rx.Send(uCmd.get())`.
  4.  **TLV Conversion**: The `Send()` method immediately serializes the C++ `cl_Poke_Command` into binary TLV data and writes it to the socket.

**Key Finding**: The service does **NOT** wait for the local app to "Ask" for data repeatedly. It pushes updates (Spontaneous/Unsolicited) as they arrive. Crucially, it **de-batches** the data: one incoming multi-item IEC 104 message results in multiple separate TLV packets sent to the local app.

#### 2. Outgoing Controls (Local App -> IEC 104)

- **Source**: Local App sends a Command (e.g., "Turn Switch On").
- **Listener**: `cl_Client` receives a `cl_Poke_Command`.
- **Processing**: The `cl_Client` looks up the `cl_104_Element`.
- **Action**: Calls `cl_104_Client::Send_SingleCommand` (or similar).
- **Transport**: Uses `CS104_Connection_sendProcessCommandEx` to send the packet to the RTU.

## Hypothesis Verification

**User Hypothesis**: "Service should continuosely (repeatedly) send request to IEC104 part to get all data, after all data are received it should send them to local port..."

**Verdict**: **Incorrect**.

- The application uses **Unsolicited Reporting / Spontaneous Events**.
- It does send an initial **Interrogation Command** upon connection.
- After that, it relies on the IEC 104 Server to push changes.
- It does **not** poll repeatedly.
- It does **not** batch data for the local app; it forwards events item-by-item.

## Detailed Workflow

1.  **Startup**:
    - `cl_104_Client::Connect` -> Connects to RTU.
    - Sends `InterrogationCommand` to get current/valid values for all points.
2.  **Steady State**:
    - **RTU** detects a change -> Sends IEC104 Message.
    - **Service** (`cl_104_Client`) receives it.
    - **Service** updates internal cache (`cl_104_Element`).
    - **Service** _immediately_ serializes and pushes this single value to all connected **Local Apps** (`cl_Client`).
3.  **Local Request**:
    - Local App connects.
    - Sends `cl_Reg_Elems_Cmd` to subscribe to specific points.
    - Sends `cl_Get_Data_Cmd` effectively asking "Give me what you have right now".
    - Service replies with current cached values.

## File References

- **`cl_104_Client.cpp`**:
  - `ASDU_ReceivedHandler` (Line 461): Main entry point for incoming IEC 104 data.
  - Lines 525-542: The "Propagation" loop that forwards data to `cl_Client`.
- **`cl_Client.cpp`**:
  - `Receive` (Line 67): Handles commands from local apps.
