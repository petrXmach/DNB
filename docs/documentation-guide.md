# Documentation Guide (DNBridge + DNC)

How to write and maintain documentation across the two sibling repositories. This is the
**canonical** guide for both; the DNC repo carries a short pointer back to it
(`../DNC/Doc/documentation-guide.md`).

The two repos sit side-by-side under `C:\_EGC` on every machine, so **cross-repo links are
repo-relative** (`../DNC/...`, `../DNBridge/...`) — never hardcode `C:\`.

---

## 1. Which repo owns what

| Repo | Owns | Lifecycle |
|------|------|-----------|
| **DNC** (`../DNC`) | "How to migrate." The C++ original (DNCalc/EVlivy3 + DNCors_IEC104), its analysis, the DLL/engine spec, the per-element port reference, and the step-by-step migration plan. | **Living** — refined continuously throughout the (long) port as manual runs confirm/correct the analysis. The C++ *code* is stable (not modified); the *docs about it* keep evolving. |
| **DNBridge** (this repo) | "The new system as built." Architecture, data structures, IEC-104 implementation, coding standards, live dev status, and the new project/repo structure. | **Grows with the implementation.** During migration it may carry thin links into DNC; when migration is done those links are dropped and DNBridge docs stand alone. |

**The rule that resolves "where does this go?":**
- Is it about the **C++ original or the port plan**? → DNC.
- Is it about the **.NET system we are building**? → DNBridge.
- **Never copy** C++ analysis into DNBridge — link to the DNC original. One source, no drift.

---

## 2. Three layers (both repos)

### Layer 1 — `CLAUDE.md` (always loaded, every message)
Project overview, tech stack, conventions, architecture map, and a **Companion repository**
block pointing at the sibling repo (path + role + what to search there).

- **Size budget: keep under ~20 KB.** Every byte loads on every turn. Before adding, ask:
  "Does this need to be in context for *every* task, or only when working this subsystem?"
  If the latter → Layer 2.
- **Belongs:** stable conventions, the high-level architecture map, protocol invariants
  (TLV/IEC-104 must-not-break rules), the working rules.
- **Does NOT belong:** long code examples, per-file detail, design rationale, status. Those
  go to Layer 2 / `DevState.md`.

### Layer 2 — `docs/*.md` (DNBridge) / `Doc/**` (DNC) — loaded on demand
The bulk of knowledge. Read only when working the relevant subsystem.

**DNBridge `docs/`:**
| File | Purpose |
|------|---------|
| `documentation-guide.md` | this guide |
| `DevState.md` | **live** implementation status — updated after every implementation change |
| `architecture.md` | target Stage-1 design (calc loop, hosts, `DNBridge.Calc`, the seams) |
| `coding-standards.md` | async/`CancellationToken`, thread-safety, library-never-calls-UI, naming |
| `iec104.md` | IEC-104 implementation notes (the ScadaClient guide folds in here) |
| `reference-links.md` | **temporary** — thin pointers into `../DNC` used during migration; deleted when the port is done |

**DNC `Doc/`** keeps its existing layered structure: `Analysis_from_Evlivy3/ai/` (L0/L1/L2
source-verified C++ analysis), `dll/` (engine spec), `App_migration/` (per-element port docs).

### Layer 3 — task references
Deeper, subsystem-specific references read only while working that area (e.g. `iec104.md`,
`coding-standards.md`, and the DNC `ai/L2_*` deep dives). **No `.claude/skills/` for now** —
promote a doc to a skill only if it grows large and would benefit from auto-triggering.

---

## 3. When to update

- **Update at a stable state, not mid-iteration.** If a component is still being designed or
  you are trying approaches, wait until it settles, then document the chosen one.
- **Exception — `DevState.md`:** update after **every** implementation change (status moves,
  new component, fixed issue). It is the primary orientation reference.
- **DNC migration docs:** update whenever a manual run against the fixed schema confirms or
  corrects the analysis, or closes one of the ⚠ open items.
- **Multi-chat work:** update docs in the final chat when the feature is done.

---

## 4. Repo docs vs auto-memory

- **Durable project/architecture facts → repo docs** (versioned, synced across machines,
  reviewable). This is the source of truth.
- **Auto-memory (`MEMORY.md`) → only user/feedback/preferences** — *not* architecture facts.
  Architecture truth must not live in a per-machine memory store.
- This mirrors the long-standing DNC rule: *write docs inside the git repo, never to external
  memory outside it.*

---

## 5. Quick reference

| Question | Answer |
|----------|--------|
| Where do I document a new .NET feature/class? | DNBridge `docs/` (+ `DevState.md` status line) |
| Where do I record current implementation status? | DNBridge `docs/DevState.md` |
| Where does the C++ analysis / "how the original works" live? | DNC `Doc/Analysis_from_Evlivy3/` — link, don't copy |
| Where does the migration/port plan live? | DNC (`Doc/App_migration/`, `ai/L2_dnbridge_full_transcode.md`) — living doc |
| Where do I document an IEC-104 detail? | DNBridge `docs/iec104.md` |
| Where do coding conventions go? | short rules in `CLAUDE.md`; detail in `docs/coding-standards.md` |
| Cross-repo link format? | repo-relative: `../DNC/...` / `../DNBridge/...` |
| Should I copy a DNC analysis doc into DNBridge? | No — link to it (`reference-links.md` during migration) |
| Should I add a 40-line example to `CLAUDE.md`? | No — Layer 2 doc; keep `CLAUDE.md` under ~20 KB |
