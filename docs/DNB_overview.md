# DNB — system overview and task assignment

> **What this is.** The top-level picture of the new **DNB** system: what it is made of, how the
> parts connect, what depends on what, and what is still open. It is a **task-assignment map**, not
> an implementation guide — each block is detailed separately later.
>
> **Companion documents.** *How to migrate* (the C++ original, decisions `D1`–`D27`, block map
> `B1`–`B33`) lives in the DNC repo:
> [`../../DNC/Doc/App_migration/AI/overview/migration_plan.md`](../../DNC/Doc/App_migration/AI/overview/migration_plan.md).
> This file references its `D`/`B` identifiers and never re-states them.
>
> ⚠ **The rest of `DNB/docs/` is inherited from the DNBridge fork and is largely stale for DNB**
> (`architecture.md`, `DesiredState_Stage1.md`, `DevState.md` describe the DNC-connected bridge).
> Cleaning that up is a task in §8.

---

## 1. What DNB is

DNB is **one application** replacing two: the C++ **DNCalc/DNCoRS** (grid model, editor, calc
engine driver) and the C# **DNBridge** (SCADA middleware). It talks **directly** to SCADA over
IEC 60870-5-104 and **directly** to the an3f4w calculation DLL. There is no DNC, no TLV, no TCP
middleware, no `.egc3`, no SQLite.

```
                        ┌───────────────────────── DNB ─────────────────────────┐
   ┌───────┐  IEC104    │  SCADA buffer  ⇄  time slice  ⇄  calc cache  ⇄  DLL    │
   │ SCADA │ ◄────────► │   (104 addr)      (per tick)    (element id)  an3f4w   │
   └───────┘            │        ▲                              ▲               │
                        │        │                     schema (JSON, immutable)  │
                        └────────┼──────────────────────────────┼───────────────┘
                                 │                              │
                          WPF shell / service            Angular editor (later)
```

Two operating modes, sharing one calculation kernel:

| | **Online** (first target) | **Offline** (later) |
|---|---|---|
| Drives | the live grid — 1-minute control loop, SCADA in/out | on-demand calcs, no SCADA |
| Scheme | **one**, loaded at start | **many**, per user |
| Users | none (unattended) | multi-user, login, upload/edit/save |
| UI | WPF shell now → headless service | Angular editor |

---

## 2. Decisions settled in this session

These **amend** `migration_plan.md`; where they differ, this file wins until the plan is updated.

| # | Decision | Consequence |
|---|----------|-------------|
| **N1** | **Two stores, not one.** A continuously-filled **SCADA buffer** (104-address-keyed, both directions) and a per-tick **calc cache** (element-id-keyed) built from a frozen time slice of it. Confirms `D12`. | §3. The schema document stays immutable; live values never land on it. |
| **N2** | **No v0 format.** The DNC JSON exporter is updated to emit the **final** C# data structures directly. No intermediate format, no DTO layer, no mapper. | ⚠ **Inverts a dependency** — see §5.1. Kills `D4`'s "wire-faithful temporary payload" and most of `B1`. |
| **N3** | **Host-side `cl_PQ_Split` is ported** (`D19` confirmed). | The split stage drives the engine N× per cycle → the engine runner must be callable **from inside a stage** from day one. |
| **N4** | **Two files per scheme** — `<scheme>.json` (topology, electrical, geometry) and `<scheme>.scada.json` (104 binding, roles, Main104 addresses). Confirms `D6`. | Element ids must stay aligned across the two files; a dangling binding must fail loudly at load. |
| **N5** | **Schema is read-only for now**, read/write once the Angular editor exists — but in the **final** format from the first read (per N2). | `B31` (ID counters, validation, undo/redo, locking) is deferred, not designed away. |
| **N6** | **Fewer assemblies than `D26`**, keeping its load-bearing rule. | §4. |
| **N7** | **Development runs against a test SCADA server**, not production. | Production keeps running DNC/DNBridge untouched until one cutover. The test server must carry a realistic `ct_2026` point set. |
| **N8** | **Two services, not one** — separate online and offline hosts. | §6. Recommended and adopted; rationale below. |
| **N9** | **DNC/TLV is deleted from DNB**, not kept dormant. | The DNBridge repo remains as frozen reference. Reverses `DesiredState_Stage1.md` §11 *for DNB only*. |

Still open: process topology for the engine (in-proc vs. child process), and the online↔offline
scheme handoff. See §7.

---

## 3. The core — two stores and the cycle

This is the basis of the whole application; everything else is a host or an editor around it.

```
  CONTINUOUS                                    ONCE PER SCHEME
  ┌──────────────────────────────┐              ┌────────────────────────────────┐
  │ SCADA ──► SCADA BUFFER ◄──── │              │ <scheme>.json      ──► model   │
  │           keyed by 104 addr  │              │ <scheme>.scada.json ──► binding│
  │           IN : measurements  │              │ <scheme>.ini/.bod2 ──► config  │
  │                Main104 1–7   │              │        │                       │
  │           OUT: setpoints     │              │        ▼ topology, node ordinals│
  │                telemetry     │              │          power domains          │
  │                Main104 mirror│              └────────────────────────────────┘
  └──────────────────────────────┘
                 │  ▲
      time slice │  │ dispatch
                 ▼  │
  ┌──────────────────────────────────────────────────────────────────┐
  │  EVERY TICK (1 min, non-overlapping)                             │
  │                                                                  │
  │  1. gate      — connected? params present? fresh? quality? open   │
  │                 boundary switches?     fail ⇒ skip, poke nothing  │
  │  2. slice     — freeze the buffer → CALC CACHE, keyed by element  │
  │                 id (apply nasobitel, sign, tap translation)       │
  │  3. split     — PQ split, drives the engine N× (dvChod)           │
  │  4. stages    — Qmin ▸ Qmax ▸ Loss ▸ Optimize                     │
  │                 each: build text → run DLL → decode by "<id>"     │
  │                 results written back into the CALC CACHE          │
  │  5. dispatch  — calc cache → SCADA buffer OUT → SCADA:            │
  │                 per-element Set-Q/Set-U/tap, Qmin/Qmax/Losses,    │
  │                 State, and the Main104 1–7 mirrors (separate IOA) │
  │  6. snapshot  — dump the cycle; rearm the delay                   │
  └──────────────────────────────────────────────────────────────────┘
```

Three invariants that define the design:

1. **The schema document is immutable.** Live values exist only in the calc cache. Enforced by the
   compiler (§4), not by discipline.
2. **One frozen slice per tick.** Every stage of a cycle sees the same input; SCADA keeps writing
   the buffer concurrently without disturbing a calc in flight.
3. **The gate is all-or-nothing.** A failed check skips the *whole* cycle and pokes nothing — never
   a partial actuation from a stale picture.

### The three parts of the SCADA buffer

| Part | Direction | Contents | Keyed by |
|---|---|---|---|
| Measurements | in | P, Q, U, tap position, switch state — everything the calc consumes | 104 address → element id |
| Main104 params 1–7 | in **and** out | `Active`, `RegMode`, `RegBranch`, `UNet_max/min`, `Qvvn`, `Q_tor` — mirrored back verbatim on a **separate IOA** after each completed cycle | 104 address pair (in/out) |
| Results | out | per-element Set-Q / Set-U / tap setpoints, plus `Q_min`, `Q_max`, `Losses`, `State` telemetry | element id → 104 command address |

---

## 4. Assemblies (proposal — confirm)

`D26`'s eight assemblies collapsed to five libraries plus hosts, keeping the one rule that carries
weight: **`DNB.Model` references nothing**, so live SCADA values and the engine *cannot* leak onto
the schema document — the compiler enforces invariant 1 above.

```
DNB.Model     elements (22 leaf classes), scheme, geometry,      → nothing
              connections, calc cache / CycleState
DNB.Calc      an3f4w P/Invoke, engine runner, result decode      → nothing
DNB.Scada     lib60870, ScadaClient, SCADA buffer                → nothing
DNB.Engine    schema reader, topology & node numbering,          → Model, Calc
              value derivation + row emission + document
              assembly, PQ split, stage chain     ← SCADA-FREE: this is the shared kernel
DNB.Online    SCADA binding, time slice, gate, cycle             → Model, Calc, Scada, Engine
              orchestration, dispatch, snapshots

hosts:  DNB.Wpf  ·  DNB.ServiceOnline  ·  DNB.ServiceOffline (+ web API)
```

**`DNB.Engine` is SCADA-free on purpose** — it is exactly what the offline service reuses. The one
expensive-to-undo mistake is letting the emitters or the decoder acquire a dependency on the cycle,
the gate, or SCADA; that is avoided for free by not adding the reference.

---

## 5. Blocks and their connections

Block ids are `migration_plan.md` §3. Below is what **connects** to what — the dependency edges
that decide build order and the ones that are easy to miss.

### 5.1 ⚠ The inverted dependency (new, from N2)

Dropping the v0 format changes the critical path at the very start:

```
   BEFORE (v0):   C++ exporter ──► v0 JSON ──► DTOs ──► mapper ──► C# model
   NOW    (N2):   C# model design ──► drives ──► C++ exporter update ──► JSON ──► C# model
                        ▲                                                         │
                        └──────────────── round-trip test ────────────────────────┘
```

**The C# element model (`B2`/`B3`/`B4`) is now a prerequisite for a C++ change.** Consequences:

- The model must be designed **before** the exporter is touched, and it must be right — every later
  model change costs a C++ edit, and the C++ side has no CI, is built by hand in Code::Blocks, and
  each iteration is slow and unverifiable by an agent.
- The completeness proof shifts. The old runtime tag-set comparison (`Serialize` vs `SerializeJson`)
  no longer proves the C# side is complete, because the JSON is now shaped by the C# model rather
  than by the TLV tags. **Two checks are needed:** the C++ tag-set check for *coverage*, and a C#
  round-trip (`read → write → compare`) for *fidelity*.
- The `D8` naming rule stops being a mapping convention and becomes the **contract** between the two
  languages.
- The `%.17g` + locale decimal-separator trap and the unsigned-negative-int trap still apply
  unchanged — they are properties of the C++ writer, not of the format version.

### 5.2 Dependency graph (phase 1)

```
  SCHEMA-SCOPED (runs once per schema, cacheable)
    B2/B3/B4 model ──► [C++ exporter] ──► B1 read ──► B4 graph ──► B5 topology, node ordinals
                                                          │
    B7 <scheme>.scada.json ──► binding, roles, Main104 ────┤
    B6 <scheme>.ini / .bod2 ──► engine config ─────────────┤
                                                          ▼
                                                    B18 power domains ¹

  PER CYCLE
    B8 SCADA buffer ──► B9 time slice ──► calc cache
                              │                │
    B10 Main104 params ───────┤                │
                              ▼                │
                         B11 gate  ── fail ⇒ skip cycle
                              │                │
                              ▼                ▼
                         B19 PQ split ══════════════╗  drives engine N×
                              │                     ║
                              ▼                     ║
    B12 derive ─► B13 emit rows ─► B14 assemble ────╫──► B21 run ──► B22 decode
         ▲              ▲                           ║        ▲          │
      B15 =Limity_Q   B16 stage tail   B17 =Mereni ─╝       B20        B23 special getters
                                            │                            │
                                            └────────────────────────────┘
                                                     │
                                            B24 stage chain: Qmin▸Qmax▸Loss▸Optimize
                                                     ▼
                                            B25 result collapse ──► B26 dispatch ──► SCADA
                                                                          ▲
                                                                   B27 snapshots
```

¹ Domain construction may be cacheable with `B5` — depends on whether it varies with switch state.
**Open** (`migration_plan.md` §5.4).

### 5.3 The connections that decide correctness

| Edge | Why it matters |
|---|---|
| **`B9` ↔ `B26`** | The inbound conversion and the outbound conversion must mirror each other exactly (`nasobitel`, sign convention). Getting either wrong is invisible until setpoints come out **backwards**. Specify and test them **together**, never separately. |
| **`B13` ↔ `B22`** | The quoted `"<id>"` is the *only* link between engine rows and scheme elements, in both directions. Byte-identical or the join silently finds nothing. |
| **`B19` → `B21`/`B22`** | The PQ split runs load flows **from inside a stage** (N3). The engine runner must therefore be reentrant-from-a-stage by design — this constrains `B21`'s interface from the first line of code. |
| **`B17` → `B23`** | `GetVoltageSetpoint(measIx)` is keyed by a measurement index **produced during input generation**. The reader must carry state the writer assigned. |
| **`B23` two ID spaces** | A controlled element's optimized **Q** is an *element* result; its **U** is the *node* result of the node it hangs on. One map cannot serve both — build two, or get silent zeros. |
| **`B7` → `B9`/`B11`/`B26`** | One config file feeds injection, the gate and dispatch. A dangling binding (id with no element) must **throw at load**; an empty virt-PQ list is **fatal**. |
| **`B10` → `B11`/`B16`/`B26`** | Main104 params gate the cycle, shape the stage tail, and are mirrored back out. A config-seeded default is **indistinguishable from a fresh SCADA value** to a freshness check — `Active` defaulting to `1` means regulation starts *on* without SCADA saying so. Accept knowingly or gate `Active` differently. |
| **`B5` schema-scoped** | Node ordinals are independent of switch state, so `B5` re-runs only on a schema **edit** — which is what makes the online scheme-reload story (§7) tractable. |

### 5.4 Work not covered by any existing block

Found by walking the requirements against the block map — these have **no `B` id** and need one:

| New | What | Where |
|---|---|---|
| **N-a** | **Delete the DNC/TLV side** — `Tlv/`, `Commands/`, `DncServer/`, the DNC-facing parts of `Core/`, `XChng.cfg` loader (N9) | DNB, one pass, first |
| **N-b** | **Multi-user layer** — login, sessions, per-user scheme storage, upload | offline service only |
| **N-c** | **Concurrency for a singleton engine** — the DLL is process-global; N concurrent user calcs need a queue or a pool of engine host processes | offline service |
| **N-d** | **Scheme hot-reload** — rebuild the schema-scoped state (`B5`/`B7`/`B18`) without dropping the SCADA connection | online service |
| **N-e** | **Test SCADA server point set** — a realistic `ct_2026` configuration on the test server (N7) | dev infrastructure |

---

## 6. Hosts

### Why two services, not one

The deciding constraint: **the an3f4w engine is a process-global singleton.** `anReadInpData` →
`anRunAnalysis` → read-back is a stateful sequence and is not thread-safe, so one process means one
serialized lane for all calculation.

1. **Head-of-line blocking on grid control.** In a single process, an ad-hoc user calc from Angular
   queues in the same lane as the 1-minute control tick — a user's what-if run can delay or skip a
   real actuation. This alone decides it.
2. **Blast radius.** The closed Delphi DLL has faulted before. A crash triggered by one user's
   malformed scheme would take the SCADA connection down with it.
3. **Release cadence.** The offline side redeploys as Angular evolves; the online side must survive
   those deploys, because grid control cannot pause for a web release.
4. **Attack surface.** Offline carries login, file upload and multi-user state. The online service
   should expose none of that.
5. **Scaling shape.** Offline scales by adding engine processes; online is fixed at exactly one and
   must stay that way — the SCADA slave permits a single connection, and a recycling web tier would
   fight over the slot.

The cost is low: everything worth sharing (`DNB.Model`, `DNB.Calc`, `DNB.Engine`) is already a
library referenced by both. Only hosting is duplicated.

### The three hosts

| Host | Role | When |
|---|---|---|
| **`DNB.Wpf`** | Bring-up and operations shell — START/STOP, live element grids, SCADA traffic, log view, basic settings, "run cycle now". Same shape as today's WPF, minus everything DNC/TLV. | **first** — this is how the online path gets built and trusted |
| **`DNB.ServiceOnline`** | Production. Owns the single SCADA slot, one scheme, the control loop, one engine. Headless. | after the loop is trusted |
| **`DNB.ServiceOffline`** | Web API + multi-user + Angular's backend. No SCADA. Own engine capacity. | with the Angular editor |

`DNB.Wpf` and `DNB.ServiceOnline` run the same core, so moving between them is no core change.

---

## 7. Open decisions

| # | Question | Blocking? |
|---|---|---|
| **O1** | **Engine process topology** — in-process, or behind a child `EngineHost` process? Deferred by choice. The interface (`ICalcEngineRunner`) makes it a swap either way, provided N3's reentrancy is respected. | no — but decide before production |
| **O2** | **Online ↔ offline scheme handoff.** When Angular saves a scheme the online service is running, what happens? Recommendation: an explicit **reload** operation (N-d), *not* a process restart — a restart drops the SCADA slot, a reload keeps the connection and rebuilds only the schema-scoped state. | no — needed when Angular lands |
| **O3** | **Pre-calc gate design (`B11`)** — faithful port of the C++ hard-coded `mapFilter` priority table, vs. a redesign around a per-point `must_be_valid` operator flag plus an energisation check. Since `<scheme>.scada.json` is authored fresh, the flag is now cheap to carry. | at `B11` |
| **O4** | **`anGetSystemLosses1f` on kind 21** returned `0+j0` in testing while per-branch `m_DeltaSabc` carried losses — the Loss telemetry getter is unconfirmed. | at `B23` |
| **O5** | **PQ-split domain caching** — does domain construction depend on switch state? Decides whether `B18` is schema-scoped or per-cycle. | at `B18` |
| **O6** | **Per-section decimal marks and separators** in the engine input — confirm against a real dump before freezing. Mismatches are **silent** parse failures. | at `B14` |
| **O7** | **Transformer orientation in `B5`** — which winding the voltage arrived by; 2W and 3W are not symmetric and a swapped U1/U2 is silent. Port gate-for-gate, validate on a multi-voltage network. | at `B5` |
| **O8** | **`RegMode` startup default** — config carries `2` (`rm_MinLoss`), its own comment says `1` (`rm_MinTransfQ`). | at `B10` |
| **O9** | **Does the GIS/map view survive into Angular?** Decides whether `m_TopoPos` and GIS polylines are required. | phase 2 |

---

## 8. Starting position

**Already done and carried over:**

- `B8` SCADA client + buffer — IEC104 connect/STARTDT/GI, supervisor reconnect with address
  rotation, 14 monitoring types, control sends, quality and timestamps. Production-hardened.
- `B20` DLL lifecycle and P/Invoke — load order, init/done, x64, serialization. Verified live.
- `B21`/`B22` partial — the run path and struct offsets are proven by test code; both need promoting
  to real components.
- `ct_2026.json` — the schema export exists and passes its completeness check. Will be **regenerated**
  in the final shape per N2.

**Ground truth to test against:** two complete recorded cycles (`C:/_egc/logs/Dumps/`), six engine
inputs for `ct_2026` (`DNB/data/`), and the `ct_2026` scheme itself. The recorded snapshot carries
**both sides** of the `B9` conversion, which makes it a free oracle for the `nasobitel`/sign trap.

**First tasks, in order:**

1. **Strip DNC/TLV** from the DNB fork (N-a); rename assemblies; delete the stale inherited docs.
2. **Design the C# element model** (`B2`/`B3`/`B4`) — now on the critical path (§5.1).
3. **Update the DNC JSON exporter** to emit it; regenerate `ct_2026.json`; round-trip test.
4. **Author `<scheme>.scada.json`** (`B7`) — does not exist yet; needs a one-time export from
   `.db3` + `XChng.cfg`.
5. **Topology and node numbering** (`B5`), then the emitters (`B12`–`B17`).

A natural first proof, needing no SCADA and no DLL: read `ct_2026.json` plus a recorded snapshot and
generate one engine input **byte-identical** to the recorded one. It exercises the model, the graph,
the topology pass and every emitter at once, with a binary pass/fail.
