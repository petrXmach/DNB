# DNBridge — Desired State (Stage 1) + Migration Notes

> # ⚠ PARTIALLY SUPERSEDED — 2026-07-27
> A second grilling session revised several decisions here. The current top-level plan is
> [`../../DNC/Doc/App_migration/overview/functional_blocks.md`](../../DNC/Doc/App_migration/overview/functional_blocks.md)
> — read its §2 before acting on anything below.
>
> | Section here | Now |
> |---|---|
> | §13 **Phase C dump-and-patch** first | **Cancelled.** Straight to full native `.egc3` transcode (read-only, lossless model). |
> | §6 **external PQ split**, `cl_PQ_Split` not ported, single `=Mereni` injection point | **Reversed.** The engine estimator does not work → host-side `cl_PQ_Split` **is** ported (and it drives the engine N× per cycle). External split kept dormant behind `PQ_split=0/1` in `dnbridge.ini`. |
> | §1 **consolidated ini** = runtime source of truth; `.db3` never read at runtime, retired at Phase A | **Deferred.** The migrated path reads the **same per-scheme files DNC uses** — `.egc3`, `<scheme>.ini`, `.d104.ini`, `.db3`, `.bod2` from the schema folder. `dnbridge.ini` + `XChng.cfg` stay as-is for the existing DNC-connected path. Config consolidation happens **after** migration works. |
> | §7 **ini `[Calc]` stage list** | **Fixed chain**, no selector: `Qmin → Qmax → Loss → Optimize → Control`. |
> | §3/§7 regulation params from ini addresses | **Keep the current `XChng.cfg` mechanism on both paths.** `<scheme>.d104.ini` is **dropped entirely** (no read, no write-back): a parameter SCADA has not sent falls back to the `XChng.cfg` **default column**. |
> | §3 **"Missing ⇒ no regulation. No ini defaults"** (fail-safe) | **Overridden.** Configured defaults *do* drive the grid until SCADA sends a value, and a seeded default is indistinguishable from a fresh SCADA value to a freshness check (it is stamped `LastDataTime=UtcNow`). This is **parity with the C++**, which regulates from its ctor/`.d104.ini` defaults the same way — but it is a deliberate step away from the previously-chosen fail-safe. Note `Active` defaults to `1`. Feasibility check + the four implementation gaps: [`functional_blocks.md` §B10.1](../../DNC/Doc/App_migration/overview/functional_blocks.md). |
> | §11 DNC/TLV side kept as dormant reference | Still true — **and** the working DNC-connected version must stay **functional** during the migration. How the two coexist (shared projects vs. separate) is **open**: analysis + recommendation in `functional_blocks.md` §7. |
> | §4 `Verify()` redesign (`must_be_valid` + energisation) decided | **Re-opened / deferred** — faithful `Filter()`/`Check_Boundary()` port vs. the redesign, decided at implementation (block B11). |
>
> **Unchanged and still authoritative:** §2 cache, §5 loop shape (non-overlapping tick, snapshot,
> dedicated thread), §8 kernel-vs-orchestration layering, §9 host plan, §10 engine isolation,
> §12 logging deferred.

> **Status: AGREED DESIGN (grilling session, 2026-06-28).** This file is the consolidated
> output of a design interrogation: the *desired end-state* of the online voltage-control
> runtime, the *decisions* behind it (with rationale), and the *migration route* to get there.
>
> It is intentionally a **standalone** document — the `docs/` tree is currently unstructured
> (old-state analyses in `docs/_a` / `docs/_fa`, mixed md files, plus the companion `../DNC/Doc`).
> Documentation will be unified later; until then **this is the source of truth for the Stage-1
> target design**, refining [`architecture.md`](architecture.md) where they differ (noted inline).
> For *current* implementation status see [`DevState.md`](DevState.md).

---

## 0. One-paragraph summary

After migration, DNBridge is the **online voltage-control runtime** replacing the C++ `DNCors`
build **and** the `DNCors_IEC104` middleware. SCADA pushes all mapped values (GI + spontaneous)
into a live `ElementCache`. A 1-minute, non-overlapping calc loop verifies the data is complete
and fresh, snapshots the cache, runs the an3f4w engine through an ini-defined stage sequence
(`qmin`/`qmax`/`loss`/`optimize`/`control`), and sends the results back to SCADA: per-element
`Set-Q` setpoints (the actual regulation) plus Main104 telemetry and config echoes. There is **no
DNC and no TLV traffic** anymore. The calc engine and its input/decode logic live in a SCADA-free,
sequence-free **kernel** that a future offline Angular app will reuse; the loop/stages/SCADA glue
sit in an **online layer** on top of it.

---

## 1. Data sources — the config boundary (the central decision)

The single biggest simplification: collapse today's scattered config (`<scheme>_104_config_commands.txt`
GUI-import → db3, egc3-set flags, `XChng.cfg`, `.tec`, `bod2`, plus DNCors-only settings) into the
files below. Three roles, cleanly separated:

| File | Role | Lifetime |
|------|------|----------|
| **`<scheme>.egc3`** | The **network topology** — nodes, branches, transformers, PQ diagrams, load PQ. The calc DLL's actual input (as a UTF-16LE text dump). | **Stays** — the main data file; authored in the DNCalc GUI editor. |
| **ini (consolidated)** | DNBridge's **runtime config**: per-element address↔calc-id mapping, command routing (`typ_id` + multiplier), role flags DNBridge needs, regulation-point addresses, calc loop + stage settings, SCADA connection. **Hand-authored.** | **The runtime source of truth.** Schema/merge of several ini files into one = **deferred** (to be designed later). |
| **`<scheme>.db3`** | **Export scaffold only** — used by the DNCalc toolchain to *produce the Phase-C dump*. **Never read by DNBridge at runtime.** | Needed only at dump-export time in Phase C; **retired entirely at Phase A**. |

### Why db3 is *not* a runtime input (and the "sync" question, resolved)

The worry was: "in Phase C the dump is produced by DNCalc/DNCors which reads db3 — must db3 be kept
in sync with the ini?" **No** — and the reason is *when* each db3 field is consumed (verified against
`cl_104_Connector.cpp`):

- **`virt_pq`** (`item_dn`) is consumed at **calc-input time** — it shapes the engine input (`napětí TR:` /
  `odbočky TR:` lines via `cl_OperCalc`; an empty `m_lstVirtPQ_Elems` is *fatal*). So it must be correct
  in db3/egc3 **when you export the dump**, and is then *baked into that dump*.
- **`controlled`** (`item_dn`) is consumed only at **poke-back time** (`m_lstCtrl_Elems`, used at
  `cl_104_Connector.cpp:1379/1760`). It never touches the DLL input.

Consequences:
1. In Phase C, DNBridge runs on **dump + ini only**; it never opens db3.
2. The dump carries element **ids**, *not* SCADA 104 addresses and *not* `controlled` → the ini's
   mapping/routing has **nothing in the dump to match**, so there is **no bidirectional sync**.
3. Only the **dump-shaping** db3 fields (`virt_pq`, topology) must be right *at export time*; they are
   few and stable. Re-export the dump only when the topology/`virt_pq` set changes.
4. **Element-id discipline:** ini element-ids must live in the same id-space as the egc3/dump (both
   derive from the egc3). The user authors the ini by hand from the known data set; just keep ids aligned.

### Phase activation of fields (subtle but important)

A field can be *in the ini* but only *active in the phase that consumes it*:
- **`controlled`** → live immediately in **Phase C** (drives the CONTROL poke-back).
- **`virt_pq`** → **inert in Phase C** (the dump already baked it) → **authoritative in Phase A** (when
  DNBridge generates the dump itself).
- **`monitored`** → with external PQ split (see §6) the engine does the estimation, so host-side
  `monitored` (power-split precision) is **not used by DNBridge** → it can be omitted from the ini.
  *(Final ini field list deferred with the ini schema.)*

---

## 2. Cache

SCADA sends **all mapped values** (answer to General Interrogation + spontaneous updates) → stored in
the thread-safe `ElementCache` as `Element104` (value + timestamp + IEC104 quality + type), keyed by
104 address. This already exists; unchanged in the desired state. Regulation points (Main104) live in
the same cache.

---

## 3. Regulation parameters — SCADA-sourced, fail-safe

The Main104 **inputs 1–7** (`Active`, `RegMode`, `RegBranch`, `UNet_max`, `UNet_min`, `Qvvn`, `Q_tor`)
are the knobs that decide how each cycle calculates.

- **Source:** SCADA, read **live from cache each tick**. Their receive addresses are in the ini.
- **Rule:** SCADA **must return all of them in the GI** (SCADA-side guaranteed). 
- **Missing ⇒ no regulation** (fail-safe). **No ini defaults** — a stale/missing limit must never
  silently drive the grid. *(Rejected an earlier "ini fallback" idea for exactly this reason.)*

---

## 4. The pre-calc gate — `Verify()`

One **ordered, editable check list**, evaluated each tick **before any DLL run**. The gate passes only
when **all** hold:

1. SCADA connected (STARTDT up).
2. All Main104 inputs **1–7** present.
3. **`Active` = on**.
4. **Boundary switches open** (`boundary` elements; closed ⇒ calc electrically invalid).
5. Required measurements **fresh** and **good quality** — a single global **`MaxValueAge`** (per-element
   override is a *later* refinement; keep the freshness check as one pluggable item in the list).
   **Bad IEC104 quality or stale ⇒ treated as "missing."**
6. **Priority + energisation policy** (see decision box) — a bad point is fatal only when it is flagged
   `must_be_valid` **and** its element is actually **energised**.

**Gate failure ⇒ skip the *whole* cycle, poke nothing** (no setpoints from a partial/stale picture).
Log *which* check failed. *(Possible later refinement: keep telemetry-only stages running when
`Active`=off, for a monitoring readout — not now, to avoid a second send path.)*

> **Decision (2026-07-07) — priority-driven gate, `dvChod` now / topology-walk later.**
> `Verify()` does **not** port the C++ hard-coded `mapFilter` table or its per-type rules. Instead
> each 104 point carries a single operator-set boolean **`must_be_valid`** (the SCADA operator
> decides what may block regulation). The gate:
> 1. Collect invalid points whose element is `must_be_valid=true`. If none → pass (no engine call).
> 2. Force-close **all** invalid switches (topology only — must not leak into the real calc).
> 3. Run the **energisation check** — **`dvChod` initially** (the calc DLL is being fixed so the
>    in-filter load-flow no longer corrupts engine state), behind an `IsEnergised(element)`
>    interface so a DLL-free **topology-reachability walk** can replace it later.
> 4. Any `must_be_valid` invalid point on an **energised** element ⇒ fatal, skip cycle; on a
>    **de-energised** element ⇒ tolerated (graceful degradation preserved).
>
> This is equivalent to the original for U/P/Q, and *generalises* it: switches gain a priority gate,
> taps gain an energisation gate. Legacy `priorita`: `1,2 → must_be_valid=true`, `3 → false`,
> table-absent combos → `false`. Keep the command-point and POWER skips; for taps, "energised" means
> **either transformer terminal** reachable. Full analysis, algorithm, and edge cases:
> [`../DNC/Doc/Analysis_from_Evlivy3/ai/L2_dncors_input_filter.md`](../DNC/Doc/Analysis_from_Evlivy3/ai/L2_dncors_input_filter.md) §8.

---

## 5. The calc loop

- **Threading:** one dedicated **`LongRunning` calc thread**; a single engine instance ⇒ all DLL calls
  serialized for free (engine is not thread-safe). The blocking `anRunAnalysis` runs off the UI and
  SCADA threads.
- **Cadence:** **non-overlapping `await`-delay** — rearm *after* the cycle finishes (never a reentrant
  timer). **Interval = 1 minute, ini-configurable.** A slow run can never stack cycles or starve SCADA.
- **Manual trigger:** a **"Run cycle now"** button in WPF posts a **one-shot** request onto the same
  calc thread → serialized with the timed loop (queued/ignored if a cycle is running), and goes through
  the **same `Verify()` gate** (test the real path). *(Optional dev-only "force, ignore gate" toggle later.)*
- **Clock-only triggering** for now (no "new SCADA data → run early"); the freshness gate already
  guarantees current data. Event-triggering can be added later.

### One cycle
```
each tick (calc thread):
  1. Verify() gate (§4)            fail → log, rearm, poke nothing
  2. snapshot = cache snapshot     one frozen input for all stages
     reg      = Main104 1–7 live   (already in cache)
  3. for each ENABLED stage (ini [Calc] order):
        text   = inputProvider.BuildInput(snapshot, reg, stage)   patch dump (Phase C) / generate (Phase A)
        raw    = runner.Run(text)                                  reset + ReadInpData + RunAnalysis + CheckFinish (blocks)
        result = decoder.Decode(raw)                               AN_*_4_T by "<id>"; + anGetSystemLosses1f for LOSS
        accumulate setpoints / Qmin / Qmax / Losses / State
  4. CONTROL (no DLL): send results back to SCADA (§7)
  5. raise events (cycle summary, pokes) → hosts render
  6. await Task.Delay(interval, ct); repeat
```

---

## 6. Feeding the DLL — external PQ split, single injection point

The engine is text-in / struct-out and runs a **state estimation**; it does not take a raw measurement
list. Decision:

- **External (engine-side) PQ split** — feed the engine the raw measurements and let its built-in **WLS
  estimator** do the split. The host `cl_PQ_Split` (~600 lines) is **NOT ported**.
- **Single injection point:** live measured values are patched into exactly one place — the **`=Mereni`
  value column, keyed by element id** — while the static load-`PQ` rows in the dump never change. This
  makes the cache→DLL feed a trivial keyed value-overwrite.
- **Open item (user to confirm):** the exact `=<PQ_Split_Section>` token name for the external-split
  input, to be verified against a known-good external-split dump from DNCalc. Reference:
  `../DNC/Doc/Analysis_from_Evlivy3/ai/L2_dnbridge_full_transcode.md` §9.

---

## 7. Outputs — three families, sent each completed cycle

All addresses come from the **ini**. The whole send bundle is **gated** (skipped if the cycle is skipped).

1. **Main104 input echo (1–7).** Each input has **two** SCADA addresses — a *receive* address (param in)
   **and** a separate *send/"set"* address. DNBridge echoes the received value back **verbatim** (mirror;
   nothing actually actuated), bundled with the completed cycle.
2. **Main104 telemetry outputs (101–106):** `Active_ACK`, `State`, `Q_min`, `Q_max`, `Losses`, `Weak` —
   computed by the `qmin`/`qmax`/`loss` (and status) stages. **`101 Active_ACK` = trivial copy of
   `Active` (1) for now** (so 101 and input-1's own echo carry the same value to two addresses — fine).
3. **Per-element control setpoints (the actual voltage control):** the `OPTIMIZE` stage (ORPF, kind 21)
   produces a `Set-Q`/`Set-U`/tap value **per `controlled` element**, dispatched by command **`typ_id`**
   (8 = Set U, 9 = Set Q, 10 = tap, 14 = Set Uopt → `C_SE_TC_1` etc.), **scaled by multiplier**, to the
   element's **command address**. For `ct_2026` this is the **10 FVE `Set-Q`** points.

**Stage enablement is incremental** (ini `[Calc]`, file order = run order): bring up telemetry-only first
(`qmin`/`qmax`/`loss` → Main104 pokes, **no grid actuation**), then enable `optimize`+`control` to
dispatch the per-element `Set-Q` setpoints once telemetry is trusted. Same `anRunAnalysis(kind)` entry
for every stage; `kind` from the dump header (5 = oper/dvChod, 21 = qmin/qmax/loss/optim).

---

## 8. Code structure — **share the kernel, not the orchestration**

> This **refines `architecture.md` §3**, which says the offline Angular app reuses `DNBridge.Calc`
> "unchanged." That is misleading: the offline app uses **no** stage sequence, control, loop, or SCADA
> (online and offline were historically separate builds with different flags). The reusable unit is
> **below** the loop, not around it.

`DNBridge.Calc` is internally **layered**, with a strict **one-way (inward) dependency**:

- **KERNEL — shared, SCADA-free *and* sequence-free.** `ICalcEngineRunner` / `InProcEngineRunner`
  (P/Invoke + init/done lifecycle), `ResultDecoder` (`AN_*_4_T` struct offsets, by id), and the **input
  builder** (Phase-C dump-patcher → Phase-A egc3 transcoder) + model. This is the expensive,
  correctness-critical, hard-to-rewrite asset. **Both** online DNBridge and the future offline Angular
  app reuse *exactly this* and must never each grow their own copy.
- **ONLINE LAYER — online-only, NOT shared.** `VoltageControlLoop`, the `qmin/qmax/loss/optim/control`
  `StageMachine`, the `Verify()` gate, and the `IElementValueSource` / `ISetpointSink` SCADA adapters.

Notes:
- `IElementValueSource` / `ISetpointSink` are the **online layer's testability seam** — **not** the
  Stage-2 reuse boundary.
- **One `DNBridge.Calc` assembly for now**; keep the kernel in its own folder/namespace with **zero deps
  on stages / SCADA / control**. Splitting it into a separate assembly for the Angular backend later is
  then near-mechanical. *The one expensive-to-undo mistake to avoid: letting the transcoder/decoder get
  tangled into the stage machine or SCADA* — avoided for free by not adding those usings.

---

## 9. Host / deployment

- **WPF now** (`DNBridge.Wpf`): bring-up host — START/STOP, live element grids, traffic monitor, and the
  manual "Run cycle now" button.
- **Headless `DNBridge.Service` later** (ASP.NET Core Generic Host, calc loop as a `BackgroundService`):
  the **production** target. **SCADA lives inside this service** — the slave allows a single connection,
  so a desktop app that's closed (or a web tier that recycles) would drop/fight over the slot (the
  orphaned-slot failure class already fought in `ScadaClient`).
- **Same host-agnostic core** (`DnbEngine` owns loop + `ElementCache` + `ScadaClient`) in both → moving
  WPF → Service is **zero core change**.
- **REST/SignalR** surface: **deferred** until a second client (Angular) actually exists.

---

## 10. Engine isolation

- **In-process now** (`InProcEngineRunner`, simplest).
- **Out-of-process `DNBridge.EngineHost` later** — production hardening: the closed Delphi DLL has
  faulted before; a child process means a native AV kills only the child (respawn, log a missed cycle)
  instead of taking SCADA down. Behind `ICalcEngineRunner` either way → a swap, loop unchanged.

---

## 11. DNC / TLV side — kept permanently

The DNC-facing TLV server (`DncServer/`, `Commands/`, GetData pagination, `XChng.cfg` loader) has **no
client** once DNC is removed. Decision: **keep it in the repo permanently as previous-state reference —
do not delete, even in the future.** It is simply **not started** in the Stage-1 online runtime.
*(This overrides any earlier "delete once Phase A is stable" note.)*

---

## 12. Logging / traffic dump — deferred

`[Scada] Scada_log` now writes a per-day SCADA traffic log (`Scada/ScadaFileLogger.cs`, same format as the WPF
SCADA Traffic detail pane) — this predates the migration and is unrelated to it.
Broader post-migration logging is still **deferred**: once DNC/DNCors are gone there is **no TLV middleware traffic**
to capture, so whatever logging is built later (SCADA-side ASDUs + cycle summaries, and optionally a
replayable per-cycle engine-input dump) is simpler. Out of scope for now.

---

## 13. Migration route

Two phases that differ **only** in the engine-input producer (`IEngineInputProvider`); everything
downstream — run the engine, decode by id, gate, route, poke — is shared and built once.

- **Phase C — dump-and-patch (first; start testing ASAP).** One fixed schema. The input text is exported
  **once** from DNCalc (this is the only thing db3 is needed for), then **patched per cycle** with live
  cache values at the `=Mereni` injection point. Proves the whole loop end-to-end with minimal new code.
  - First milestone: **static replay, zero patching** — feed the unmodified DNCalc dump, run, decode by
    id, compare to DNCalc. Proves runner + decode before any live patching.
- **Phase A — native generate (after C is proven).** DNBridge parses `.egc3` (TLV+bzip2) + the ini and
  **generates** the input text natively. **db3 retired.** `virt_pq` and the other dump-shaping fields now
  come from the ini (or egc3).

---

## 14. Open items / risks

- **ini schema & merge** — the consolidated ini format (and merging several ini files into one) is
  **to be designed**. Must represent the per-element M:N 104 mapping, command routing (`typ_id` +
  multiplier), the **dual address** (receive + send) for Main104 inputs 1–7, and the regulation/loop/stage
  settings.
- **`=<PQ_Split_Section>` token** — confirm exact name for external split (§6).
- **`anGetSystemLosses1f` on kind 21 (dvOrpf)** returned `0+j0` in testing though per-branch
  `m_DeltaSabc` carries losses — the `LOSS` telemetry getter on the ORPF kind is still TBD
  (see [`DevState.md`] / the an3f4w notes).
- **Boundary-open semantics** — confirm which elements are `boundary` and their normal operating state
  in `ct_2026`.
- ~~**`Verify()` fatal-vs-tolerable policy**~~ — **decided 2026-07-07** (§4 decision box): drop the
  hard-coded `mapFilter`, use a per-point operator flag `must_be_valid` + energisation check
  (`dvChod` now, topology-walk later). See
  [`../DNC/Doc/Analysis_from_Evlivy3/ai/L2_dncors_input_filter.md`](../DNC/Doc/Analysis_from_Evlivy3/ai/L2_dncors_input_filter.md) §8.

---

## 15. Decision log (quick reference)

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | egc3 stays as topology / DLL input | It's the electrical model; can't live in an ini |
| 2 | ini = hand-authored runtime config; schema/merge deferred | Fast dev iteration; consolidates the scattered config |
| 3 | db3 = export scaffold only, no runtime read, no sync | Dump carries ids not addresses; only `virt_pq`/topology matter at export |
| 4 | Regulation params SCADA-sourced, missing ⇒ halt, no defaults | Fail-safe; operator-driven |
| 5 | `Verify()` gate; fail ⇒ skip whole cycle, poke nothing | Never actuate on partial/stale data |
| 6 | 1-min non-overlapping loop, ini-configurable + manual WPF trigger | Predictable actuation; testability |
| 7 | External PQ split; single `=Mereni`-by-id injection | Avoids porting `cl_PQ_Split`; trivial feed |
| 8 | Three output families incl. 1–7 echo (dual addr); 101 = copy of Active | Matches SCADA contract; minimal now |
| 9 | Share the **kernel**, not the orchestration | Offline reuses engine/decode/transcode, not the online loop |
| 10 | WPF now, headless Service later, same core | Fast bring-up; Service owns the single SCADA slot in production |
| 11 | Engine in-proc now, out-of-proc later | Simplicity now; crash isolation in production |
| 12 | Keep DNC/TLV side permanently as reference | Previous-state reference; never delete |
| 13 | Logging deferred | Simpler post-migration (no TLV traffic) |
| 14 | `Verify()` gate = per-point `must_be_valid` flag + energisation check (drop hard-coded `mapFilter`) | Operator, not source, decides what blocks regulation; equivalent to original for U/P/Q, generalises switches/taps |
| 15 | Energisation check via `dvChod` now, `IsEnergised()` interface for topology-walk later | Calc DLL being fixed so in-filter load-flow is safe; walk is a DLL-free follow-up, not a blocker |
