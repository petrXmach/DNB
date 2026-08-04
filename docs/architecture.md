# DNBridge — Target Architecture (Stage 1: online voltage control)

> # ⚠ PARTIALLY SUPERSEDED — 2026-07-27
> Current top-level plan: [`../../DNC/Doc/App_migration/overview/functional_blocks.md`](../../DNC/Doc/App_migration/overview/functional_blocks.md) (§2 = decision deltas).
> Affected here: **§2 "Phase C then Phase A" — Phase C (dump-and-patch) is cancelled**, the route
> is native `.egc3` transcode only, and the **external PQ split is dropped** (the engine estimator
> does not work → host-side `cl_PQ_Split` is ported and runs the engine N× per cycle);
> **§7 ini-driven stage sequence** → fixed chain `Qmin → Qmax → Loss → Optimize → Control`;
> **§9 config** → the migrated path reuses DNC's per-scheme files (`.egc3`/`.ini`/`.d104.ini`/`.db3`/`.bod2`),
> `.db3` is *not* retired, consolidation deferred. §3–§6 and §10–§11 stand, with
> `IEngineInputProvider` now having exactly one implementation (the transcoder) and the loop
> gaining a **PQ-split stage** ahead of the calc stages.

> **Status: DESIGN / PLANNED.** This document captures the agreed Stage-1 architecture for
> turning DNBridge into the online voltage-control runtime that replaces the C++ `DNCors`
> build (EVlivy3 `_VOLTAGE_CTRL_` → `DNCoRS.exe`) **and** the `DNCors_IEC104` middleware.
> It is the destination, not the current state. For what is actually implemented today, see
> [DevState.md](DevState.md). For the C++ being ported and the step-by-step port plan, see
> the **DNC** companion repo (`../DNC/Doc/`), which remains the living migration reference.
>
> **⚠ Refined by [`DesiredState_Stage1.md`](DesiredState_Stage1.md) (grilling session 2026-06-28).**
> That doc is the current source of truth for the Stage-1 target and corrects three points below:
> (a) the offline Angular app does **not** reuse all of `DNBridge.Calc` "unchanged" — it reuses only
> the **kernel** (engine/decode/transcode), not the loop/stages/SCADA (§3 here); (b) `.db3` is an
> **export scaffold**, not a DNBridge *runtime* registration source — the **ini** is the runtime config
> (§9 here); (c) the DNC/TLV side is **kept permanently** as reference, not deleted after Phase A (§9 here).

---

## 1. Staging

| Stage | What | When |
|-------|------|------|
| **1 — online voltage control** | DNBridge connects to SCADA, runs the periodic calc loop, pokes setpoints back. Replaces the C++ `DNCors` runtime + `DNCors_IEC104`. | **now** |
| **2 — offline editor/calc** | New Angular app ("DNCalc"): drawing, element editing, on-demand calculation from fixed values (different DLL entry points). Reuses `DNBridge.Calc`, no SCADA. | far off — design pressure only |

The C++ **GUI editor (DNCalc/EVlivy3) stays** as the authoring tool: it produces the fixed
`.egc3` schema and exports the DLL-input dump used in Phase C (below). We do not modify or
re-port the editor in Stage 1.

## 2. Migration route — Phase C then Phase A

The calculation DLL (`wxbase_supp64.dll` / an3f4w, x64 Delphi) is a **text-in / struct-out**
engine: `anReadInpData(UTF-16LE)` → `anRunAnalysis(kind)` → read back node/branch structs.
The only thing that changes between the two phases is **how the engine-input text is produced**:

- **Phase C — dump-and-patch (first, to start testing ASAP).** One fixed schema; the input
  text is exported once from DNCalc, then **patched per cycle** with live values. Proves the
  whole loop (run DLL → decode → route → poke SCADA) end-to-end with minimal new code.
- **Phase A — native parse (after C is proven).** DNBridge parses `.egc3` (TLV+bzip2) + `.db3`
  (SQLite) into a C# model and **generates** the input text natively — no C++ in the loop.

C and A differ **only** in the input-text producer (`IEngineInputProvider`). Everything
downstream — run the engine, decode `AN_*_4_T` by `"<id>"`, route via `.db3`, poke SCADA,
the stage machine — is shared and built once.

**Phase-C decisions:**
- **External PQ split** (engine WLS estimator), *not* the host `cl_PQ_Split`. Live measured
  values enter through a single place — the `=Mereni` value column, keyed by element id —
  so the load `PQ` rows stay static and the ~600-line split algorithm is never ported.
- **First milestone = static replay, zero patching:** feed the unmodified DNCalc dump,
  run `anRunAnalysis(21)`, decode by `"<id>"`, compare to DNCalc. Proves the runner + decode
  before any live patching.
- Open item to confirm against a known-good external-split dump: the exact `=<PQ_Split_Section>`
  token name (see `../DNC/Doc/Analysis_from_Evlivy3/ai/L2_dnbridge_full_transcode.md` §9).

## 3. Solution composition

```
DNBridge            core lib (host-agnostic)          — exists
  ├─ Elements/      ElementCache, Element104           SCADA value store, keyed by 104 address
  ├─ Scada/         ScadaClient (lib60870)             owns the SCADA socket + supervisor
  ├─ Core/          DnbEngine, Config                  composition root / orchestrator
  └─ DncServer/, Commands/                             DORMANT in Stage 1 (kept, not deleted)

DNBridge.Calc       NEW lib (host-agnostic)            — no lib60870, no TLV
  ├─ ICalcEngineRunner    → InProcEngineRunner         P/Invoke wxbase_supp64.dll   [→ OutOfProc later]
  ├─ IEngineInputProvider → DumpPatchInputProvider     phase C                       [→ Transcoder phase A]
  ├─ ResultDecoder                                     AN_*_4_T decode by "<id>"
  ├─ Db3Map                                            .db3: addr↔id, role lists, command typ_id/multiplier
  ├─ StageMachine                                      walks the ini-enabled stage sequence
  ├─ VoltageControlLoop                                the await-delay tick + cycle orchestration
  └─ IElementValueSource / ISetpointSink               the two seams to the SCADA world

DNBridge.Wpf        host — testing/monitoring shell    — exists
DNBridge.Service    host — ASP.NET Core (NEW)          BackgroundService loop + REST/SignalR for Angular
DNBridge.EngineHost child process (NEW, Phase B)       out-of-process DLL isolation
```

**Share the kernel, not the orchestration** (refined — see [`DesiredState_Stage1.md`](DesiredState_Stage1.md) §8).
`DNBridge.Calc` is internally **layered** with a one-way (inward) dependency:
- **Kernel** — SCADA-free *and* sequence-free: `ICalcEngineRunner`/`InProcEngineRunner`, `ResultDecoder`,
  the input builder (`IEngineInputProvider`) + model. **This is the only part the offline Angular app
  reuses** (it supplies fixed values; it uses **no** stage sequence, control, loop, or SCADA).
- **Online layer** — online-only, *not* reused offline: `VoltageControlLoop`, `StageMachine`, the
  `Verify()` gate, and the `IElementValueSource`/`ISetpointSink` SCADA adapters.

`IElementValueSource`/`ISetpointSink` (104 addresses + small `ElementSample`/`CommandType` types) are
therefore the **online layer's testability seam**, *not* the Stage-2 reuse boundary. One assembly for
now; keep the kernel in its own folder/namespace with zero deps on stages/SCADA so it can be split out
for the Angular backend later.

## 4. Ownership & wiring

```
Host (WPF  OR  Service)
  └─ owns DnbEngine  (via IDnbEngine + events)         single composition root
        ├─ owns ElementCache             shared value store (ConcurrentDictionary)
        ├─ owns ScadaClient              writes cache on ASDU; sends pokes
        ├─ owns Db3Map                   loaded at start → registration + roles + routing
        └─ owns VoltageControlLoop, given:
                 ICalcEngineRunner   = InProcEngineRunner
                 IEngineInputProvider= DumpPatchInputProvider
                 ResultDecoder, StageMachine, Db3Map
                 IElementValueSource = adapter over ElementCache    (supplied by DnbEngine)
                 ISetpointSink       = adapter over ScadaClient       (supplied by DnbEngine)
```

`DnbEngine` builds the loop, hands it two thin adapters (cache-read, scada-send), and
starts/stops it with the same lifetime `CancellationToken` already used for SCADA.

## 5. Threads

| Thread | Owner | Does |
|--------|-------|------|
| lib60870 worker(s) | ScadaClient | receive ASDUs → **write** ElementCache |
| SCADA supervisor (1 s) | ScadaClient | connect/reconnect/rotate IPs only |
| **Calc loop (dedicated, LongRunning)** | VoltageControlLoop | the cycle; the **blocking** engine call lives here |
| DNC accept/recv | DncTcpServer | dormant (not started in Stage 1) |
| UI dispatcher / ASP.NET request threads | Host | **only consume events** — never touch the engine |

One calc thread + one engine ⇒ all DLL calls are serialized for free (the engine is not
thread-safe). The blocking `anRunAnalysis` runs on a thread that is neither the UI nor SCADA,
so it can never freeze the socket or the window.

## 6. One cycle (the tick)

START opens SCADA and begins the tick loop; STOP ends both. The loop runs continuously with
a **non-overlapping `await`-delay** (rearm *after* the cycle finishes, never a reentrant timer).

```
each tick (own thread):
  1. SCADA connected?            no  → do nothing, rearm
  2. Verify()                    editable pipeline of checks: boundary switches open,
                                 value availability, validity/quality, freshness.
                                 fail → log, do nothing, rearm
  3. snapshot = cache snapshot   ONE snapshot per tick; all stages use this frozen input
     reg      = Main104 points   regulation params (Unet, RegMode, Qvvn, limits) read LIVE from SCADA
  4. for each ENABLED stage (ini order):
        text   = inputProvider.BuildInput(snapshot, reg, stage)   patch dumped txt (phase C)
        raw    = runner.Run(text)                                  reset+ReadInpData+RunAnalysis+CheckFinish (blocks)
        result = decoder.Decode(raw)                               by "<id>"; + anGetSystemLosses1f for LOSS
        accumulate setpoints / Qmin / Qmax / Losses
  5. CONTROL: controlled elements → Db3Map id→(104 cmd addr, typ_id, multiplier) → setpointSink.Send(...)
              + poke Main104 outputs (Qmin/Qmax/Losses/State)
  6. raise events (cycle summary, pokes, track-log) → hosts render
  7. await Task.Delay(interval, ct); repeat
```

**`Verify()` is the single editable hook** — a list of independent check functions, so checks
can be added/changed over time without touching the loop. Cache read (loop) and cache write
(SCADA) are concurrent but safe (`ConcurrentDictionary`).

## 7. Stage sequence — ini-driven

The stage sequence is **the ini file**, not a hardcoded "kind". A `[Calc]` section lists the
steps in execution order; each is enabled (`1`) or disabled (`0` / `;`-`#` comment). The loop
walks the enabled steps top-to-bottom (file order = run order).

```ini
[Calc]
; enable/disable each step; comment out to skip
dvChod   = 0     ; plain load flow (kind 5)
Qmin     = 1     ; telemetry: -Σ secondary-winding Q over supply TRs → Main104
Qmax     = 1     ; telemetry
LOSS     = 1     ; telemetry: anGetSystemLosses1f → Main104
OPTIMIZE = 1     ; ORPF (kind 21) → produces U/Q/tap setpoints
CONTROL  = 1     ; send results back to SCADA (no DLL)
```

`Qmin`/`Qmax`/`LOSS` are **telemetry only** (Main104 summary points); `OPTIMIZE` produces the
setpoints `CONTROL` dispatches. First bring-up can run just `OPTIMIZE`+`CONTROL`.

## 8. Lifecycle

- **Start:** `Config.Load` → load `.db3` into `Db3Map` + register points/roles →
  `ScadaClient.ConnectAsync` (GI fills cache) → start `VoltageControlLoop`. The tick's
  step-1/step-2 guards keep it idle until SCADA is up and the post-GI cache is fresh.
- **Stop:** one `_cts.Cancel()` → loop falls out of `Task.Delay`, SCADA supervisor stops,
  `InProcEngineRunner.Dispose` calls `anDoneLibrary` + `FreeLibrary`. Resilient try/catch
  teardown (already in place for SCADA/DNC).

## 9. DNC/TLV side & config in Stage 1

- The DNC-facing TLV server (`DncServer/`, `Commands/`, GetData pagination) has **no client**
  once DNC is removed → it is **not started** in the Stage-1 service. **Kept in the repo
  permanently as previous-state reference — not deleted, even in the future** (refined; the earlier
  "delete once Phase A is stable" no longer applies).
- **The consolidated ini replaces `RegisterElements`** as DNBridge's runtime registration + routing
  source: address↔calc-id, the role flags DNBridge needs (`controlled`, `boundary`; `virt_pq` for
  Phase-A dump-gen; `monitored` dropped — external split), and command routing (`typ_id` 8/9/10/14 +
  multiplier). `XChng.cfg` is retired. **`.db3` is *not* a DNBridge runtime input** — it is only an
  *export scaffold* used by the DNCalc toolchain to produce the Phase-C dump (its `virt_pq`/topology
  shape that dump), and is retired entirely at Phase A. `ElementCache` is unchanged — only its
  *population source* moves from DNC → SCADA GI, with the **ini** as the mapping. See
  [`DesiredState_Stage1.md`](DesiredState_Stage1.md) §1.
- Config consolidation (the ini→db3 cleanup) is a **separate later milestone** — Stage 1 reads
  the current `.db3` as-is. Regulation/limits/calc-kind arrive as **Main104 points over SCADA**,
  not from a file.

## 10. Deployment shape

- **Production = a standalone long-running service** (`DNBridge.Service`, ASP.NET Core Generic
  Host). The voltage-control loop runs as an `IHostedService`/`BackgroundService`; **lib60870 /
  the SCADA connection lives inside this service**, never in a web server. The same process
  exposes a thin **REST + SignalR** surface for clients.
- **Why the service owns SCADA:** the slave permits one connection; a web tier that
  scales/recycles would fight over the slot (the orphaned-slot failure class already fought in
  `ScadaClient`), and grid control must survive web deploys/restarts.
- **Angular FE / WPF are pure clients** of that API. WPF stays the test/monitor shell (START/STOP,
  logs, traffic, live grids).
- **Stage 2 offline** is a *separate* SCADA-free web backend that references `DNBridge.Calc`
  in-process — the only place "web API hosts the calc" is appropriate.

## 11. Engine isolation evolution (Phase B)

The DLL is in-process now (`InProcEngineRunner`) behind `ICalcEngineRunner`. Because it is
closed-source Delphi that has already been seen to fault, production should move to an
**out-of-process** `DNBridge.EngineHost` child (`OutOfProcEngineRunner` over a local pipe):
a native AV then kills only the child (respawned; loop logs a missed cycle) instead of taking
SCADA down. The loop is unchanged — `Run()` just blocks on IPC instead of P/Invoke.

## 12. Source / reference map

| Topic | Where |
|-------|-------|
| Full egc3→engine native-port plan (Phase A) | `../DNC/Doc/Analysis_from_Evlivy3/ai/L2_dnbridge_full_transcode.md` |
| Voltage-control loop in C++ (`Perform_Calculation`) | `../DNC/Doc/Analysis_from_Evlivy3/ai/L2_voltage_ctrl_calc_loop.md` |
| DLL lifecycle / P/Invoke / encodings | `../DNC/Doc/Analysis_from_Evlivy3/ai/L2_dll_lifecycle_pinvoke.md` |
| ORPF stage loop, stage→tail, setpoint readback | `../DNC/Doc/Analysis_from_Evlivy3/ai/L2_dll_orpf_scada.md` |
| Result struct decode (`AN_*_4_T`) | `../DNC/Doc/Analysis_from_Evlivy3/ai/L2_dll_result_structs.md` |
| `.db3` address↔id mapping + command dispatch | `../DNC/Doc/Analysis_from_Evlivy3/ai/L2_db104_mapping.md`, `L2_command_setpoints.md` |
| DLL call-sequence reproduction (kinds, args) | `../DNC/Doc/dll/dll_call_sequence_orpf_repro.md` |
| DLL function surface (~35 bound of ~250) | `../DNC/Doc/an3f4w_function_usage_overview.md` |
| Current implementation status | [DevState.md](DevState.md) |
