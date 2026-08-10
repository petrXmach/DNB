# DNB — development state

> **What this is.** The single answer to *"where are we?"*. **Update after every implementation
> change** — a block changing status, a file landing, an `O*` question closing.
>
> Architecture and the *why* are in [`DNB_overview.md`](DNB_overview.md); it is stable and this file
> supersedes its §8 "starting position". Block ids `B*` are
> [`migration_plan.md`](../../DNC/Doc/App_migration/AI/overview/migration_plan.md) §3; `N*`/`O*` are
> `DNB_overview.md` §2/§7. Doc rules: [`documentation-guide.md`](documentation-guide.md).
>
> **Last updated:** 2026-08-10

---

## 1. Solution

`DNB.slnx` — .NET 10 (SDK 10.0.203), builds clean, 0 warnings. Common settings live in
`src/Directory.Build.props` (deliberately not at repo root, so `libs/` does not inherit our warning
level). Each `.csproj` header comment states the references it must **not** acquire.

| Project | TFM | References | Content |
|---|---|---|---|
| `libs/lib60870` | net10.0 | — | vendored upstream v2.3.0, retargeted from net8; `NoWarn` for two TLS obsoletions (dead paths) |
| `src/DNB.Model` | net10.0 | **nothing** | **empty** |
| `src/DNB.Iec104` | net10.0 | lib60870 | **empty** |
| `src/DNB.Calc` | net10.0, x64 | nothing | **empty** — `Native/` copy rules present but commented out |
| `src/DNB.Scada` | net10.0 | Iec104 | **empty** |
| `src/DNB.Engine` | net10.0 | Model, Calc | **empty** |
| `src/DNB.Online` | net10.0 | Model, Calc, Scada, Engine | **empty** |
| `src/DNB.Wpf` | net10.0-windows, x64 | Online | template `App.xaml` / `MainWindow.xaml` only |

**Everything is a skeleton.** The old DNBridge solution was removed wholesale (commit `62e6550`);
nothing has been ported back in yet. Reusable code sits in the frozen
[`../../DNBridge`](../../DNBridge) repo — see the "port from" column below.

⚠ **`DNB.Iec104` is a 6th library**, one more than `DNB_overview.md` §4 / N6 describe. It exists so
the value codec can be shared with a standalone SCADA simulator (N-g). Fold into §4 or drop the split.

## 2. Blocks

`—` not started · `PORT` code exists in `../DNBridge`, awaiting port · `WIP` in progress · `✅` done

| Block | What | State | Port from / notes |
|---|---|---|---|
| **N-a** | Strip the DNC/TLV side | ✅ | whole old solution deleted, `62e6550` |
| **B2/B3/B4** | **C# element model** | — | **critical path** — blocks the C++ exporter change (§5.1) |
| B1 | Read `<scheme>.json` | — | blocked on the model (N2) |
| B5 | Topology, node ordinals | — | `O7` open (transformer orientation) |
| B6 | `.ini` / `.bod2` engine config | — | |
| B7 | `<scheme>.scada.json` | — | **file does not exist yet**; needs a one-time export from `.db3` + `XChng.cfg` |
| B8 | SCADA client + buffer | **PORT** | `src/DNBridge/Scada/ScadaClient.cs` (926 lines, production-hardened) → `DNB.Scada` |
| B9 | Time slice → calc cache | — | mirror of B26 — specify and test **together** (§5.3) |
| B10 | Main104 params 1–7 | **PORT** | `Elements/Main104Catalog.cs`; `O8` open (`RegMode` default) |
| B11 | Pre-calc gate | — | `O3` open (faithful port vs. per-point flag) |
| B12–B17 | Derive, emit rows, assemble | — | `O6` open (decimal marks); the bulk of phase 1 |
| B18/B19 | Power domains, PQ split | — | `O5` open (is domain construction switch-state dependent?) |
| B20 | DLL lifecycle + P/Invoke | **PORT** | `src/DNBridge.Calc/` (exact copy of the DNBridge folder) — verified live |
| B21/B22 | Engine run + result decode | **PORT** | proven by test code (`An3f4wCalcTest`, 323 lines) — **needs promoting to real components**, not porting as-is |
| B23 | Special result getters | — | `O4` open (`anGetSystemLosses1f` returned `0+j0`) |
| B24–B27 | Stage chain, collapse, dispatch, snapshots | — | |
| **N-f** | **Extract the IEC-104 codec** into `DNB.Iec104` | — | decode exists (`ScadaClient.cs:570–711`); **encode does not exist as a unit** — fragments inside six `Send*` methods |
| **N-g** | **SCADA simulator** (CS104 slave) | — | separate solution; supersedes the old `Scada/Replay/`. Validate the codec against recorded dumps, never against the simulator |
| N-d | Supervision & restart policy | — | online service |
| N-e | Test SCADA point set | — | partly superseded by N-g |
| N-b/N-c | Multi-user layer, engine concurrency | — | offline service, phase 2 |

`N-f`/`N-g` are new ids not yet in `DNB_overview.md` §5.4.

## 3. Ground truth available

| Artifact | Proves |
|---|---|
| `data/*.txt` — six real `ct_2026` engine inputs | byte-comparison target for the emitters (B12–B17) |
| `C:/_egc/logs/Dumps/` — two complete recorded cycles | carries **both sides** of the B9 conversion → free oracle for the `nasobitel`/sign trap |
| [`../../DNC/Schema/ct_2026/`](../../DNC/Schema/ct_2026) | `.egc3` + `.json` + `.inp_*.txt` + `.db3` |

## 4. Next

1. **Port `DNB.Calc`** — copy `src/DNBridge.Calc/` including `Native/`, re-enable the DLL copy rules,
   namespace to `DNB.Calc`. Smallest possible first port; proves the x64 + native-load path still works.
2. **Port `DNB.Scada` + extract `DNB.Iec104`** (B8 + N-f) — one pass, since every line gets touched
   for the namespace anyway. Carry the concurrency comments verbatim: attempt-id guard, the 15 s
   stuck-connect timeout above lib60870's 10 s T0, never `Close()` inside a handler.
3. **Design the C# element model** (B2/B3/B4) — the critical path. Do not let infrastructure work
   push this back; a C++ exporter iteration is slow and unverifiable by an agent, so the model must
   be right before it is touched. Gets a `design/element-model.md`.
4. **Author `<scheme>.scada.json`** (B7).
5. **Topology and node numbering** (B5), then the emitters.

**First proof worth aiming at** — needs no SCADA and no DLL: read `ct_2026.json` plus a recorded
snapshot and generate one engine input **byte-identical** to the recorded one. Exercises the model,
the graph, the topology pass and every emitter at once, with a binary pass/fail.

**Standing constraint:** WPF is written against `IEngineClient` and state projections only (N11).
Anything WPF shapes privately is something the Web API later cannot reuse.
