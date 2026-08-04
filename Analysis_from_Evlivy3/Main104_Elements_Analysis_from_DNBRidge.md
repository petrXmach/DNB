# Main104 Elements — Complete Analysis

> Reference document for understanding bidirectional (Main104Flag) elements.
> Covers C++ original behavior, current C# implementation, and future design decisions.

---

## 1. Three Categories of Elements

### A. Monitor Elements (SCADA → DNC)
- **Purpose:** Measured values from the power network (voltages, currents, switch states)
- **Data flow:** SCADA sends spontaneous updates → DNBridge stores in Element104 → DNC polls via GetData
- **Registration:** DNC sends `RegisterElements` with plain IDs (e.g. 959–988), `IsSetPoint=false`
- **C++ storage:** `cl_Client::m_Elements` map, iterated by `GetData`
- **C# storage:** `DncSession.MonitorElements`

### B. Command/SetPoint Elements (DNC → SCADA)
- **Purpose:** Control commands — DNC calculates network state and sends setpoints to manage the grid
- **Data flow:** DNC sends Poke → DNBridge forwards to SCADA via IEC 104 commands
- **Registration:** DNC sends `RegisterElements` with `IsSetPoint=true`
- **C++ storage:** `cl_Client::m_CmdElements` map, looked up by `Poke`
- **C# storage:** `DncSession.CommandElements`

### C. Main104Flag Elements (Bidirectional — configuration/regulation parameters)
- **Purpose:** Regulation parameters that control DNC's data processing (voltage limits, precision, operating mode). These can be set by DNC and sent to SCADA, but SCADA can also modify them.
- **Data flow:** DNC → SCADA via Poke (same as command elements), plus SCADA → DNC (see section 4)
- **Flag:** `0xC0000000` OR-ed into the element ID
- **Known IDs** (from `Commands.h`):
  ```
  ID_104_Active      = 1   → 0xC0000001  // active regulation on/off
  ID_104_RegMode     = 2   → 0xC0000002  // regulation mode
  ID_104_RegBranch   = 3   → 0xC0000003  // regulated branch
  ID_104_UNet_max    = 4   → 0xC0000004  // max voltage limit
  ID_104_UNet_min    = 5   → 0xC0000005  // min voltage limit
  ID_104_Qvvn        = 6   → 0xC0000006  // reactive power ref
  ID_104_Q_tor       = 7   → 0xC0000007  // reactive power tolerance
  ID_104_Active_ACK  = 101 → 0xC0000065  // status acknowledgment
  ID_104_State       = 102 → 0xC0000066  // current state
  ID_104_Q_min       = 103 → 0xC0000067  // Q min limit
  ID_104_Q_max       = 104 → 0xC0000068  // Q max limit
  ID_104_Losses      = 105 → 0xC0000069  // losses
  ID_104_Weak        = 106 → 0xC000006A  // weak node
  ```
- **C++ storage:** `cl_Client::m_CmdElements` (same map as SetPoint elements) + `cl_104_Client::m_Interrog_Elements` (separate list for interrogation)
- **C# storage:** `DncSession.CommandElements` (loaded from `XChng.cfg`, temporary)

---

## 2. C++ Implementation Details

### 2.1 XChng.cfg Loading (`cl_104_Client::GetXChngCfg` — cl_104_Client.cpp:114-174)

Called at startup from `main.cpp:206-207` for each SCADA server subdirectory.

- **Format:** Tab-separated: `IEC104_Address\tID\tType`
- **Comments:** Lines starting with `//` or `#`
- **Actions per line:**
  - `FindElement(nAddr, true, true)` — find or create element in 104 client cache
  - `pElem->m_nCtrl_ID = nID | MAIN_104_Flag` — stamp with flag
  - `pElem->m_nType = nType` — set IEC 104 type
  - `m_Interrog_Elements.push_back(pElem)` — add to interrogation list
  - Special: if `m_nCtrl_ID == (ID_104_Active_ACK | MAIN_104_Flag)`, store as `m_pStatus_Element`
- **Does NOT set:** `m_bPropagate`, `m_nACK_Address`, `m_bSetPoint`

### 2.2 Registration via RegisterElements (`Commands_Srv.cpp:22-63`)

When DNC sends `RegisterElements`, each element stub is sorted:

```
if (IsSetPoint || (ID & MAIN_104_Flag) == MAIN_104_Flag):
    → m_CmdElements[ID] = element     // command map (for Poke lookup)
    → m_CmdElement_IDs[element] = ID  // reverse lookup (for reverse Poke)
    → element.m_bPropagate = stub.m_bPropagate  // from DNC
    → element.m_nACK_Address = stub.m_nACK_Address
    → element.m_bSetPoint = true
    if (ID & MAIN_104_Flag):
        → AddInterrog(element)  // also add to interrogation list
else:
    → m_Elements[ID] = element  // monitor map (for GetData)
```

**Key point:** In the current deployment, DNC does NOT send Main104 elements in RegisterElements. They are only created by XChng.cfg. This means:
- `m_bPropagate` is never set (stays `false` from constructor)
- `m_nACK_Address` is never set (stays `0`)
- They exist in `m_Interrog_Elements` (from XChng.cfg) but NOT in `m_CmdElements` (never registered by DNC)

### 2.3 Poke: DNC → SCADA (`Commands_Srv.cpp:94-141`)

```
Poke(elementID):
    lookup in m_CmdElements → if not found, silently return
    for each value in poke:
        send to SCADA via IEC 104 (MeasuredValueShort / SinglePoint / DoublePoint)
        update element.m_fValue and m_nType
```

**Current problem:** Since Main104 elements are NOT in `m_CmdElements` (DNC doesn't register them), Poke for `0xC0000001-3` silently fails in C++ too! The C++ code just returns without logging. This means Poke for these elements was likely never functional in the original deployment without DNC including them in RegisterElements.

### 2.4 GetData: SCADA → DNC (`Commands_Srv.cpp:65-92`)

```
GetData:
    iterate m_Elements (monitor elements only)
    for each element with new data since last poll:
        add to answer
```

**Main104 elements are NOT in m_Elements** — they are in m_CmdElements. So GetData never returns their values. The only mechanism for SCADA→DNC delivery would be reverse Poke (see 2.5).

### 2.5 Reverse Poke: SCADA → DNC (`cl_104_Client.cpp:265-279`)

When SCADA sends a spontaneous data update for any element:

```
ASDU_ReceivedHandler:
    find element by IEC 104 address
    update element value
    if element.m_bPropagate:
        for each DNC client:
            find element's ID via reverse lookup (m_CmdElement_IDs)
            create Poke command with new value
            send to DNC client
```

**For Main104 elements:** `m_bPropagate` is always `false` (never set by XChng.cfg, never registered by DNC). So **reverse Poke never triggers for Main104 elements in practice.**

### 2.6 Interrogation Response (`cl_104_Client.cpp:315-378`)

When SCADA sends General Interrogation (`C_IC_NA_1`):

```
Rx_Interrogation:
    send ACTIVATION_CON
    for each element in m_Interrog_Elements:
        if type == 0: skip
        determine CA/IOA (from ACK address if set, else from element address)
        send current value as IEC 104 ASDU (SinglePoint/DoublePoint/MeasuredValueShort)
    send ACTIVATION_TERMINATION
```

This is the **only actively functional behavior** for Main104 elements in C++: when SCADA asks "what are your current values?", DNBridge responds with the values DNC has Poke-d into these elements.

### 2.7 ACK Address Handling (`cl_104_Client.cpp:281-309`)

When a propagatable element with `m_nACK_Address != 0` receives data from SCADA, the C++ code sends acknowledgment commands back to SCADA using the ACK address. This handles the IEC 104 command confirmation protocol (ACTIVATION_CON + ACTIVATION_TERMINATION). **Not active for Main104 elements** since `m_bPropagate=false` and `m_nACK_Address=0`.

---

## 3. Current C# Implementation

### 3.1 XChng.cfg Loader (TEMPORARY)

**File:** `CommandExecutor.cs` — `LoadXChngCfgElements()` in `#region TEMPORARY`

- Called from `ExecuteRegisterElementsAsync()` after processing DNC-sent elements
- Reads `XChng.cfg` from exe directory (`AppContext.BaseDirectory`)
- Parses same format as C++: `Address\tID\tType`
- Creates elements in `ElementCache` with `isSetPoint=true`
- Adds to `session.CommandElements` with `ID | Main104Flag`
- **Temporary** — will be removed when DNC includes these elements in RegisterElements

### 3.2 What Works Now

- Poke for Main104 elements succeeds (no more warnings)
- Element values are stored in Element104 cache
- SCADA stub logs the send calls

### 3.3 What's Missing (not needed until real SCADA client)

- Interrogation response (C_IC_NA_1 handling)
- No `Propagate` property on `Element104` (exists on `ElementStub` but not stored)
- No reverse Poke mechanism
- No ACK address handling

---

## 4. Future C# Design — Simplified Architecture

The C++ implementation has some complexity that can be simplified in C#. The goal: **same external behavior, cleaner internals.**

### 4.1 Dual-Map Registration (Recommended)

When Main104 elements are eventually included in `RegisterElements` from DNC, register them in **both** maps:

```csharp
if ((stub.Id & Main104Flag) == Main104Flag)
{
    // Bidirectional: Poke works (CommandElements) AND GetData returns values (MonitorElements)
    session.CommandElements[stub.Id] = elem;
    session.MonitorElements[stub.Id] = elem;  // same Element104 instance in both maps
}
else if (stub.IsSetPoint)
{
    session.CommandElements[stub.Id] = elem;
}
else
{
    session.MonitorElements[stub.Id] = elem;
}
```

**Benefit:** Eliminates the need for reverse Poke entirely. When SCADA updates a Main104 element's value, it's already in `MonitorElements`, so the next `GetData` poll naturally delivers it to DNC. Same Element104 object is shared — Poke updates the value, GetData reads it.

**Trade-off:** GetData polls every ~1 minute vs. reverse Poke being immediate. For configuration parameters (voltage limits, regulation modes) this latency is acceptable.

### 4.2 Interrogation Without Separate List

Instead of maintaining `m_Interrog_Elements`, filter on the fly when handling `C_IC_NA_1`:

```csharp
// In future SCADA client, when receiving General Interrogation:
var interrogElements = session.CommandElements
    .Where(kvp => (kvp.Key & ElementStub.Main104Flag) == ElementStub.Main104Flag);

foreach (var (id, elem) in interrogElements)
{
    // Send current value back to SCADA based on elem.Iec104Type
}
```

**Benefit:** No extra bookkeeping, no list to keep in sync. The `Main104Flag` in the key is the filter criterion. The performance difference is negligible for a handful of elements.

### 4.3 Propagate Property

If reverse Poke is ever needed (for immediate SCADA→DNC delivery), add to `Element104`:

```csharp
public bool Propagate { get; set; }
```

And store it during registration:

```csharp
elem.Propagate = stub.Propagate;
```

Currently not needed because: (a) `Propagate` is always `false` for Main104 elements in practice, and (b) the dual-map approach handles SCADA→DNC via GetData polling.

### 4.4 Summary: C++ vs C# Architecture

| Feature | C++ | C# (current) | C# (future recommended) |
|---------|-----|---------------|------------------------|
| Main104 source | XChng.cfg + RegisterElements | XChng.cfg only (temp) | RegisterElements from DNC |
| Poke (DNC→SCADA) | m_CmdElements lookup | CommandElements lookup | CommandElements lookup |
| SCADA→DNC delivery | Reverse Poke (m_bPropagate) | Not implemented | GetData via dual-map |
| Interrogation list | Separate m_Interrog_Elements | Not implemented | Filter CommandElements by flag |
| Propagate flag | On element, set by DNC | Not stored | Store on Element104 if needed |
| ACK address | On element, set by DNC | Not stored | Store on Element104 if needed |

---

## 5. File References

### C++ Source (docs/SourceCodeFiles/)
- `Commands.h:64-76` — ID constants (ID_104_Active, etc.)
- `Commands.h:156` — `#define MAIN_104_Flag 0xC0000000`
- `Commands_Srv.cpp:22-63` — RegisterElements execution with element sorting
- `Commands_Srv.cpp:94-141` — Poke execution
- `Commands_Srv.cpp:65-92` — GetData execution (monitor elements only)
- `cl_104_Client.cpp:114-174` — GetXChngCfg() loading
- `cl_104_Client.cpp:229-313` — ASDU_ReceivedHandler (data receive + reverse Poke)
- `cl_104_Client.cpp:315-378` — Rx_Interrogation (interrogation response)
- `cl_104_Client.cpp:385-393` — AddInterrog()
- `cl_104_Client.cpp:775` — Element constructor (m_bPropagate=false default)
- `main.cpp:206-207` — XChng.cfg loading at startup

### C# Source (src/DNBridge/)
- `Commands/CommandExecutor.cs:178-266` — TEMPORARY XChng.cfg loader (#region)
- `Commands/CommandExecutor.cs:82-83` — Call site in ExecuteRegisterElementsAsync
- `Commands/ElementStub.cs:12` — Main104Flag constant
- `Commands/ElementStub.cs:19` — Propagate property (deserialized but unused)
- `DncServer/DncSession.cs:14-17` — MonitorElements / CommandElements maps
- `Elements/Element104.cs` — No Propagate or AckAddress properties yet
