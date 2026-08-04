# DNBridge (DNB)

> General project description, purpose, and architecture. For current implementation
> status (what is done / partial / not started), see **`docs/DevState.md`** — keep it
> updated after every implementation change.

---

## 1. Purpose

**DNBridge** (assembly/library name **DNB**) is a C# .NET 8 communication **bridge**
between two independent systems. It translates commands and data in both directions
and is wire-compatible with the legacy C++ service it replaces — neither side is
modified.

- **DNC** — the "brain". A local application that reads measured grid data
  (U, I, P, Q, switch states, …), evaluates the state of the network grid, and sends
  control commands back. It holds the network schema with measured elements (IDs +
  parameters) and the SCADA server info. DNC connects to DNBridge as a **TCP client**
  over a custom binary **TLV protocol**; **DNBridge is the TCP server** on this side.
- **SCADA** — a remote industrial control server. **DNBridge connects to it as a
  TCP client (IEC 104 master)** using **IEC 60870-5-104**; SCADA is the server (slave).

```
┌─────────┐    TLV / TCP     ┌───────────┐   IEC 60870-5-104   ┌─────────┐
│   DNC   │ ───────────────► │ DNBridge  │ ──────────────────► │  SCADA  │
│ (brain) │ ◄─────────────── │  (DNB)    │ ◄────────────────── │ (server)│
└─────────┘                  └───────────┘                     └─────────┘
  TCP client                TCP server ↔ TCP client (master)    TCP server
```

DNBridge collects spontaneous/interrogated data from SCADA into a cache, serves it to
DNC on request, and forwards DNC's control commands to SCADA.

### Origin / legacy

DNBridge is a C# rewrite of a C++ service. The original C++ projects — **DNCalc** (the
DNC brain) and **DNCors_IEC104** (the original bridge) — are documented separately and
are **not** described in detail here; this repo only needs to remain wire-compatible
with them.

### Companion repository

The C++ original and the **migration/port plan** live in the sibling repo **`../DNC`**
(both repos sit side-by-side under `C:\_EGC` on every machine; use repo-relative links).

| Repo | Role | Search there for |
|------|------|------------------|
| `../DNC` | "How to migrate" — the C++ original + its analysis, the DLL/engine spec, the living port plan | C++ behavior (`file:line`), an3f4w DLL spec, `L2_dnbridge_full_transcode.md`, per-element port reference |

**Doc rules (both repos):** see [`docs/documentation-guide.md`](docs/documentation-guide.md).
In short — knowledge about the **C++ original / port plan** goes in `../DNC` (link, never
copy); knowledge about the **.NET system being built** goes in this repo's `docs/`. The
**target Stage-1 architecture** (online voltage control, replacing the C++ `DNCors` runtime)
is in [`docs/architecture.md`](docs/architecture.md); live status is in `docs/DevState.md`.

### Scope simplifications vs. the C++ original

- Single SCADA connection and single DNC connection (architecture leaves room for more).
- **No Replay mode** — all replay code paths are skipped.
- `ACK_Address == 0` and `Propagate == false` everywhere — related branching removed.
- No BZ2 compression in TLV.

---

## 2. Technology Stack

| Layer | Technology |
|---|---|
| Language / runtime | C# 12, .NET 8 |
| IEC 60870-5-104 | **lib60870.NET** (mz-automation), referenced as **source** (`libs/lib60870`), not NuGet |
| Async model | `async/await` + `CancellationToken` throughout |
| Thread safety | `ConcurrentDictionary` for the shared `ElementCache` |
| Configuration | Custom INI reader (`dnbridge.ini`) |
| UI host | WPF, code-behind only (no MVVM) |
| Build / IDE | Visual Studio 2026, solution file `DNBridge.slnx` |

---

## 3. Solution Structure

```
DNBridge.slnx
├── src/DNBridge/          — Core class library. All business logic. No UI dependencies.
├── src/DNBridge.Wpf/      — WPF desktop host for monitoring/debugging.
└── libs/lib60870/         — IEC 60870-5-104 protocol library (source reference).
```

Planned (not yet created): `DNBServiceW` (Windows service) and `DNBServiceL`
(Linux service) hosts. The core library is host-agnostic — any host consumes it via
the `IDnbEngine` interface and its events.

Project references:
```
DNBridge.csproj      → libs/lib60870/lib60870.csproj   (source, not NuGet)
DNBridge.Wpf.csproj  → src/DNBridge/DNBridge.csproj
```

---

## 4. Library Module Map

```
DNBridge/
├── Core/            — Engine, config, lifecycle
│   ├── DnbEngine.cs / IDnbEngine.cs   — orchestrator + public host interface
│   ├── Config.cs                      — INI reader (Load / Validate / Log)
│   └── DnbConfig.cs                   — configuration model
├── Tlv/             — Binary TLV serialization engine
│   ├── ITlvSerializable.cs, TlvReader.cs, TlvWriter.cs, TlvTags.cs, TlvObjectFactory.cs
├── DncServer/       — TCP server for DNC
│   ├── DncTcpServer.cs, DncClientHandler.cs, DncSession.cs, IDncClientHandler.cs
├── Commands/        — TLV command/answer DTOs + execution
│   ├── DnbCommand.cs, CommandExecutor.cs
│   ├── Init / RegisterElements / GetData / Poke (command + answer pairs)
│   ├── ElementStub.cs, ElementValue.cs
│   └── PokeValue.cs (+ Bool / Float / FourState subtypes)
├── Scada/           — IEC 104 client
│   ├── IScadaClient.cs, ScadaClient.cs   — lib60870 wrapper: connect, reconnect, ASDU, send
├── Elements/        — Shared element cache
│   ├── Element104.cs, ElementCache.cs
└── Events/          — EventArgs raised toward the host
    ├── LogEventArgs.cs, IsRunningEventArgs.cs
    ├── DncConnectionEventArgs.cs, ScadaConnectionEventArgs.cs
    ├── CommandReceivedEventArgs.cs, ScadaDataEventArgs.cs
    ├── ElementsRegisteredEventArgs.cs, ElementValueChangedEventArgs.cs, ElementPokeEventArgs.cs
```

**`DnbEngine`** owns and wires all components: it creates the shared `ElementCache`,
the `ScadaClient`, the `CommandExecutor`, the `DncSession`, and the `DncTcpServer`,
then re-raises their events to the host.

---

## 5. DNC Side — TCP / TLV

### Transport

- DNBridge listens on a configurable TCP port (default **9000**). One DNC client at a
  time; max payload **128 KB**; malformed/oversized frames cause disconnect.
- **TLV (Tag-Length-Value)** binary format, **little-endian**, 8-byte headers
  (`uint32 Tag` + `uint32 Length`), no padding.
- Class tags (nested containers) have bit `0x80000000` set; attribute tags (leaf
  values) do not. Every message is wrapped in an outer envelope (`Tag=0`, `Length=N`).

### Command protocol

After connecting, DNC drives DNBridge through four commands:

| # | Command | Direction | Purpose | Answer |
|---|---------|-----------|---------|--------|
| 1 | **Init** | DNC → DNB | Establish session, provide SCADA server name | InitAnswer (OK, Mode=IEC104) |
| 2 | **RegisterElements** | DNC → DNB | Register elements to monitor/control | RegisterElementsAnswer (OK) |
| 3 | **GetData** | DNC → DNB | Poll updated element values (paginated, ≤30/answer) | GetDataAnswer (values, IsFinal) |
| 4 | **Poke** | DNC → DNB | Write a value to a SCADA element | *none (fire-and-forget)* |

- **Init** stores the server name only — it does **not** connect to SCADA.
- **RegisterElements** finds/creates each `Element104`, sorts it into the session's
  **MonitorElements** (GetData) or **CommandElements** (Poke) map — setpoints and
  elements carrying the **Main104Flag** (`0xC0000000`) go to CommandElements — loads
  optional `XChng.cfg` command elements, then initiates the SCADA connection.
- **GetData** snapshots the monitor list on `Start=true` and pages through it with a
  per-session cursor, returning elements with `LastDataTime > NewerThan`.
- **Poke** looks up the target by schema ID and calls the matching `ScadaClient.Send*`:
  `FloatPokeValue → C_SE_TC_1` (with current-time CP56Time2a), `BoolPokeValue → C_SC_NA_1`, `FourStatePokeValue → C_DC_NA_1`.

---

## 6. SCADA Side — IEC 60870-5-104

- Connection lifecycle: connect → wait `STARTDT_CON` → mark connected → send
  **General Interrogation** (CA=`0xFFFF`, QOI=20).
- **Auto-reconnect** with server-address rotation: on close/failure, rotate to the next
  configured address and retry after 5 s. Reconnect is de-duplicated; stale callbacks
  from old attempts are ignored via an attempt-id guard.
- **ASDU receive handler** updates the shared `ElementCache` directly. Address is
  `(CA << 24) | IOA`; unregistered addresses are silently skipped. `_TB_`/`_TD_`/`_TE_`/
  `_TF_` types carry a CP56Time2a timestamp; others are stamped with `DateTime.UtcNow`.
  GI and command confirmations (ACT_CON/ACT_TERM) are logged, not forwarded to DNC.
- **Send (control) commands** map Poke values to `C_SE_TC_1`, `C_SC_NA_1`, `C_DC_NA_1`
  (the float setpoint is sent as `C_SE_TC_1` (63) — short float **with** a CP56Time2a tag
  stamped with the current local time), guarded by an `EnsureConnected` check; the raw COT byte maps to
  `lib60870.CauseOfTransmission` (falls back to `ACTIVATION`). SBO (Select-Before-
  Operate) parameters exist on the signatures but direct-execute is used for now.

**Supported monitoring TypeIDs (SCADA → DNB):**

| TypeID | Name | Value |
|--------|------|-------|
| M_SP_NA_1 (1)  | Single Point | bool → 0.0/1.0 |
| M_DP_NA_1 (3)  | Double Point | int 0–3 |
| M_ST_NA_1 (5)  | Step Position | int |
| M_ME_NA_1 (9)  | Measured Normalized | float |
| M_ME_NB_1 (11) | Measured Scaled | int |
| M_ME_NC_1 (13) | Measured Short Float | float |
| M_ME_ND_1 (21) | Measured Normalized (no quality) | float |
| M_SP_TB_1 (30) | Single Point + CP56Time | bool |
| M_DP_TB_1 (31) | Double Point + CP56Time | int |
| M_ST_TB_1 (32) | Step Position + CP56Time | int |
| M_ME_TD_1 (34) | Measured Normalized + CP56Time | float |
| M_ME_TE_1 (35) | Measured Scaled + CP56Time | int |
| M_ME_TF_1 (36) | Measured Short Float + CP56Time | float |
| C_IC_NA_1 (100)| Interrogation (confirmations) | — |

**Control TypeIDs (DNB → SCADA):** `C_SC_NA_1` (45, single), `C_DC_NA_1` (46, double),
`C_SE_TC_1` (63, setpoint short **with CP56Time2a**, current local time), `C_IC_NA_1` (100, GI).

---

## 7. Element System

- **Address encoding** — IEC 104 address packed into a `ulong`:
  bits 39–24 = CA (uint16), bits 23–0 = IOA (uint24); upper bytes zero.
  String form `"CCC.CCC.III.III.III"`, e.g. `"000.001.000.000.005"` = CA=1, IOA=5.
- **`Element104`** — Address (immutable) + derived CA/IOA, Value (`double`, all types
  normalized), Quality (uint), LastDataTime, Iec104Type (byte), IsSetPoint.
- **`ElementCache`** — thread-safe `ConcurrentDictionary<ulong, Element104>` shared
  between the SCADA ASDU handler (writes), the CommandExecutor (GetData reads / Poke
  writes), and the engine (`All` view for the UI).
- **`DncSession`** maps DNC schema IDs → cached elements: `MonitorElements` (GetData)
  and `CommandElements` (Poke). The **Main104Flag** (`0xC0000000`) on the schema ID
  marks control/regulation elements during registration.

---

## 8. Configuration — `dnbridge.ini`

Loaded from the application directory; defaults used when absent. The engine **logs**
the effective config and **validates** it on start — it refuses to start if the TCP
port is invalid or no SCADA addresses are configured.

```ini
[Config]
Log_File=C:\Logs\dnbridge.log   ; log file path
Log_Level=2                      ; 0=Trace 1=Debug 2=Info 3=Warning 4=Error 5=Fatal
TCP_Port=9000                    ; DNC listener port

[Scada]
Addresses=192.168.1.10:2404;192.168.1.11:2404   ; semicolon-separated host:port (port defaults to 2404)
Scada_log=C:\Data\dnbridge       ; optional folder; when set, logs SCADA traffic to scada_MMdd.log (one file per date)
```

**`XChng.cfg`** (temporary) — optional tab-separated file (`IEC104_Address⇥ID⇥Type`)
that pre-registers extra command elements; each ID is OR-ed with `Main104Flag` and
added to CommandElements. To be removed once DNC includes these in RegisterElements.

---

## 9. Events (Engine → Host)

The library never calls into UI code; `DnbEngine` raises events the host subscribes to:

| Event | When |
|-------|------|
| `LogMessage` | any log-worthy activity (filtered by configured log level) |
| `IsRunningChanged` | engine started / stopped |
| `DncConnectionChanged` | DNC client connects / disconnects |
| `ScadaConnectionChanged` | SCADA connection state changes |
| `DncCommandReceived` | TLV frame received from DNC |
| `ScadaDataReceived` | ASDU received from SCADA |
| `ElementsRegistered` | RegisterElements processed (monitor / Main104 / command lists) |
| `ElementValueChanged` | cached element values updated from SCADA |
| `ElementPokeConfirmed` | a Poke was sent to SCADA |

---

## 10. Key Design Decisions

1. **Init does not connect to SCADA** — connection happens during RegisterElements,
   after all elements are in the cache.
2. **Single-client model**, but per-connection handlers allow future multi-client.
3. **GetData uses snapshot pagination** to avoid enumerator invalidation during
   concurrent SCADA updates.
4. **Poke has no answer** (matches C++ behavior).
5. **Values normalized to `double`** for uniform storage and GetData responses.
6. **SCADA reconnect is non-blocking** (scheduled via `Task.Run`, attempt-id guard).
7. **`Connection.Close()` is never called from inside a lib60870 handler** (deadlock).
8. **WPF host is a thin shell** — code-behind only, no MVVM.

---

## 11. TLV Protocol Reference (DNC side)

Wire-compatible reimplementation of the C++ `cl_Serializer`. **Must match the C++ wire
format byte-for-byte.**

**Binary format**
```
Outer envelope:  uint32 Tag = 0x00000000 | uint32 Length = <payload size>
Payload:         one or more nested TLV records:
                 uint32 Tag | uint32 Length | byte[Length] Value
```
- 8-byte headers (`uint32 Tag` + `uint32 Length`), packed, **little-endian**.
- Class tags have bit `0x80000000` set (nested TLV containers); attribute tags do not
  (raw leaf values).
- **DateTime** is an `int64` = **milliseconds since Unix epoch (Jan 1 1970 UTC)** —
  resolved; mirrors C++ `wxDateTime::GetValue()`.
- Value types: Bool (uint8 0/1), U8/U16/U32/U64/I16/I32, Double (IEEE-754 8 bytes),
  UTF-8 string (null-terminated).

**Tag constants** (defined in `Tlv/TlvTags.cs`; mirror C++ `DN_Cors_tag.h` / `tlv_tag.h`).
`TLV_CLASS = 0x80000000`.

Class tags (bit 31 set): `COMMAND 0x80101000`, `ANSW_ACK …002`, `CMD_QUIT …004`,
`CMD_QUIT_ACK …005`, `CMD_INIT …010`, `ANSW_INIT …011`, `CMD_REG_ELEMS …012`,
`ANSW_REG_ELEMS …013`, `CMD_GET_DATA …014`, `ANSW_DATA …015`,
`REPLAY_CMD …016` / `REPLAY_ANSW …017` *(skipped — no Replay)*, `POKE_CMD …018`,
`ELEM_STUB 0x80102000`, `VALUE …2006`, `POKE_FLT …2010`, `POKE_BOOL …2011`,
`POKE_4STATE …2012`. *(Source also defines unused `VALUE_SW/GEN/MEAS/NODE/TRANSF`.)*

Attribute tags: `u32_ID 0x00000001`, `sz8_Name 0x00000002`, `u32_Client_ID 0x00110001`,
`sz8_IP …003`, `u16_Port …004`, `sz8_Server …005`, `b_OK …006`, `dt_DateTime …008`,
`b_Value …009`, `dbl_Value_U …00A`, `dbl_Value_P …00B`, `dbl_Value_Q …00C`,
`u8_Mode …00D`, `u32_Value …00E`, `dbl_Value …010`, `dt_To …011`, `u64_104ADDR …012`,
`b_SetPoint …013`, `u32_Quality …014`, `b_Propagate …015`, `u64_ACK_104ADDR …016`,
`u8_COT …017`, `u8_104TYPE …018`, `u8_Value …019`.

> ⚠️ **Tag collision:** `dbl_Value` and `dt_From` are both `0x00110010`. `dt_From` is
> only used by Replay commands (skipped), so there is no ambiguity in the C# version.

---

## 12. Conventions / Working Rules

1. **Never change the wire protocol** on either side — DNC TLV and SCADA IEC104 are fixed.
   Verify byte order, packing, and tag values against the C++ original before finalizing TLV.
2. **`async/await` everywhere** for I/O, with `CancellationToken` in async signatures.
3. **All business logic lives in `src/DNBridge`**; hosts (WPF, future services) stay thin.
4. **Library never calls into UI** — it raises events; hosts subscribe.
5. **Access `ElementCache` thread-safely** — SCADA callback and DNC handler touch it from
   different threads.
6. **No Replay; `ACK_Address == 0`, `Propagate == false`** — related branching is removed.
7. **Preserve `Main104Flag` (`0xC0000000`)** logic in RegisterElements.
8. **Logging** is a custom `Action<string, DnbLogLevel>` callback surfaced as the
   `LogMessage` event (no `Microsoft.Extensions.Logging`).
9. After any implementation change, **update `docs/DevState.md`**.

---

## 13. Build & Run

```sh
dotnet build DNBridge.slnx
dotnet run --project src/DNBridge.Wpf        # WPF monitoring host
```

Place `dnbridge.ini` (and optional `XChng.cfg`) next to the host executable.

---

## 14. Execution policy
  For quick, simple or medium tasks, work directly in the main session.
  Only spawn parallel subagents when a task genuinely splits into independent big
  workstreams (e.g. multiple unrelated modules/files) or would blow the context
  window otherwise. Default to sequential execution.

---

## 15. Documentation Index

| File | Contents |
|------|----------|
| `CLAUDE.md` (this file) | General description, purpose, tech stack, architecture, TLV/IEC104 reference — describes the **current DNC-connected** system |
| `../DNC/Doc/App_migration/overview/functional_blocks.md` | **★ Migration block map (B1–B31) + current decisions** — read first for any work on the DNC-free migrated runtime |
| `docs/DevState.md` | **Live implementation status** — keep current after every change |
| `docs/AI_GUIDE_SCADA_WRAPPER.md` | ScadaClient implementation guide |
| `docs/Main104_Elements_Analysis.md` | Main104 regulation element analysis |
| `docs/Main104_calc_usage.md` | How the 7 Main104 input params drive the DNC calc (DLL text input vs. C++ control); Calc_Kind stages — migration reference |
| `docs/_a/`, `docs/_fa/` | Analysis of the original C++ source (DNCalc / DNCors_IEC104) |
