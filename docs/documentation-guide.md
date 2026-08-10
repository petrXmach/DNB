# How DNB documentation works

> **What this is.** Where each kind of knowledge goes in this repo, which files are *live* (updated
> as work happens) and which are *stable* (edited only when a decision changes, never appended to).
> Read this before creating a new `.md` file.
>
> Cross-repo rules (which of the three repos owns what) are canonical in
> [`../../DNBridge/docs/documentation-guide.md`](../../DNBridge/docs/documentation-guide.md) and
> summarised in [`../CLAUDE.md`](../CLAUDE.md) §2. This file covers **DNB's own docs only**.

---

## 1. The four levels

| Level | File(s) | Loaded | Lifecycle |
|---|---|---|---|
| **L0** | [`../CLAUDE.md`](../CLAUDE.md) | **every message** | stable — the map: what DNB is, the three repos, the doc index, standing rules |
| **L1** | [`DNB_overview.md`](DNB_overview.md) | on demand | **stable** — architecture, decisions `N*`, open questions `O*`. The *why*. |
| **L1** | [`DevState.md`](DevState.md) | on demand | **live** — where the work actually is, and what's next. The *now*. |
| **L1** | `coding-standards.md` | on demand | stable — C# / TypeScript / Angular conventions *(not written yet)* |
| **L2** | `design/<topic>.md` | on demand | **one per subsystem** — born as a plan, ends as the record. See §3. |

**L0 size budget: under ~20 KB.** Every byte loads on every turn. Before adding to `CLAUDE.md`, ask:
*does this need to be in context for every task, or only when working this subsystem?* If the latter,
it belongs in L1 or L2.

## 2. The two live files, and their cadence

Everything else is stable. These two are not:

- **[`DevState.md`](DevState.md)** — the single answer to *"where are we?"*. Structured as a **table
  keyed by block id** (`B1`–`B33` from the migration plan), not prose, because a table resists rot:
  a stale row is visible, a stale paragraph is not. **Update after every implementation change** —
  a block moving status, a file landing, an `O*` question closing. Its `§ Next` section is the
  short-horizon plan (3–7 items, ordered).
- **`design/<topic>.md`** while its subsystem is being built. See below.

## 3. `design/` — the plan *is* the record

The trap with planning documents is that they rot the moment the work lands: you end up with a stale
plan and a separate description of what was actually built. So don't create two files.

```
   before coding   ──►   during   ──►   after
   the plan:              amended        the record:
   what to build,         as reality     what exists, why it is shaped
   in what order,         corrects it    that way, the traps found
   what to verify                        (plan sections deleted)
```

**One file per subsystem, `docs/design/<topic>.md`.** It starts as the plan for that piece of work.
As the work lands, plan sections are **replaced** by what was actually built — not appended to,
**replaced**. When the subsystem is done the file contains no future tense and no checklists.

Create one only when a subsystem needs real design before coding (the element model, the emitters,
the codec, the simulator). Small work needs no file — it goes straight into `DevState.md` as a row.

**Do not keep a "phases and blocks" plan here.** Phasing, the decisions `D1`–`D27` and the block map
`B1`–`B33` are owned by
[`migration_plan.md`](../../DNC/Doc/App_migration/AI/overview/migration_plan.md) in the DNC repo.
Work that has no `B` id gets an `N-*` id in [`DNB_overview.md`](DNB_overview.md) §5.4 — not a new
plan document.

## 4. What to write down

Write it only if it is one of these four:

1. **Non-local** — a fact spread over many files that must otherwise be reconstructed by reading all
   of them.
2. **Verified alignment** — work checked against a committed artifact (a generated engine input ⇄ a
   recorded one).
3. **Hidden control flow** — gates, derivation precedence, sign conventions, concurrency invariants
   that are invisible in the code (the attempt-id guard, the deadlock rule).
4. **Decisions** — what was chosen and why. Not in the source at all; the irreplaceable category.

**Do not write:** member and field lists, type declarations, class hierarchies, "which files exist",
or long code examples. All are one grep away, and a stale copy is worse than no copy. Point at the
source: `file.cs:120` is clickable and cannot go quietly out of date the way a transcription can.

## 5. Rules that apply to every file here

1. **State the current state, not its history.** When a decision changes, **edit it in place**. No
   "superseded" banners, no record of options considered and dropped. Git is the audit trail.
2. **Docs live in this repo, never in agent memory.** The repos are synced across machines; a fact in
   a per-machine memory store is unavailable everywhere else. Agent memory is for user preferences
   and working style only — never architecture.
3. **Update at a stable state, not mid-iteration.** If a component is still being designed, wait
   until the approach settles. The exception is `DevState.md`, which is meant to be current.
4. **Cross-repo links are repo-relative** — `../../DNC/...`, never `C:\...`. All three repos sit
   side-by-side under `C:\_EGC` on every machine.
5. **Every non-trivial claim carries a `file:line` reference.**
6. **Czech domain terms stay verbatim** (`dodáv`, `odběr`, `provoz`, `nasobitel`) with a translation
   on first use — they are the terms in the C++ source and in the engine input.
7. **Never duplicate DNC's C++ analysis here.** Link to it. One source, no drift.

## 6. Where a new fact goes — the decision in one pass

```
Is it about the C++ original or how to migrate?      → ../DNC  (link to it, never copy)
Is it a decision about DNB's shape?                  → DNB_overview.md  (§2 N* or §7 O*)
Is it "what is built / what is next"?                → DevState.md
Is it how one DNB subsystem works?                   → design/<topic>.md
Is it a convention every task must follow?           → CLAUDE.md §7  (only if it earns the budget)
Is it a member list / tag table / file listing?      → nowhere. Point at the source.
```
