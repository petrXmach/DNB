# Problems to be solved

> **What this is.** Defects and structural mistakes found in the current DNB skeleton and in the
> `../../DNBridge` code queued for porting — each with the evidence, the consequence, and a proposed
> fix. **This is a punch list, not a design doc:** an item leaves this file when it is resolved, and
> the resolution goes to its real home (`DNB_overview.md` for a decision, `design/<topic>.md` for a
> subsystem, `DevState.md` for status). When this file is empty, delete it.
>
> Items marked **DECIDE** need the developer's call before code is written against them.
>
> **Last updated:** 2026-08-12

---

## 0. Summary

| # | Problem | Fix before |
|---|---|---|
| **P1** | `DNB.Engine → DNB.Calc` points the wrong way — the kernel is welded to the native DLL | any `DNB.Engine` code |
| **P2** | Calc cache in `DNB.Model` defeats the invariant that assembly exists to enforce | any `DNB.Model` code |
| **P3** | `IEngineClient` has no home; the project layout makes `N10` unimplementable | `DNB.Wpf` shell |
| **P4** | `DNB.Online` references `Model` and `Calc` redundantly — and one of them is load-bearing | `DNB.Online` code |
| **P5** | Every value is a `double`; the domain is erased | `DNB.Iec104` |
| **P6** | The `M_ST` transient bit is discarded — a moving tap is indistinguishable from a settled one | `DNB.Iec104` |
| **P7** | Quality `0` means both "good" and "no quality descriptor" | `DNB.Iec104` |
| **P8** | One timestamp field carries two different meanings; freshness uses the wall clock | `B11` |
| **P9** | Buffer slot updates are neither atomic nor published; the slice shares mutable references | `DNB.Scada` |
| **P10** | No test project, no `global.json`, no `.editorconfig`; `PlatformTarget` scattered | first code |

---

## 1. Reference graph

### P1 — `DNB.Engine → DNB.Calc` is the wrong edge

`src/DNB.Engine/DNB.Engine.csproj` references `DNB.Calc`, which is `PlatformTarget=x64` and exists to
load `an3f4w.dll` + `borlndmm.dll`. The architecture is careful to keep `DNB.Engine` SCADA-free
(`DNB_overview.md` §4) and then couples it to the native engine in the same breath.

Three consequences, all already contradicted by something written down:

- **The first proof cannot run clean.** `DevState.md` §4 wants `ct_2026.json` + a snapshot → an engine
  input byte-identical to `data/dvChod.txt`, *needing no DLL*. Every emitter test now drags an
  x64-pinned assembly whose only purpose is P/Invoke.
- **`O1` is not actually deferred.** "The interface (`ICalcEngineRunner`) makes it a swap either way"
  requires the interface on the **consumer** side. `DNB.Engine` holds a hard reference to the
  implementation, so no swap is available.
- **`N3` has nowhere to be expressed.** "The engine runner must be reentrant-from-a-stage by design —
  this constrains `B21`'s interface from the first line of code" (`DNB_overview.md` §5.3). That
  interface does not exist.

**Fix.** `ICalcEngineRunner` is declared in `DNB.Engine`; `DNB.Calc` implements it and references
`DNB.Engine`. The composition root (`DNB.Online` / `DNB.Wpf`) wires the concrete runner.
`DNB.Engine → DNB.Model` only — SCADA-free *and* native-free.

### P2 — the calc cache does not belong in `DNB.Model`

`DNB_overview.md` §4 places "elements, scheme, geometry, connections, **calc cache / CycleState**" in
one assembly, whose stated purpose is that "the compiler enforces invariant 1 rather than discipline".

It does not. The compiler enforces nothing *within* an assembly. Nothing prevents a `LiveValue`
property appearing on an element class — which is precisely the leak the rule exists to stop. The
rule is currently as much a matter of discipline as it would be with no split at all.

**Fix.** `DNB.Model` = the immutable schema document, nothing else. The calc cache moves to
`DNB.Engine` — it is per-run state of a calculation, and the offline service needs it on the same
terms. Invariant 1 then holds by construction.

### P3 — `IEngineClient` has no home, and `N10` cannot be built

`N11`: WPF and the Web API are both written against `IEngineClient`.
`N10`: "The Web API holds **no engine**."

With today's graph the interface can only live in `DNB.Online`, so `DNB.WebApi → DNB.Online` pulls the
SCADA client, lib60870, the cycle and the native engine into the tier that is specified to hold none
of them. The two decisions contradict each other at the project level.

**Fix.** A contracts assembly referencing nothing — `IEngineClient`, `EngineEvent`, the state
projections — referenced by `DNB.Online` (implements), `DNB.Wpf`, `DNB.WebApi`, and by both sides of
the gRPC transport later. **DECIDE:** name (`DNB.Contracts`) and whether the projections are records
or interfaces.

### P4 — `DNB.Online`'s redundant references, one of which matters

`DNB.Online → Model, Calc, Scada, Engine`; `Model` and `Calc` both arrive transitively through
`Engine`. Dropping them is not cosmetic: keeping `Calc` lets the cycle call the DLL directly and
bypass the kernel, which would leave the offline service reusing a kernel the online loop does not
actually go through. **`DNB.Online → Engine, Scada, Contracts`.**

### Target graph

```
DNB.Model      → —                       immutable schema document only
DNB.Iec104     → lib60870                protocol codec, both directions
DNB.Contracts  → —                       IEngineClient, EngineEvent, projections
DNB.Engine     → DNB.Model               kernel: SCADA-free AND native-free
DNB.Calc       → DNB.Engine              implements ICalcEngineRunner        ⇦ inverted
DNB.Scada      → DNB.Iec104              client + address-keyed buffer
DNB.Online     → DNB.Engine, Scada, Contracts
DNB.Wpf        → DNB.Contracts (+ Online, Calc at the composition root only)
```

---

## 2. Value representation

### P5 — uniform `double` erases the domain

`Element104.Value` is a `double` (`../../DNBridge/src/DNBridge/Elements/Element104.cs:13`) and
`ExtractValue` widens all 18 TypeIDs into one
(`../../DNBridge/src/DNBridge/Scada/ScadaClient.cs:582-690`). That is a fossil of TLV middleware which
had to be type-agnostic to shovel values at DNC. DNB has no such requirement.

A switch state is a 4-valued DPI enum; a tap position is a small signed int plus a transient flag;
`Active` is a bool. Storing `2.0` and later writing `if (v == 2)` is unchecked, and both the topology
pass (`B5`) and the gate (`B11`) branch on switch states — a misread there is silent.

**The C++ shows exactly where this leads.** Its own validity check for "switch position is
INDETERMINATE" is a floating-point comparison, `value > 2.05`
([`L2_dncors_input_filter.md`](../../DNC/Doc/Analysis_from_Evlivy3/ai/L2_dncors_input_filter.md) §5,
check 3, `DB_104.cpp:455-463`) — a threshold standing in for an enum case, because by then the enum
was gone. Do not port that shape.

**Fix — a discriminator, not a class hierarchy.** These values arrive thousands of times a minute;
virtual dispatch and per-point allocation are the wrong answer. The problem is not the `double`
*storage*, it is the missing *tag*:

```
readonly record struct Iec104Value
    Iec104ValueKind Kind      Single | DoublePoint | Step | Normalized | Scaled | Float | Undecodable
    double          _raw
    byte            _flags    transient, ...
    AsSingle() / AsDoublePoint() / AsStep() -> (int Tap, bool Transient) / AsMeasurement()
```

One struct, no allocation, ~24 bytes — but reading forces the caller to name the domain, so a switch
state cannot be silently consumed as a measurement. An accessor mismatch throws: a point bound as
`M_ST_NA_1` in `<scheme>.scada.json` and read as a measurement is a binding bug, and it should
surface on the first tick rather than as a wrong tap.

`double` remains correct in the **calc cache** after `B9`, where values have been through binding,
`nasobitel` and the sign convention and are genuine physical quantities (MW, MVAr, kV). Even there,
tap position and switch state stay typed fields — they are not physical quantities and the emitters
treat them differently.

### P6 — the transient bit is discarded

```csharp
case TypeID.M_ST_NA_1:
{
    var v = (StepPositionInformation)io;
    return (v.Value, v.Quality.EncodedValue, DateTime.UtcNow);   // v.Transient dropped
}
```
`../../DNBridge/src/DNBridge/Scada/ScadaClient.cs:611-615` (and `:616-620` for `M_ST_TB_1`).

`StepPositionInformation` carries `Value` **and** `Transient` (`libs/lib60870/CS101/StepPositionInformation.cs:103`).
Transient means *the tap changer is mid-travel*, which is exactly the condition under which a new tap
setpoint must not be computed. It cannot be represented in a `double`, so it was thrown away, and the
gate has no way to see it.

**Proposal on the table: map transient → the IEC quality invalid bit (`0x80`) at decode time.**

*Verdict: right behaviour, wrong layer.* In its favour — the C++ filter's first check is exactly the
`0x80` bit (`Filter_104`, `DB_104.cpp:417-425`), so the condition needs no new gate concept to become
fatal, and for a **tap/branch** point an invalid reading is fatal directly without consulting
`mapFilter` (`DNCoRS_Filter.cpp:275-282`). The intended effect lands with zero new plumbing.

Against — it is lossy in a second direction. `IV` also arrives from SCADA meaning telemetry failure or
comms loss. Merging them makes "the tap changer is moving, as commanded" indistinguishable from "this
point is broken": the log, the failed-cycle footer and the WPF grid would all report `invalid data`
for a healthy grid doing what DNB just told it to do, and a transient resolves in seconds while a
genuinely dead point may never resolve. Folding at decode time is a one-way door — no later consumer
can recover the distinction.

**Fix.** Carry `Transient` as its own bit on `Iec104Value` (P5 reserves it), and let the **gate**
treat it as fatal on tap/branch points exactly as it treats `IV`. Decode preserves; policy decides.
The C++ already emits a distinct reason string per check and captures it via `AddCalcErr` for the
snapshot footer, so a new reason — `tap in transit` — costs nothing and makes the footer say
something true.

**DECIDE:** whether transient is fatal for the *whole* cycle. Invariant 3 is all-or-nothing, and DNB
itself commands the tap changes — so a tick landing during a commanded tap change would skip the
cycle. Probably correct, but it should be a conscious choice, and worth measuring against the
recorded dumps before `B11` freezes.

### P7 — quality `0` is overloaded

`ExtractValue` returns quality `0` for the four control types and for `M_ME_ND_1`
(`ScadaClient.cs:636`, `:669`, `:674`, `:679`, `:684`), where `0` also means "SCADA says good".
`CarriesQuality` (`:570-575`) exists solely to un-say it afterwards, and the unknown-type default
returns a bare `0x80` (`:688`) that is indistinguishable from a genuine SCADA invalid.

`B11` must tell "SCADA asserts this is valid" from "this frame carries no validity statement".

**Fix.** `Iec104Quality` wraps the encoded byte (`IV/NT/SB/BL/OV`) with an explicit `None` case for
types that carry no descriptor, and `Iec104Value.Undecodable` replaces the `0x80` sentinel.

### P8 — one timestamp field, two meanings, wrong clock

```csharp
case TypeID.M_SP_NA_1:  return (…, DateTime.UtcNow);         // when WE received it
case TypeID.M_SP_TB_1:  return (…, Cp56ToUtc(v.Timestamp));  // when SCADA SAMPLED it
```
`ScadaClient.cs:590` vs `:595` — and the same pattern for every untimed/timed pair through `:685`.

`Element104.LastDataTime` (`Element104.cs:15`) therefore means different things for different points,
selected by whether the TypeID happens to carry a tag. A freshness check compares apples to oranges
for half the point set. It also compares against `DateTime.UtcNow`, which steps with NTP — the C++
equivalent is the `m_dtcasovy_limit` window
([`L2_dncors_input_filter.md`](../../DNC/Doc/Analysis_from_Evlivy3/ai/L2_dncors_input_filter.md) §5,
check 2).

**Fix.** Two fields, two instruments. `SourceTime` — nullable, from the CP56 tag, evidence about the
grid. `ReceivedAt` — monotonic (`Environment.TickCount64`), ours, never null. Freshness uses
`ReceivedAt`; `SourceTime` is data, and its absence is information rather than a value to fabricate.

---

## 3. Concurrency

### P9 — buffer slot updates are neither atomic nor published

```csharp
elem.Value        = value;
elem.Quality      = quality;
elem.LastDataTime = timestamp;
elem.Iec104Type   = (byte)asdu.TypeId;
```
`ScadaClient.cs:501-504`, running on a lib60870 worker thread. `Element104`'s fields are plain and
non-volatile (`Element104.cs:13-17`), and `_cache.Find(addr)` (`:495`) hands out the **live mutable
object**, not a copy.

Three distinct defects:

1. **Torn slot.** The tick thread can read `Value` from update *N* and `Quality` from update *N+1* —
   a pair that never existed on the wire. The dangerous direction is a fresh value carrying a stale
   `good` quality, or a stale value carrying a fresh timestamp, which is exactly what defeats the
   freshness check in P8.
2. **No publication.** Nothing orders or publishes those writes, so the tick thread has no guarantee
   of observing them. (The `double` write itself is atomic on x64 by alignment — incidental, not
   designed.)
3. **The slice shares references.** Handing out the live object means "freeze the buffer into the
   calc cache" (`DNB_overview.md` §3, invariant 2) does not actually freeze anything unless the slice
   copies values out.

**Fix — one lock, two call sites.** A single lock per buffer: the writer holds it across the whole
slot update; the slice holds it once across the entire copy loop. Cost is negligible — a few thousand
updates per minute against one copy per minute, and the copy is microseconds — and it buys the strong
guarantee, not just the per-point one.

Deliberately **not** lock-free. An immutable-sample-plus-`Volatile.Write` design avoids the lock but
only gives per-*point* atomicity: the slice would still read point 3 and point 400 at different
instants, so invariant 2's "one frozen slice" would remain aspirational. Holding the lock for the
copy makes the invariant literally true. Revisit only if profiling ever shows contention, which at
this rate it will not.

Two rules that go with it:

- **Never raise `ElementsUpdated` while holding the lock** — collect the updates, release, then raise.
  Handlers are host code and can do anything, including re-entering the buffer.
- **The calc cache needs no locking at all.** It is built by the tick thread and read by the stages on
  that same thread; the slice copy is the only handoff. State this where the cache is defined so
  nobody adds defensive locks that obscure the design.

Note that lib60870 delivers ASDUs on one worker thread per connection, so there is effectively a
single writer today — but the supervisor replaces connections (`ScadaClient.cs:245-272`), so
single-writer is a property of the moment, not of the design. The lock is what makes it safe;
the threading model is not a substitute.

---

## 4. Infrastructure

### P10 — missing scaffolding that changes the reference graph

- **No test project anywhere.** For a plan whose proofs are *byte comparison against `data/*.txt`* and
  *round-trip*, that is structural, not cosmetic. `tests/DNB.Iec104.Tests` (codec round-trip over all
  18 TypeIDs) and `tests/DNB.Engine.Tests` (the emitters) are needed as soon as either assembly has
  code; `data/*.txt` becomes a test asset.
- **No `global.json`.** The SDK version (10.0.203) is pinned in `DevState.md` prose while the repos are
  synced across machines. Pin it in the file that enforces it.
- **No `.editorconfig`**, and `EnforceCodeStyleInBuild=false` (`src/Directory.Build.props`). With an
  agent writing much of this code, that is the cheapest consistency available.
- **`PlatformTarget` scattered.** Declared on `DNB.Calc` and `DNB.Wpf` only, while `DNB.Engine` and
  `DNB.Online` inherit x64 transitively and implicitly. The product is x64-only because the DLL is —
  set it once in `src/Directory.Build.props`.

### Also settled while reviewing

`DNB.Iec104` is a 6th library, one more than `DNB_overview.md` §4 / `N6` describe
(`DevState.md` §1 flags it as unresolved). **Keep the split** — the codec must be referencable by the
standalone SCADA simulator (`N-g`), and the simulator is what exercises the encode half that no
production path touches. Fold it into §4 rather than dropping it.
