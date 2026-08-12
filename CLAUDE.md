# DNB — orientation & documentation index

> **This file is a map, not a manual.** It says what DNB is in a paragraph, where every other
> document lives, and which rules apply to all work here. Anything longer than a few lines belongs
> in a linked doc, never here.

---

## 1. What DNB is

**DNB** is a C# / .NET 8 application replacing **two** legacy programs with one: the C++
**DNCalc/DNCoRS** (grid model, editor, calc-engine driver) and the C# **DNBridge** (IEC-104 SCADA
middleware). It talks **directly** to SCADA over IEC 60870-5-104 and **directly** to the `an3f4w`
calculation DLL. There is **no DNC, no TLV, no TCP middleware, no `.egc3`, no SQLite**.

Two modes over one calculation kernel: **online** (1-minute unattended U/Q control loop, one scheme,
first target) and **offline** (on-demand calcs, many schemes, multi-user, Angular editor, later).

⚠ **This repo is a copy of the `DNBridge` fork and still contains the DNC/TLV side** (`src/DNBridge/Tlv`,
`Commands/`, `DncServer/`). Stripping it is task **N-a** — the first job. Until then, existing code
and the target architecture disagree; the target always wins.

**▶ Read [`docs/DNB_overview.md`](docs/DNB_overview.md) first for any work in this repo.** It owns the
architecture: the two stores (SCADA buffer ⇄ calc cache), the per-tick cycle, the assembly layout,
decisions **N1–N12** and open questions **O1–O9**.

### This repo's own documents

| Doc | Contents | Lifecycle |
|---|---|---|
| **[`docs/DNB_overview.md`](docs/DNB_overview.md)** | Architecture and the *why* — the cycle, assemblies, decisions `N*`, open questions `O*` | stable; edit in place |
| **[`docs/DevState.md`](docs/DevState.md)** | **Where the work is and what's next** — block-by-block status, solution state | **live — update after every implementation change** |
| **[`docs/problems_to_be_solved.md`](docs/problems_to_be_solved.md)** | **Punch list `P1`–`P10`** — structural mistakes in the skeleton and defects in the code queued for porting. **Read before touching the reference graph, the codec or the buffer.** | transitional — items leave as they are resolved; delete when empty |
| [`docs/documentation-guide.md`](docs/documentation-guide.md) | Which file a new fact belongs in, and the rules for all of them | stable |
| `docs/design/<topic>.md` | One per subsystem — **born as a plan, ends as the record** of what was built | live while its subsystem is built |
| `docs/coding-standards.md` | C# / TypeScript / Angular conventions | *not written yet* — see §8 |

---

## 2. The three repositories

All three sit side-by-side under `C:\_EGC`; **use repo-relative links** (`../DNC/…`), never `C:\`.

| Repo | Owns | Status |
|---|---|---|
| **`.` (this, `dnb`)** | **The .NET system being built.** Architecture, decisions, implementation. | living |
| **[`../DNC`](../DNC)** | **"How to migrate."** The C++ original (DNCalc/EVlivy3 + DNCors_IEC104), its source-verified analysis, the `an3f4w` DLL spec, and the migration plan. | living |
| **[`../DNBridge`](../DNBridge)** | The DNC-connected bridge this repo was forked from. **Frozen reference only** — its `CLAUDE.md` and `docs/` describe the *old* TLV-based system. | frozen |

**Where does a new fact go?** About the **C++ original or the port plan** → `../DNC`. About **the
.NET system we are building** → here. **Never copy** C++ analysis into this repo — link to it.
DNB-local rules and the level-by-level decision table:
[`docs/documentation-guide.md`](docs/documentation-guide.md). Cross-repo rules are canonical in
[`../DNBridge/docs/documentation-guide.md`](../DNBridge/docs/documentation-guide.md).

---

## 3. Migration reference — `../DNC/Doc/App_migration/AI/`

Reading order and scope rules: [`../DNC/Doc/App_migration/README.md`](../DNC/Doc/App_migration/README.md).

| Doc | Contents |
|---|---|
| **★ [`overview/migration_plan.md`](../DNC/Doc/App_migration/AI/overview/migration_plan.md)** | **The single top-level migration document** — two phases, decisions **`D1`–`D27`**, block map **`B1`–`B33`**, dependency graph, golden masters, open items, C++ source map. `docs/DNB_overview.md` references its `D`/`B` ids and **amends** it via `N1`–`N12`. |
| [`overview/cpp_element_model.md`](../DNC/Doc/App_migration/AI/overview/cpp_element_model.md) | The C++ element model as it is — object graph, inheritance, the four data-attachment mechanisms, what is runtime-only. Feeds `B1`–`B4`. |
| [`overview/cpp_connection_model.md`](../DNC/Doc/App_migration/AI/overview/cpp_connection_model.md) | The bipartite node↔element graph, the **two incompatible meanings of "terminal"**, the gates that make adjacency ≠ conductivity. Feeds `B1`–`B5`. |
| [`overview/schema_json_format.md`](../DNC/Doc/App_migration/AI/overview/schema_json_format.md) | The JSON export bridge, the C++ member ⇄ JSON key ⇄ C# property **naming rule** (`D8`) and its exceptions, the `%.17g` / locale traps. |
| [`overview/assign_measurement.md`](../DNC/Doc/App_migration/AI/overview/assign_measurement.md) | *Přiřadit měření* (`cl_Measurement_Attrib`) — the only calc-relevant attribute; sign/entry-mode conventions, its three roles in `cl_PQ_Split`. |
| **[`elements/_index.md`](../DNC/Doc/App_migration/AI/elements/_index.md)** | Class ⇄ TLV tag ⇄ **Bodor code** ⇄ port coverage for all 22 element classes. **Read before touching any element**, then its per-element file in [`elements/`](../DNC/Doc/App_migration/AI/elements/). |

## 4. C++ behaviour reference — `../DNC/Doc/Analysis_from_Evlivy3/ai/`

Documents DNCoRS **as it runs today** (separate track from the migration docs). Start at
[`L0_system_context.md`](../DNC/Doc/Analysis_from_Evlivy3/ai/L0_system_context.md) (routing index).
The ones phase 1 actually needs:

| Doc | Why you'd open it |
|---|---|
| [`L2_engine_pipeline.md`](../DNC/Doc/Analysis_from_Evlivy3/ai/L2_engine_pipeline.md) | End-to-end: input assembly → run → decode → dispatch. The behaviour being ported. |
| [`L2_voltage_ctrl_calc_loop.md`](../DNC/Doc/Analysis_from_Evlivy3/ai/L2_voltage_ctrl_calc_loop.md) | The periodic cycle and the stage chain (Qmin ▸ Qmax ▸ Loss ▸ Optim ▸ Control). |
| [`L2_dncors_input_filter.md`](../DNC/Doc/Analysis_from_Evlivy3/ai/L2_dncors_input_filter.md) | The pre-calc gate — `Filter()` (quality) + `Check_Boundary()` (topology). `B11`. |
| [`L2_dll_input_masks.md`](../DNC/Doc/Analysis_from_Evlivy3/ai/L2_dll_input_masks.md) | **Bodor text grammar** — section/row/column layout of the engine input. `B13`/`B14`. |
| [`L2_egc3_to_bodor_derivation.md`](../DNC/Doc/Analysis_from_Evlivy3/ai/L2_egc3_to_bodor_derivation.md) | How row *values* are derived (not a prop→column copy). `B12`. |
| [`L2_dll_lifecycle_pinvoke.md`](../DNC/Doc/Analysis_from_Evlivy3/ai/L2_dll_lifecycle_pinvoke.md) | Load / init / run / teardown + P/Invoke contract. `B20`/`B21`. |
| [`L2_dll_result_structs.md`](../DNC/Doc/Analysis_from_Evlivy3/ai/L2_dll_result_structs.md) | 64 KB buffer protocol, packed `AN_*_4_T` structs, the `"<id>"` join, units. `B22`. |
| [`L2_db104_mapping.md`](../DNC/Doc/Analysis_from_Evlivy3/ai/L2_db104_mapping.md) · [`L2_command_setpoints.md`](../DNC/Doc/Analysis_from_Evlivy3/ai/L2_command_setpoints.md) | The `<scheme>.db3` 104 binding and the input/output direction bit — the source for authoring `<scheme>.scada.json` (`B7`). |
| [`L2_phase_data_model.md`](../DNC/Doc/Analysis_from_Evlivy3/ai/L2_phase_data_model.md) | 1-phase vs 3-wire vs 4-wire; where per-phase values live. |

**`an3f4w` engine spec:** [`../DNC/Doc/dll/`](../DNC/Doc/dll) — `an3f4w_functions_1/2.md`,
`an3f4w_data_types.md`, `an3f4w_examples.md`, `dll_call_sequence_orpf_repro.md`, plus the vendor PDFs.

**Carried-over DNBridge analysis still worth reading** (frozen repo):
[`Main104_calc_usage.md`](../DNBridge/docs/Main104_calc_usage.md) (how the 7 Main104 params drive the
calc — `B10`/`B16`), [`Main104_Elements_Analysis.md`](../DNBridge/docs/Main104_Elements_Analysis.md),
[`AI_GUIDE_SCADA_WRAPPER.md`](../DNBridge/docs/AI_GUIDE_SCADA_WRAPPER.md) (the `ScadaClient` that
`B8` inherits).

## 5. Ground truth

| Artifact | What it proves |
|---|---|
| `data/*.txt` (`dvChod`, `QMin`, `QMax`, `Loss`, `Optim`, `Oper`) | Six real `ct_2026` engine inputs — byte-comparison targets for the emitters. |
| `C:/_egc/logs/Dumps/` | Two complete recorded cycles; carries **both sides** of the `B9` conversion → free oracle for the `nasobitel`/sign trap. |
| [`../DNC/Schema/ct_2026/`](../DNC/Schema/ct_2026) | The ground-truth scheme: `.egc3` + `.json` + `.inp_*.txt` + `.db3`. |

---

## 6. Code layout & build

```
DNBridge.slnx
├── src/DNBridge/        — core library (still carries the DNC/TLV side; see N-a)
├── src/DNBridge.Calc/   — an3f4w P/Invoke + Native/
├── src/DNBridge.Wpf/    — WPF bring-up shell (code-behind, no MVVM)
└── libs/lib60870/       — IEC 60870-5-104, referenced as source (not NuGet)
```

Target assemblies (`docs/DNB_overview.md` §4): `DNB.Model` · `DNB.Calc` · `DNB.Scada` · `DNB.Engine`
· `DNB.Online`, plus hosts `DNB.Wpf` / `DNB.ServiceOnline` / `DNB.WebApi` / `DNB.ServiceOffline`.

```sh
dotnet build DNBridge.slnx
dotnet run --project src/DNBridge.Wpf
```

`dnbridge.ini` goes next to the host executable. Tooling: Visual Studio 2026, C# 12 / .NET 8, x64
(the DLL is 64-bit only). **Never build the C++ `../DNC` projects** — the developer compiles those by
hand in Code::Blocks.

---

## 7. Standing rules

**Architecture invariants** — the ones that are expensive to undo:

1. **`DNB.Model` references nothing.** Live SCADA values and the engine cannot leak onto the schema
   document — the compiler enforces it, not discipline.
2. **`DNB.Engine` stays SCADA-free.** It is exactly what the offline service reuses.
3. **The schema document is immutable**; live values exist only in the calc cache.
4. **One frozen slice per tick**; the pre-calc gate is **all-or-nothing** — a failed check skips the
   whole cycle and pokes nothing.
5. **Hosts are thin.** WPF and the Web API are written against **`IEngineClient`** and state
   projections only — never `IDnbEngine`, `Element104` or raw event args (`N11`).
6. **Restart on change** (`N12`) — a schema or config change means a full process restart. No hot
   reload.

**Working rules:**

7. **`async`/`await` + `CancellationToken`** for all I/O; the library never calls into UI.
8. **Never break the IEC-104 wire behaviour** — SCADA is fixed and unmodifiable.
9. **Docs live in the repo, never in agent memory** — the repos are synced across machines.
   Record only non-local facts, verified alignments, hidden control flow, and **decisions**.
10. **State the current decision, not its history.** Edit in place; git is the audit trail.
11. **Execution policy:** work directly in the main session for quick, simple or medium tasks.
    Spawn parallel subagents only for genuinely independent large workstreams. Default sequential.

---

## 8. Coding standards — *to be added*

> Placeholder. Best practices and conventions for **C#**, **TypeScript** and **Angular** will land
> here (or in `docs/coding-standards.md`, linked from here) once agreed. Until then, follow the
> existing style in `src/` and the working rules in §7.
