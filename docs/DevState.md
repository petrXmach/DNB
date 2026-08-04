# DNBridge — Development State

> **IMPORTANT FOR AI:** Update this file after every implementation change.
> When completing a task, update the relevant section status and move items between categories as needed.
> Keep descriptions factual and concise — this file is your primary orientation reference.

---

## Quick Status Overview

| Layer | Status | Notes |
|-------|--------|-------|
| TLV Engine | DONE | Reader, Writer, Tags, Factory, ITlvSerializable |
| Commands / DTOs | DONE | All command & answer classes with Serialize/Deserialize |
| DNC TCP Server | DONE | Server + ClientHandler, receive loop, send, single-client |
| Command Execution | DONE | CommandExecutor dispatches Init/RegElems/GetData/Poke |
| Element Cache | DONE | ElementCache with FindOrCreate, Find, thread-safe |
| DNC Session | DONE | DncSession with element maps + GetData pagination cursor |
| SCADA IEC104 Client | DONE | Real lib60870 client: connect, reconnect, ASDU receive, send commands, ElementsUpdated event |
| SCADA Replay Mode | DONE | Offline snapshot replay: `ReplayScadaClient` (drop-in `IScadaClient`) + swappable `SnapshotTableReader`; feeds recorded measurements into the cache, no live SCADA |
| Engine Integration | DONE | DnbEngine wires executor, cache, ScadaClient, session; re-raises element-cache events |
| WPF Host | PARTIAL | Start/stop, logs, DNC/SCADA traffic, 3 live element grids (Monitor/Main104/Setpoints), **DLL TEST** button → calc-engine test window (DLL probe + per-kind calc checklist), **Replay** source selector (Live SCADA / Replay + Browse + Inject) |
| Config (INI) | DONE | Config.cs reads dnbridge.ini; DnbConfig model + Validate()/Log(); engine validates on start |
| Test Client | DONE | Console app, sends TLV frames for manual testing |
| Calc Engine (an3f4w) | PARTIAL | `DNBridge.Calc`: P/Invoke surface + smoke test + generic per-kind run (`An3f4wCalcTest`) — dvChod/Oper (kind 5) and QMin/QMax/Loss/Optim (kind 21 dvOrpf), each from a `data/` dump. Lifecycle + summary + sample struct decode. Covers migration blocks **B20** (done) / **B21–B22** (partial). |
| Migrated path (DNC-free runtime) | NOT STARTED | Blocks **B1–B19, B23–B27** — `.egc3` model, topology, transcode, PQ split, stage chain, dispatch, snapshots. Plan + decisions: [`../../DNC/Doc/App_migration/overview/functional_blocks.md`](../../DNC/Doc/App_migration/overview/functional_blocks.md). Existing DNC-connected path stays functional in parallel (coexistence/layout **open**). |

---

## Detailed Component Status

### DONE — Fully Implemented

**Tlv/** — Binary TLV serialization engine
- `ITlvSerializable.cs` — interface with default Serialize/Deserialize
- `TlvTags.cs` — all TAG_* constants, GetName(), IsClassTag()
- `TlvReader.cs` — recursive Deserialize, all primitive readers
- `TlvWriter.cs` — all primitive writers, SerializeObject, ToEnvelope
- `TlvObjectFactory.cs` — factory creating command instances by tag

**Commands/** — All command/answer/value DTOs + execution
- `DnbCommand.cs` — base class (SessionId, Answer, Serialize/Deserialize)
- `InitCommand.cs` / `InitAnswer.cs`
- `RegisterElementsCommand.cs` / `RegisterElementsAnswer.cs`
- `GetDataCommand.cs` / `GetDataAnswer.cs` (MaxRecordsPerAnswer=30, pagination)
- `PokeCommand.cs` / `AckAnswer.cs`
- `ElementStub.cs` (Id, Address104, AckAddress, Iec104Type, IsSetPoint, Propagate, Main104Flag) — `AddrToStr()` + `TryParseAddress()` (both parse directions co-located)
- `ElementValue.cs` (Address, Value, Quality, DateTime)
- `PokeValue.cs` (abstract) + `BoolPokeValue.cs`, `FloatPokeValue.cs`, `FourStatePokeValue.cs`
- `CommandExecutor.cs` — dispatches all 4 command types: Init (stores ServerName, returns InitAnswer), RegisterElements (builds element maps, loads XChng.cfg, connects SCADA), GetData (paginated iteration), Poke (SCADA send per value type). Exposes `OnElementsRegistered` + `OnPokeExecuted` callbacks consumed by DnbEngine. **TEMPORARY:** `LoadXChngCfgElements()` loads command elements from `XChng.cfg` (tab-separated: Address, ID, Type) in exe directory, OR-s IDs with Main104Flag, adds to session.CommandElements. Wrapped in `#region`, remove when DNC includes these elements in RegisterElements.

**DncServer/** — TCP server for DNC connections
- `IDncClientHandler.cs` — interface
- `DncTcpServer.cs` — TcpListener, accept loop, single-client, events, ClientHandlerCreated event for executor injection
- `DncClientHandler.cs` — receive loop, TLV deserialization, executor dispatch + answer send, SetExecutor() method
- `DncSession.cs` — per-connection state: ServerName, MonitorElements/CommandElements maps, GetData pagination cursor (ResetDataCursor/GetNext/IsCursorExhausted)

**Elements/**
- `Element104.cs` — Address (ulong with CA/IOA bit layout), Value, Quality, LastDataTime, Iec104Type, IsSetPoint
- `ElementCache.cs` — thread-safe ConcurrentDictionary wrapper, FindOrCreate, Find, All, Clear

**Events/** — All event arg classes
- `LogEventArgs.cs` (DnbLogLevel enum), `CommandReceivedEventArgs.cs`, `DncConnectionEventArgs.cs`, `ScadaConnectionEventArgs.cs`, `ScadaDataEventArgs.cs`, `IsRunningEventArgs.cs`
- `ElementsRegisteredEventArgs.cs` (Monitor/Main104/Command element lists), `ElementValueChangedEventArgs.cs` (value updates), `ElementPokeEventArgs.cs` (Poke confirmation)

**Scada/** — IEC 60870-5-104 SCADA client (lib60870.NET wrapper)
- `IScadaClient.cs` — interface: ConnectAsync(ct), DisconnectAsync, SendSetpointShort/SendSingleCommand/SendDoubleCommand (with useSbo param), ConnectionChanged/DataReceived/ElementsUpdated events
- `ScadaClient.cs` — real lib60870.CS104.Connection wrapper: accepts server addresses list in constructor (from DnbConfig.ScadaAddresses); connect with STARTDT, GI on connect, reconnect loop with server rotation, ASDU receive handler (14 monitoring TypeIDs + GI/command confirmations), 3 send control commands (C_SE_NC_1, C_SC_NA_1, C_DC_NA_1) with ConnectionException handling, EnsureConnected guard. Reconnect scheduling is de-duplicated and stale callbacks from old attempts are ignored via attempt-id guard; lifecycle state transitions are simplified to avoid duplicate disconnect state. Poke COT byte is mapped to lib60870 CauseOfTransmission. SBO parameter accepted but not yet implemented (direct execute only).

**Scada/Replay/** — Offline SCADA snapshot replay (no live SCADA)
- `ReplaySample.cs` — stable reader→injector contract: `record (string Name, string Qty, uint Ioa, double Value /*raw*/, byte Iec104Type)`. Value is the raw SCADA magnitude (the file's `scadaIn` column, as SCADA delivered it).
- `IReplayReader.cs` — the single swappable seam for the input file format: `Read(path, log) → IReadOnlyList<ReplaySample>`. All parsing lives here.
- `SnapshotTableReader.cs` — reader for the current `*_snapshot_ok.txt` dumps. Columns: `name qty id ioaIn ioaOut scadaIn scadaOut dllIn dllOut valid` (raw-SCADA `scadaIn`/`scadaOut` pair + SI DLL `dllIn`/`dllOut` pair; col widths adjusted). Splits rows on `\s{2,}` for name/qty/id/ioaIn (keeps space-containing names intact; these 4 stay positionally stable). Reads the value **header-anchored**: values are right-aligned so a value's last char lines up with its header's last char, so it locates the `scadaIn` header once (end column) and, per row, takes the text up to that column and reads the last whitespace-delimited token (right edge = header's right edge; scan left to first space; comma decimal → dot). A decimal separator is required, so a blank `scadaIn` correctly yields "no value" instead of grabbing an earlier integer column. Binds to the *named* column (can't pick a neighbour), self-calibrates on width changes. `scadaIn` is the raw value before the DLL's *nasobitel* is applied, so it is used **directly — no inverse scaling**. Stamps `Iec104Type` (stav→M_DP_NA_1, else M_ME_NC_1) for the UI.
- `IReplaySource.cs` / `ReplayScadaClient.cs` — drop-in `IScadaClient` (so engine/CommandExecutor/events are unchanged). `ConnectAsync` marks connected (status shows "Replay") but does **not** auto-inject; `Send*` are no-ops (nothing leaves the process); `LoadFile`/`Inject` update the cache exactly like `ScadaClient.OnAsduReceived` (find by `(1<<24)|IOA`, skip unregistered, stamp Value/Quality=0/LastDataTime=now/Type) and raise the same `ElementsUpdated`/`DataReceived` events. CA is fixed at 1 for this deployment.

**Core/DnbEngine.cs** — Main orchestrator
- Parameterless constructor loads dnbridge.ini via Config.Load()
- Creates ElementCache, ScadaClient (with cache + ScadaAddresses from config), CommandExecutor, DncSession
- DncTcpServer started on Config.TcpPort (no longer hardcoded)
- Injects executor into DncClientHandler via ClientHandlerCreated event
- Wires all events: DNC connection, SCADA connection, frame received, log, plus element-cache events (ElementsRegistered, ElementValueChanged, ElementPokeConfirmed)
- Logs + validates config on StartAsync (refuses to start if TCP port invalid or no SCADA addresses — the SCADA-address check is skipped in replay mode)
- Clears session on DNC connect/disconnect; disconnects SCADA on stop
- **Replay mode:** `ReplayMode`/`ReplayFilePath` (set before Start) branch the data source to `ReplayScadaClient` instead of the live `ScadaClient` (same event wiring); `LoadReplayFile()`/`InjectReplay()` forward to it. A file chosen before Start is pre-loaded.

**Core/Config.cs** — INI config reader
- `Config.Load(path?)` — reads dnbridge.ini from exe dir, returns populated DnbConfig
- `Config.FileName` constant; `Config.ParseAddresses()` for the address list
- Sections: `[Config]` (Log_File, Log_Level, TCP_Port), `[Scada]` (Addresses, TimeIsUtc, Scada_log)
- `[Scada] Addresses` — semicolon-separated list of "host:port" or "host" (default port 2404)
- `[Scada] Scada_log` — optional **folder**; when set, `ScadaFileLogger` (`src/DNBridge/Scada/ScadaFileLogger.cs`) writes SCADA traffic there, one file per date (`scada_MMdd.log`), same line format as the SCADA Traffic detail pane, with a blank-line + `*** App started <datetime>` banner on each engine start
- Returns defaults when file is absent

**Core/DnbConfig.cs** — Configuration model
- Properties: LogFile, LogLevel (default 2), TcpPort (default 9000), ScadaLog
- `ScadaAddresses` (List of (Host, Port)) — populated by Config.Load()
- `Log(...)` dumps effective config; `Validate(...)` returns false (and logs errors) if TcpPort invalid or no SCADA addresses

### PARTIALLY Done

**DNBridge.Wpf/** — WPF host application
- `MainWindow.xaml.cs` — Start/Stop buttons, 9 event subscriptions (log, running, DNC/SCADA connection, DNC/SCADA traffic, ElementsRegistered, ElementValueChanged, ElementPokeConfirmed), clean dispose
- Three live DataGrids: Monitor, Main104 (13 named regulation params), Setpoints; tab headers show counts; rows update on value change / poke
- DNC Traffic & SCADA Traffic tabs use a shared `TrafficLogView` master/detail control (`TrafficLogView.xaml` + `TrafficItem.cs`): master `ListBox` shows one colored summary line per frame, clicking shows full detail in the pane below. Both bounded to 500 rows.
- **Both directions** shown, colored: received (blue) / sent (dark red `#B71C1C`) per the legend's ◀/▶ swatches — row color is driven by `Outbound`, not by text; DNC lines still spell out the arrow glyph, SCADA lines use a leading `R`/`S` field instead (see below). DNC: inbound commands + outbound answers (`CommandReceivedEventArgs.IsOutbound`). SCADA: inbound ASDUs/confirmations + outbound control commands & GI (`ScadaDataEventArgs.IsOutbound`, raised from `ScadaClient.RaiseTraffic`).
- Summary vs detail split: commands expose `GetSummary()` (master line) + `GetDetails()` (detail pane); `GetDataAnswer`/`RegisterElements` emit per-value/per-element detail lines; addresses shown as decimal IOA via `ElementStub.AddrToIoaDecimal`.
- **Traffic line format now diverges by tab** — DNC/`GetDataAnswer` keep the labeled format described below (`TrafficFormat.Summary`/`DetailLine`); **SCADA Traffic** uses a compact positional format instead (`TrafficFormat.ScadaMaster`/`ScadaDetailLine`): master `R/S; hh:mm:ss; typeId; typeName; N elem.[; CA=n]; COT`, detail ` ; hh:mm:ss; OK/KO/-; {ioa,4}; value(≤3 decimals)[; note]`, multi-element blocks sorted by IOA ascending.
- **Unified traffic line format (DNC / GetDataAnswer)** — `TrafficFormat` (`src/DNBridge/TrafficFormat.cs`): master `{n} element(s) (COT=…)`, detail `IOA=…; Value=…; Quality=…; Time=hh:mm:ss.fff` (missing fields omitted, never faked — `ScadaClient.CarriesQuality()` drops `Quality=` for control types C_SC/C_DC/C_SE, which carry a command qualifier (QU/QOS) rather than a quality descriptor, and for M_ME_ND_1, which is defined without one; `ExtractValue` returns 0 for those as a placeholder, not a "good" reading; times always local; a frame-specific `note` appends `confirmed` / `REJECTED` / `SUPPRESSED (replay)`). Used by `GetDataAnswer`; `ScadaClient`/`ReplayScadaClient` now use the SCADA-specific format above instead. Outbound lines show the mapped `CauseOfTransmission` name instead of the raw COT byte so both directions read alike.
- **Address display rule (logs + traffic tabs):** always the **decimal IOA** (`IOA=1107`), never the dotted `CCC.CCC.III.III.III` wire form — `TrafficFormat.Address(ulong)` for a packed address, `TrafficFormat.CaPrefix(int)` for a bare CA. The CA is **suppressed while it equals `TrafficFormat.DefaultCa` (1)** and printed only when it genuinely differs. Deliberate exceptions that always print the CA: the GI request/confirmation (broadcast CA `0xFFFF`) and the "no elements matched cache" warning (a wrong CA is what it diagnoses). `ElementStub.AddrToStr` is now **config/wire-form only** (XChng.cfg parsing, inverse of `StrToAddr`) and must not be used for display; `AddrToIoaDecimal` was removed as superseded.
- `MainWindow.BuildDetail` appends the payload body whenever it adds anything beyond the master line. It previously tested for a newline, which silently discarded the detail of every **single-object** frame (one-IOA ASDU, outbound command) — those showed the summary twice.
- DNC line = `[time] {arrow} {Summary}` — the TLV tag name is dropped (each `GetSummary()` is self-labeled: `Poke:`, `GetData:`, `GetDataAnswer:`, …). SCADA line keeps its ASDU type prefix (`C_SE_TC_1:`, `M_ME_TF_1:`, …), which is not duplicated in the value summary.
- **Poke** lines are enriched via `PokeTrafficFormatter.FormatPokeSummary(cmd, session)`: friendly label + decimal SCADA IOA + value, e.g. `Poke: Active [IOA 1103] = Bool(True, COT=3)` (Main104) or `Poke: id 1687 [IOA 910] = Float(...)` (setpoint — schema name is not on the wire). IOA resolved from `session.CommandElements` (setpoints/Main104 outputs) or reverse-looked-up in `session.Main104Inputs`. Main104 names come from the shared `Commands/Main104Catalog` (also used by the WPF Main104 grid).
- Log vs Traffic separation: the bottom **Log** is lifecycle + warnings/errors only; actual protocol messages live in the traffic tabs. Protocol echoes (sent DNC answers, GI/command confirmations) are demoted to Debug so they don't duplicate at the default Info level; **command rejections stay at Warning**.
- Code-behind only, no MVVM
- **Calc-engine test window:** the main window has a single **DLL TEST** button → non-modal `CalcTestWindow` (`CalcTestWindow.xaml[.cs]`). Inside: a **Test an3f4w DLL** button (basic probe → `An3f4wSmokeTest.Run`, shows version/error) plus a checklist of per-kind calculation tests built in code-behind from `An3f4wCalcTest.Catalog` (dvChod, Oper, QMin, QMax, Loss, Optim). Each row = checkbox + input-dump path (preset from the resolved `data/` dir) + Browse. **RUN CALCULATIONS** runs every ticked test sequentially into one timestamped trace log (Select all / Clear all / Clear log helpers, shared `createOutFiles` toggle). Engine runs off the UI thread (one `Task.Run`); log lines marshalled back via `Dispatcher`.
- Missing: settings/config-editor UI, manual command injection toward DNC/SCADA

**DNBridge.Calc/** — native calculation-engine wrapper (first migration step toward replacing the C++ DNCalc/DNCors runtime; see `docs/architecture.md` §3/§5)
- `Native/An3f4w.cs` — raw P/Invoke surface for the engine (x64 Delphi, `an3f4w.dll`, renamed from `wxbase_supp64.dll`). `__stdcall`; `LongBool` as `int`; returned `wchar_t*` read via `Marshal.PtrToStringUni` (never freed). Declared: `anInitLibrary`, `anDoneLibrary`, `anDLLVersion`, `anGetErrorMsg`. `[DllImport]` uses the **extension-less logical name `"an3f4w"`** for portability.
- `Native/NativeLoader.cs` — `[ModuleInitializer]`-registered `DllImportResolver`. On Windows pre-loads `borlndmm.dll` (must load before the engine) then `an3f4w.dll`; on a future Linux build loads `liban3f4w.so`. Keeps all call sites OS-agnostic.
- `An3f4w.cs` (run path) — adds `anReadInpData(IntPtr utf16)`, `anRunAnalysis(uint kind, int outFiles)`, `anCheckFinishStatus`, `anGetFinishMessage`, `anGetFinishFrequency`, `anGetNodeCount`, `anGetBranchCount`, `anGetSystemLosses1f` (returns 16-byte `AnCplx` by value). Input passed as a pinned UTF-16LE byte buffer (+ trailing NUL), not string auto-marshalling.
- `An3f4wEngine.cs` — **shared engine lifecycle** (owns `Gate` + init/done state). `Reset(log)` brings the engine to a clean initialized state before each run: it calls `anDoneLibrary` **only when already initialized**, then `anInitLibrary`. This mirrors the proven C++ sequence `Init | Done Init | Done Init | …` (Init once at `Open`, then Done+Init per calc) — the key being the process **never starts with a bare `anDoneLibrary`**. A bare Done on a never-initialized engine corrupts it so the *next* `anInitLibrary` returns 0 (the observed "second run / multi-kind run fails with anInitLibrary failed" bug). Both the smoke test and calc tests route through `An3f4wEngine`, keeping one consistent init state.
- `An3f4wSmokeTest.cs` — `An3f4wEngine.Reset` → read `anDLLVersion`; serialized behind `An3f4wEngine.Gate` (engine is single-instance / not thread-safe). Leaves the engine **initialized** (C++ resting state; no `anDoneLibrary`). Verified live: returns version `16.1.0331.EGC`.
- `An3f4wCalcTest.cs` — generic per-kind analysis run (was `An3f4wDvChodTest`). `Run(name, calcKind, inputText, log, createOutFiles)`: `anDoneLibrary → anInitLibrary → anReadInpData → anRunAnalysis(calcKind, outFlag) → anCheckFinishStatus`, then reports finish message, node/branch counts, system losses (P+jQ), frequency, and a sample decode of the first 5 node + 5 branch records. **The same `anRunAnalysis(kind,…)` entry point drives every kind** — only the kind and the objective embedded in the input text differ (per `../DNC/.../L2_dll_orpf_scada.md` §2): OPER/dvChod = kind 5, QMIN/QMAX/LOSS/OPTIMIZE = kind 21 (dvOrpf). **Between tests: nothing special** — each run resets the engine itself (Done+Init), mirroring C++ `cl_Calculation::Do_Calculate3` which resets on every calc. `Catalog` lists the six built-in tests (name/description/kind/default file); `ParseCalcKind(text, fallback)` reads the dump's `calc_kind=N` header (authoritative; catalog kind is the fallback). Every native call + params + return is pushed through an `Action<string>` log callback. Two success gates; never throws on an expected engine failure. `createOutFiles` → `anRunAnalysis(…,1)` + a temp `anSetFileName`.
  - **Struct decode by explicit byte offsets** (verified against `../DNC/DNCalc/EVlivy3/AN3_Iface.h`): `AN_NODE_DATA_4_T` = 564 B (Pack=1, incl. `m_Padding[32]`), `AN_BRANCH_DATA_4_T` = 1248 B (incl. `m_Padding[40]` + per-port `m_Padding[64]`). Reads `m_szID`, node `|U|abc`/`Sinj`, branch from-to/`S_port0`/losses. `m_nCheck` sanity tripwire (nodes 1-based, branches 0-based) flags layout/version mismatch on DLL swaps.
  - **DLL getter status (updated 2026-06-26):** an even newer `an3f4w.dll` (`v16.1.0331.EGC`, fetched 2026-06-26) **fixes the in-RAM getters** — `anGetNodeData`/`anGetBranchData` now return real node voltages and branch S/losses. (`anGetSystemLosses1f` still reported 0+j0 on a QMin/dvOrpf run; the per-branch `m_DeltaSabc` decode does carry losses — system-loss getter on dvOrpf TBD.) The earlier "BIN3 nejsou k dispozici" regression is gone with this build.
  - **⚠ Init/Done ordering bug (fixed 2026-06-26):** running a test **twice**, or several kinds in one RUN, failed after the first with `anInitLibrary failed` (second `anInitLibrary` returned 0). Cause: our reset started with a **bare `anDoneLibrary`** on a never-initialized engine, which corrupts it so the next `anInitLibrary` fails. The C++ host never hits this because `cl_AN3_Lib::Open` does one `anInitLibrary` first (sequence `Init | Done Init | …`). Fixed by `An3f4wEngine.Reset` calling `anDoneLibrary` only when already initialized.
- Native DLLs committed inside the project at `src/DNBridge.Calc/Native/` (`an3f4w.dll` + `borlndmm.dll`), copied to host output **root** (next to the exe, via `<TargetPath>` + `CopyToOutputDirectory`) **only on Windows builds** (`Condition="$([MSBuild]::IsOSPlatform('Windows'))"`; Linux `.so` slot stubbed in the csproj). A `publish` folder is therefore self-contained (exe + managed + native DLLs in one folder). Project + WPF host forced to **x64** (`<PlatformTarget>x64</PlatformTarget>`, kept on the solution's Any CPU platform) — the engine is x64-only.
- **Cross-platform status:** all managed projects are `net8.0` and portable. `DNBridge.Wpf` is `net8.0-windows` (WPF) — Windows-only **by design**, a monitoring shell; the cross-platform production host is the planned `DNBridge.Service` (ASP.NET Core). No code-level blocker remains for a future Linux engine build.
- **Not yet:** `ICalcEngineRunner`/`InProcEngineRunner`, input providers, result decode, stage machine, voltage-control loop — the smoke test only proves the DLL loads and answers.

### NOT Started — Files Do Not Exist

| Component | Expected Location | Purpose |
|-----------|------------------|---------|
| DnbLogger | `Core/DnbLogger.cs` | Production file logging (a **temporary** quick file sink now lives inline in `DnbEngine` — see Known Issues note — to be replaced by this) |
| SBO implementation | `Scada/ScadaClient.cs` | Select Before Operate pattern (signatures ready, logic deferred) |

---

## Known Issues

1. **Grid Set Q setpoints not poked — RESOLVED (config).** Root cause was in DNC, not the
   bridge: DNC registers an element as *command-capable* by its **type** (`typ.command=1`),
   but only *sends* control to it when the per-element **`controlled`** flag is set
   (`item_dn.controlled`, `cl_104_Connector.cpp:343` builds `m_lstCtrl_Elems`). In
   `ct_2026.db3` every `item_dn.controlled` was `0`, so `m_lstCtrl_Elems` was empty and no
   `SetQ` poke (`cl_104_Connector.cpp:1456`) ever fired — even though the 10 setpoints
   (schema IDs 1304–1349) were registered fine. Setting `controlled=1` for the 10 FVE
   generators (dncors_ids 170,174,178,182,186,190,192,197,201,205) makes DNC poke them; the
   bridge forwards them to SCADA. Verified end-to-end in `ct_2026.DNB.log` (10× `Sent
   SetpointShort` IOA 901/903–910/1901). The temporary `DnbEngine` file logger + richer
   `CommandExecutor` poke/register Debug logging (added for this diagnosis) remain in place.
2. **Float setpoints sent as `C_SE_TC_1` (type 63).** `ScadaClient.SendSetpointShort` now
   sends `SetpointCommandShortWithCP56Time2a` with a `new CP56Time2a(DateTime.Now)` time tag
   (was `C_SE_NC_1`/type 50, untimestamped). Bool/4-state command paths unchanged.
   **COT forced to ACTIVATION:** the DNC poke carries `COT=SPONTANEOUS` (`FloatPokeValue.COT`
   default 3), which is invalid for a control-direction ASDU and was being silently dropped by
   the target SCADA. `SendSetpointShort` now overrides it to `CauseOfTransmission.ACTIVATION`
   (6) so the target SCADA (provider spec: IOA 901–910/1901 = type 63) accepts the command and
   replies ACT_CON/ACT_TERM. Monitoring outputs (types 30/31/36) keep COT=SPONTANEOUS.
3. **Main104 elements — TEMPORARY traffic-loop wiring DONE (via `XChng.cfg`).** For the
   SCADA ↔ DNBridge ↔ DNC+dll loop test, Main104 points are mapped from `XChng.cfg`
   (`DNBridge.Wpf/XChng.cfg`, copied to output). Direction is inferred from the id
   (`<100` = input, `≥100` = output). Implemented (all under `#region TEMPORARY` / `// TEMPORARY`):
   - **Loader split** (`CommandExecutor.LoadXChngCfgElements`): outputs → `CommandElements`
     (Poke target); inputs → `DncSession.Main104Inputs` (addr → flagged id) + cache.
   - **Input defaults + startup push** (`LoadXChngCfgElements` → `SendReversePokes`, called from
     `ExecuteRegisterElementsAsync` inside the load-once guard): an INPUT line may carry an
     optional **4th column = default value** (`<addr> <id> <type> <default>`; the mirror `M`
     flag occupies the same column, so the two are distinguished by content). Configured:
     currently in the file: `1→1, 2→2, 4→8.1 %, 5→-8.2 %, 6→1100.0 kVAr, 7→5500.0 kVAr` (the
     file's own header comment still documents the older `2→1, 4→8.0, 5→-8.0, 6→1000, 7→5000` —
     the live values look like deliberate test values; note `2→2` is `rm_MinLoss`, not
     `rm_MinTransfQ`, per `enum Reg_Mode_T{rm_BasicQ,rm_MinTransfQ,rm_MinLoss,rm_None}`).
     Modelled on DNC's own effective startup state (`ct_2026.d104.ini` over the `cl_DNCoRS_Data`
     ctor fallbacks, `DNCoRS_Data.cpp:26-37`). Values are in **wire units** (kVAr for Qvvn/Q_tor
     — **not** MVAr; see `Main104_calc_usage.md` §7). **On the migrated path `<scheme>.d104.ini`
     is dropped entirely** and this default column becomes the only startup source — see
     [`functional_blocks.md` §B10.1](../../DNC/Doc/App_migration/overview/functional_blocks.md)
     for the feasibility check (`RegBranch` id 3 has no source; the loader + mirror triggers must
     move off DNC's RegisterElements/State-poke). Behavior:
     - The default is **seeded into the cache as if SCADA had sent it** (Value/Quality/LastDataTime),
       so the mirrors echo it and the WPF tab shows it before SCADA's GI lands. The tab picks it up
       via `ElementInfo`'s value fields (see below), **not** via `ElementValueChanged` — that event is
       fed only by `ScadaClient.ElementsUpdated`, which by definition never sees a seeded default.
     - Seeding is guarded by `LastDataTime == DateTime.MinValue`: the cache is engine-scoped and
       outlives a DNC reconnect while the loader re-runs per session, so **live SCADA values are
       never clobbered** by a reconnect.
     - `LoadXChngCfgElements` returns the inputs that have a value (seeded default *or* live SCADA)
       and they are pushed to DNC as reverse-Pokes at registration, overwriting DNC's own
       ini/hardcoded defaults. **No DNC code change needed** — `cl_Poke_Command::Exec` →
       `SetParameter` applies them and persists them into `<scheme>.d104.ini`. Inputs with no
       default and no SCADA data are skipped (pushing `0.0` would zero DNC's params).
     - No race with DNC's `Reg_DoneOK` → `SendData_to_DRS`: that only *reads* DNC's fields and
       pokes them back out (we drop the echo — id not in `CommandElements`); it never overwrites.
     - Parsed with `CultureInfo.InvariantCulture` — the ambient cs-CZ locale wants a decimal
       comma and would reject `-8.0`.
     - id 3 (`RegBranch`) has no address in `XChng.cfg`, so it cannot be defaulted this way.
   - **Inbound decode** (`ScadaClient.ExtractValue`): SCADA issues inputs as commands —
     added `C_SC_NA_1`/`C_DC_NA_1`/`C_SE_NC_1`/`C_SE_TC_1` cases (COT=ACTIVATION falls through
     to the data loop and updates the cache).
   - **Reverse-Poke** (`CommandExecutor.SendReversePokes`, wired via `DnbEngine.OnScadaElementsUpdated`
     + `SetDncSender`/`DncClientHandler.SendAsync`): a SCADA update of an input address pushes an
     unsolicited `PokeCommand(id|flag, FloatPokeValue)` to DNC — the only channel that reaches
     `SetParameter` (GetData looks Main104 addresses up only in `item_104`, so it can't carry them).
   - **Host display of inputs** (`CommandExecutor.OnElementsRegistered` block): Main104 **inputs**
     (in `Main104Inputs`, not `CommandElements`) are now also emitted in
     `ElementsRegisteredEventArgs.Main104Elements` — one `ElementInfo` per input, resolved from the
     cache — so the WPF Main104 tab renders them via the same `Main104Catalog.Describe` +
     `ElementValueChanged` path as the outputs (name/direction/live value).
   - **`ElementInfo` carries the element's value** (`Value`/`Quality`/`UpdatedAt`, built by the local
     `Describe(id, Element104)` helper): the host **clears and rebuilds every row on each
     `ElementsRegistered`** (`MainWindow.xaml.cs:241-246`) and DNC fires RegisterElements several
     times (double-fire + ≤30 batches), so any value not carried on `ElementInfo` is blanked by the
     next batch. This is what made the seeded XChng.cfg input defaults show empty in the Main104 tab:
     they are written to the cache once (loader is once-per-session) and never re-sent, so the
     `ElementValueChanged` path — which only *updates* existing rows and is fed solely by
     `ScadaClient.ElementsUpdated` — never covered them. `UpdatedAt == DateTime.MinValue` means
     "no value yet" and renders as `—` (see `ValueTextOf`/`QualityTextOf`/`UpdatedTextOf`), so an
     element SCADA has not sent shows blank rather than a fake `0`. Applies to the Monitor tab too.
   - **Outbound by configured type** (`ExecutePoke`): outputs with `Iec104Type` 30/36 are sent as
     `M_SP_TB_1`/`M_ME_TF_1` (new `ScadaClient.SendSinglePointWithTime`/`SendMeasuredShortWithTime`,
     master-originated monitoring ASDUs via `SendASDU`) instead of the poke-kind default control type.
   - **Input mirrors** (`ExecutePoke` → `SendMain104Mirrors`): `M`-flagged `XChng.cfg` lines
     (`<out_addr> <src_id> <type> M`) register `DncSession.Main104Mirrors` (source input element →
     target output element + type). On the **State** poke (`0xC0000066`, one per calc cycle —
     OK or error path), each input's current cached value is re-sent to SCADA at its mirror OUTPUT
     address as a **time-tagged MONITORING ASDU** (COT=SPONTANEOUS, current CP56Time2a):
     30 `M_SP_TB_1` / 31 `M_DP_TB_1` / 36 `M_ME_TF_1`, via `SendSinglePointWithTime` /
     `SendDoublePointWithTime` (new) / `SendMeasuredShortWithTime`. (Previously sent as the control
     types SCADA issued them with — 45/46/63, COT=ACTIVATION, no timestamp.) DNC never sees these.
     Configured mirrors: src ids 1,2,4,5,6,7 → IOA 1203,1204,1207,1208,1205,1206.
     - **Load-once guard:** the `XChng.cfg` loader now runs **once per DNC session**
       (`DncSession.TryBeginXChngLoad`, race-safe via `Interlocked` against concurrent
       RegisterElements frames). Previously `LoadXChngCfgElements` ran on every RegisterElements
       (DNC double-fires + batches ≤30/frame) and appended to the `Main104Mirrors` **list**, so the
       list accumulated duplicates and each mirror was sent 2-3× per calc cycle. The loader also
       clears `Main104Mirrors`/`Main104Inputs` on entry (defense in depth); the guard resets in
       `DncSession.Clear()` so a reconnecting client reloads.
   - **Time fix (corrected):** cache timestamps are stored as **true UTC instants**. CP56Time2a
     carries no zone (`GetDateTime()` → Kind=Unspecified), so the SCADA's clock convention is now
     **configurable** via `[Scada] TimeIsUtc` (`DnbConfig.ScadaTimeIsUtc`, default **true**):
     `ScadaClient.Cp56ToUtc` tags the bytes as UTC when `TimeIsUtc=1`, else converts local→UTC.
     Outbound command time tags use the matching clock via `NowCp56()` (UtcNow vs Now). Untimed
     branches keep `DateTime.UtcNow`. This keeps timed and untimed values on one clock and satisfies
     the wire layer: `TlvWriter.AddDate` does `new DateTimeOffset(value, TimeSpan.Zero)`, which
     **throws for a Local-kind DateTime** and `GetData.NewerThan` round-trips as UTC. UI shows local
     via `.ToLocalTime()`.
     - **Field finding (2026-06-30):** the original code hard-coded local→UTC. The production SCADA
       (`cernovicke_terasy`) actually sends **UTC** in CP56Time2a, so every timed value landed ~2h
       early (`GetData` log showed `skew=-7160s`), failed the `LastDataTime > NewerThan` filter, and
       DNC reported "no new data" while the cache held fresh values. Dev worked because its simulator
       sent local time. Hence the toggle; this site needs `TimeIsUtc=1`, a local-time SCADA needs `0`.
       Diagnostics added: `CommandExecutor.ExecuteGetData` now logs NewerThan/newest/skew (Debug),
       and `DnbEngine.OpenLogFile` resolves a relative `Log_File` against the exe dir + logs the path.
     - **Verified end-to-end (2026-06-30, after fix):** `skew=+63.7s`, GetData returned 53 values,
       DNC computed and poked 10 setpoints back to SCADA (C_SE_TC_1, IOA 901–910/1901). Loop closed.
       Two follow-up log cleanups from that run: (a) a Poke for a Main104 **input** id (DNC echoes
       these outbound but they are reverse-Poke-only, never forwarded to SCADA) is now dropped at
       Debug instead of Warning (`ExecutePoke` checks `Main104Inputs.ContainsValue`); (b) duplicate
       `STARTDT_CON_RECEIVED` on the current connection is ignored (guard on `_startDtReceived`) so
       GI / the "connected" event aren't raised twice. NB: DNC still logs `Timeout … zadna data` for
       the setpoint points — expected, SCADA does not mirror setpoints back (DNC-side check only).

   **Input mirroring (DNBridge→SCADA echo of inputs) IMPLEMENTED** (see "Input mirrors" above):
   the 6 inputs are echoed back to SCADA at separate output addresses on each State poke so an
   operator can verify what DNBridge received. `main_104` in `ct_2026.db3` stays empty (0 rows) —
   DNC pokes outputs and accepts reverse-Pokes by hard-coded id, so the table is not needed for the
   test. Configured ids: inputs 1,2,4,5,6,7 (IOA 1103,1104,1107,1108,1105,1106); outputs
   102,103,104,105 (IOA 1000,1101,1100,1102); input mirrors src 1,2,4,5,6,7 (IOA 1203,1204,1207,1208,1205,1206).
   Post-migration this whole `#region`/`XChng.cfg` is removed; the real mapping comes from the
   consolidated **ini** — see [`DesiredState_Stage1.md`](DesiredState_Stage1.md) §3/§7.

   - **Active removed (2026-07-29):** Main104 id 1 (`Active`, IOA 1103 input / 1203 mirror)
     is commented out in `src/DNBridge.Wpf/XChng.cfg` (both the input and mirror data
     lines) and in `Commands/Main104Catalog.cs` (ids 1 and 101/`Active_ACK`), not deleted —
     uncomment to restore. Reason: DNCoRS (`../DNC`) already forces `m_bActive=true`
     unconditionally in three places (`DNCoRS_Data.cpp`, marked `// TEMPORARY`), so the
     calc always runs and always reports to SCADA regardless of what SCADA sends on IOA
     1103 — there is nothing left for this element to gate. DNC/DNCors itself was left
     unchanged. Remaining Main104 inputs/mirrors: RegMode, UNet_max, UNet_min, Qvvn, Q_tor
     (ids 2,4,5,6,7 / IOA 1104,1107,1108,1105,1106, mirrors 1204,1207,1208,1205,1206).
     Side effect: if DNC still echoes a Poke for id 1 (per the reverse-Poke behavior
     described above), `CommandExecutor.ExecutePoke` now logs it at **Warning** ("not
     found in command elements") instead of the previous **Debug** ("Main104 input —
     ignored"), since id 1 is no longer in `Main104Inputs`. Cosmetic only, not a
     functional regression.

## Connection Robustness (DNC + SCADA edge-case hardening)

Fixes for a WPF freeze observed when DNCalc died abruptly while both DNC and SCADA
were connected (STOP button unresponsive; recovered only when SCADA disconnected):

- **WPF UI throttling** (`MainWindow.xaml.cs`): log / DNC-traffic / SCADA-traffic
  streams enqueue to `ConcurrentQueue`s and are flushed by a single 250 ms
  `DispatcherTimer`. Log is a bounded rolling text buffer; traffic streams feed
  `TrafficLogView` (each self-bounds to 500 rows). Prevents dispatcher-queue
  flooding and unbounded growth from saturating the UI thread.
- **Off-thread shutdown** (`MainWindow.xaml.cs`): `StopButton_Click` / `Window_Closing`
  run `_engine.StopAsync()` via `Task.Run`, so lib60870 `Connection.Close()`
  (`workerThread.Join()`) never blocks the UI thread.
- **SCADA non-blocking close** (`ScadaClient.DisconnectAsync`): `CloseConnection()` is
  offloaded to `Task.Run` so the join never blocks the caller's thread.
- **SCADA cancellation propagation**: `CommandExecutor` takes an engine-lifetime
  `CancellationToken` (from `DnbEngine._cts`) and passes it to `ScadaClient.ConnectAsync`
  instead of `CancellationToken.None`, so `StopAsync`'s `_cts.Cancel()` also tears down
  the SCADA supervisor loop.
- **SCADA connectivity rewrite — single supervisor loop** (`ScadaClient`, 2026-06-19):
  replaced the dual "connection-event reconnect + health-monitor" drivers with one
  background `SupervisorLoop`. It is the only place a connection is created, torn down,
  or reconnected; lib60870 event handlers only update volatile flags and arm the backoff.
  Fixes a class of production failures seen as "SCADA unchecked while DNCalc connected"
  and "after STOP/START SCADA never reconnects or accepts no data":
  - **Orphaned worker threads.** lib60870 `Connection.Close()` is a no-op while the socket
    is still *connecting* (`running==false`), so the old code (which only called `Close()`
    then nulled the reference) leaked a live worker that kept SCADA's single connection
    slot — blocking the new connection from ever getting STARTDT_CON. `CloseConnection`
    now calls `Connection.Cancel()` (force-closes the socket, aborting a pending connect)
    before `Close()`, and bumps the attempt id so late callbacks are stale. As a final
    safety net, any `OPENED`/`STARTDT_CON_RECEIVED` from a superseded attempt cancels its
    own connection.
  - **Stuck "TCP up, STARTDT never confirmed".** The old health monitor only recovered
    from `IsConnected && !IsRunning`; the actual stuck state (`!IsConnected && IsRunning`)
    sat forever. The supervisor now bounds the establish phase with `ConnectStuckTimeoutMs`
    (15 s, > lib60870 T0 connect timeout) and force-reconnects.
  - **Missed CLOSED events.** Supervisor detects a dead worker (`IsConnected && !IsRunning`)
    and reconnects.
  - **Thread-safety.** `IsConnected`/`_connection`/`_startDtReceived` are now `volatile`;
    the attempt id is `Interlocked`. Connection-state reads are consistent across the
    supervisor, worker, and Connect/Disconnect threads.
  - **CA-mismatch diagnosis.** "ASDU received but no element matched cache" is now a
    rate-limited recurring warning (30 s) instead of one-shot.
  - **Idempotent `ConnectAsync`.** A duplicate RegisterElements (DNC sends it twice) no
    longer tears down a healthy connection — it is a no-op while the supervisor runs.
- **DNC single-client tracking** (`DncTcpServer.RunClientAsync`): the slot is released
  with `Interlocked.CompareExchange(ref _currentClient, null, handler)` so a dying
  handler can't evict a freshly-reconnected client.
- **STOP NullReferenceException** (`DncTcpServer.StopAsync`): the active client is now
  captured with `Interlocked.Exchange(ref _currentClient, null)` and disposed via the
  local, fixing a check-then-act race where `RunClientAsync`'s finally nulled the field
  between the null-check and the dereference. `DncClientHandler.DisposeAsync` is now
  idempotent (both paths may dispose the same handler). This NRE previously propagated
  out of `StopAsync`, leaving the engine half-stopped (Start button stranded, SCADA not
  disconnected, checkboxes never reset).
- **Resilient shutdown** (`DnbEngine.StopAsync`): DNC and SCADA teardown are wrapped in
  try/catch so a failure in one still runs the other and the final state-reset events
  (`IsRunningChanged`/`DncConnectionChanged`/`ScadaConnectionChanged`). `StopButton_Click`
  restores button state in a `finally`.

---

## Design Decisions (CommandExecutor)

- **Init** stores ServerName but does NOT connect to SCADA — just returns InitAnswer
- **RegisterElements** builds element maps, loads XChng.cfg command elements (TEMPORARY), THEN connects to SCADA — ensures all elements are available before Poke commands arrive
- **GetData** uses snapshot-based pagination (list + index) to avoid enumerator invalidation
- **Poke** has no answer (matches original C++ behavior), calls renamed Send methods: SendSetpointShort, SendSingleCommand, SendDoubleCommand
- IEC104 type constants from lib60870.CS101.TypeID enum (M_SP_NA_1, M_DP_NA_1, M_ME_NC_1)

## Design Decisions (ScadaClient)

- **Single supervisor loop** owns all connect/reconnect/teardown. `SupervisorLoop` runs
  every `SupervisorIntervalMs` (1 s) under `_lifecycleLock` and is the *only* place that
  creates or closes a connection. lib60870 callbacks (`OnConnectionEvent`,
  `OnAsduReceived`) only mutate volatile state and arm the backoff — they never reconnect
  and never block (no `Close()`/`Join()` from a callback → no deadlock).
- **Connection lifecycle:** lib60870 `Connection.ConnectAsync()` (non-blocking) is used.
  Handlers are per-attempt closures capturing the `Connection` + `attemptId`, so a stale
  callback can cancel its own connection. `CloseConnection` = bump attempt id → `Cancel()`
  (force-close socket, kills connecting orphans) → `Close()` → null reference.
- **Reconnect:** On CLOSED/CONNECT_FAILED, or supervisor-detected stuck/dead connection →
  rotate to next server → 5 s backoff (`ReconnectDelayMs`) → supervisor reconnects.
  Stopped by `DisconnectAsync()` (sets `_intentionalDisconnect`, cancels `_cts`).
- **Establish timeout:** `ConnectStuckTimeoutMs` (15 s) bounds the "connecting / waiting for
  STARTDT_CON" phase; exceeding it force-reconnects.
- **GI:** Sent automatically on STARTDT_CON_RECEIVED (CA=0xFFFF, QOI=20).
- **ASDU handler:** Updates ElementCache directly (shared references with DncSession).
  Unregistered IOAs silently skipped. Command confirmations (ACT_CON/ACT_TERM) logged.
  Stale ASDUs (wrong attempt id) dropped. "No element matched cache" warning rate-limited
  to 30 s.
- **Inbound control-frame diagnostic (`OnAsduReceived`):** every inbound control-direction
  frame (`C_SC_NA_1`/`C_DC_NA_1`/`C_SE_NC_1`/`C_SE_TC_1`) logs a `SCADA: Inbound control …
  IOA=… COT=… negative=…` Debug line *regardless of COT*, so a setpoint "confirmation" that
  arrives with a COT other than ACT_CON/ACT_TERM (which skips the confirmation branch and
  falls through to the monitoring path) is still visible in the Log box with its raw COT and
  P/N bit. The "ASDU matched" monitoring line now also includes `COT=…`.
- **Send commands:** Guard via `GetConnectedOrLog` (snapshots `_connection`, checks
  `_startDtReceived && _isConnected`). All wrapped in try/catch ConnectionException.
- **SBO:** `useSbo` parameter on all Send methods, defaults to false. Currently logs warning and falls through to direct execute. Will be implemented later.

---

## Project References

```
DNBridge.csproj → libs/lib60870/lib60870.csproj (source, not NuGet)
DNBridge.Wpf.csproj → src/DNBridge/DNBridge.csproj
```

---

## Key Specification Documents

- `../DNC/Doc/App_migration/overview/functional_blocks.md` — **★ top-level migration block map (B1–B31) + current decisions** (2026-07-27). Supersedes the dump-and-patch route and the external-PQ-split decision in the two files below. **Read first for any migration work.**
- `CLAUDE.md` (repo root) — general project description, purpose, tech stack, architecture, TLV/IEC104 protocol reference, conventions
- `docs/DesiredState_Stage1.md` — Stage-1 target design + migration notes (grilling session 2026-06-28); ⚠ partially superseded — see its banner
- `docs/architecture.md` — Stage-1 target architecture (online voltage control); ⚠ partially superseded — see its banner
- `docs/AI_GUIDE_SCADA_WRAPPER.md` — ScadaClient implementation guide
- `docs/Main104_Elements_Analysis.md` — Main104 regulation element analysis
- `docs/_a/`, `docs/_fa/` — analysis of the original C++ source (DNCalc / DNCors_IEC104)

---

## Update Instructions

**When to update this file:**
- After implementing a new class or component
- After changing component status (stub → partial → done)
- After fixing a known issue
- After adding new dependencies or project references

**How to update:**
1. Move items between DONE / PARTIAL / STUB / NOT STARTED sections
2. Update the Quick Status Overview table
3. Add or remove Known Issues
4. Keep descriptions short — one line per file, status + key facts only
