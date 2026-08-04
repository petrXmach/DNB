# Main104 input parameters in the DNC voltage-regulation calculation

> **Migration reference.** Traced against the C++ original in `../DNC` (DNCalc / EVlivy3).
> Purpose: for Stage-1 migration, DNBridge will build the Bodor text inputs and run the
> an3f4w calc itself, then take the control decision. This doc records **where each of the 7
> Main104 regulation INPUT parameters is used** — split into what shapes the **DLL text
> input** vs. what drives **C++ control logic** — and how the staged `Calc_Kind` sequence
> works (esp. the production kind 9).
>
> Line refs are into `../DNC/DNCalc/EVlivy3/` (`cl_104_Connector.cpp`, `cl_OperCalc.cpp`,
> `DNCoRS_Data.cpp`) as of this analysis. Related: [Main104_Elements_Analysis.md](Main104_Elements_Analysis.md).

---

## 1. The calc model — stages are independent DLL runs; control is separate C++

The regulation loop `cl_104_Connector::Perform_Calculation()` (`cl_104_Connector.cpp:1144`)
walks a 32-bit sequence word 4 bits at a time; each nibble is one stage
(`cl_104_Connector.cpp:36-45`):

| Stage | Nibble | What it is |
|-------|:-----:|------------|
| `DO_CALC_OPER` | 1 | operational load-flow (dvchod) |
| `DO_CALC_SPLIT` | 2 | PQ split + voltage-band check (`Check_dU`) |
| `DO_CALC_OPTIMIZE` | 3 | regulation optimization (produces Uopt / tap) |
| `DO_CALC_QMIN` | 5 | reactive-power lower envelope |
| `DO_CALC_QMAX` | 6 | reactive-power upper envelope |
| `DO_CALC_LOSS` | 7 | system losses |
| `DO_CALC_CONTROLL` | 10 | **C++**: send results (pokes) to SCADA |
| `DO_CALC_SAVE` | 15 | persist results |

**Every compute stage** (OPER/OPTIMIZE/QMIN/QMAX/LOSS) constructs its own `cl_OperCalc`,
builds a Bodor **text** input buffer (`m_szInpData`), writes it to `InpData_*.dat`, and calls
the an3f4w DLL:

```
cl_OperCalc::Calculate()            cl_OperCalc.cpp:227
  → PrepareData()                   builds m_szInpData (text)   :242
  → Do_Calculate()                  runs the an3f4w DLL         :246
  → ProcessResult()                 reads DLL outputs           :250
```

**CONTROLL is not a DLL call** — it is pure C++ that reads the computed results and decides
what to poke. So the answer to "do params drive C++ or shape the DLL text input?" is
**both, in different places**:

- **DLL text input** — params become Bodor keywords, but **only in the OPTIMIZE stage**
  (`m_CalcStage == sg_CalcReg`). QMIN/QMAX/LOSS override them with fixed constants; OPER uses
  none of them.
- **C++ control** — params gate the CONTROLL dispatch (post-DLL), no DLL involvement.

---

## 2. Calc_Kind sequences and the production kind 9

`Calc_Kind` (config `/Config/Calc_Kind`) is decoded into the sequence word at
`cl_104_Connector.cpp:327-367`. Selected kinds:

| Kind | Sequence (execution order) |
|-----:|----------------------------|
| 1 | OPER |
| 3 | OPTIMIZE |
| 8 | QMIN → QMAX → LOSS |
| **9** | **QMIN → QMAX → LOSS → OPTIMIZE → CONTROLL** |
| 19 | SPLIT → QMIN → QMAX → LOSS → OPTIMIZE → CONTROLL |
| 21 | OPER → CONTROLL |

**Kind 9 (production) does not run OPER or SPLIT.**

### What OPER does, and why control doesn't need it
OPER (`cl_104_Connector.cpp:1301-1343`) builds a `cl_OperCalc` with
`RES_CALC_RUN | RES_CALC_ADJUST` and **does not set `m_pDNCoRS_Data`** — so it uses none of
the 7 params. It runs a plain operational load-flow and calls
`SaveResultValues(&OpCalc, pCalcScheme)` to write voltages/flows back into the scheme, but
**that output feeds only display/monitoring** (`ShowResultValues`, saved results). The
control path never consumes OPER's results. Node voltages are reset to 0 at the top of every
cycle (`:1211-1213`) and each stage recomputes from measurements, so omitting OPER costs only
the operational snapshot, not control correctness.

### OPTIMIZE does not depend on OPER
OPTIMIZE (`:1387-1453`) builds its own DLL input from the scheme's measurements/topology and
produces both `m_fUcalc` (operational U → SetU id 8) and `m_fUopt` (optimized U → SetUopt
id 14). The **only** genuine cross-stage dependency is the **rm_MinLoss** Uopt decision,
which compares OPTIMIZE's `cfOptimLosses` against the **baseline loss from the LOSS stage**
(`m_Data.m_cfLosses`). Kind 9 runs LOSS before OPTIMIZE, so it is satisfied — that ordering
is exactly why kind 9 is QMIN,QMAX,LOSS,OPTIMIZE,CONTROLL.

---

## 3. Per-parameter usage

Member map (struct `cl_DNCoRS_Data`, `DNCoRS_Data.h`):

| Param | Member | Meaning |
|-------|--------|---------|
| Active | `m_bActive` | regulation enabled — **NOTE (2026-07-29):** commented out of DNBridge's `XChng.cfg`/`Main104Catalog.cs`; DNCoRS already forces `m_bActive=true` always, so nothing currently drives this to `false`. DNC/DNCors left unchanged. |
| RegMode | `m_RegMode` | `rm_BasicQ=0, rm_MinTransfQ=1, rm_MinLoss=2, rm_None=3` |
| RegBranch | `m_bBrnch_Reg` | tap-changer regulation enabled |
| UNet_max / min | `m_fUnet[1]` / `[0]` | voltage band % |
| Qvvn | `m_fQvvn` | reactive-power target (stored in **VAr**) |
| Q_tor | `m_fQtol` | reactive-power tolerance/deadband (**VAr**) |

### A. Written into the DLL text input — OPTIMIZE only (`cl_OperCalc.cpp`, `if (m_pDNCoRS_Data != nullptr)` at `:547`)

| Param | Text line emitted | Line |
|-------|-------------------|------|
| **RegMode** | `cílová funkce: ZakladniRegQ` / `Priorita_Q` / `MinZtraty_dP` | `:563-580` |
| RegMode (cont.) | `optim.metoda: nereg` if `rm_None` (else `CBus`) | `:583-589` |
| **UNet_min/max** | `meze napětí: %+.2f%%,%+.2f%%` | `:562` |
| **Qvvn / Q_tor** | `zad.hodnota Q: %.2f ±%.2f` (MVAr) | `:598` |
| **RegBranch** | `regulace TR: ano` / `ne` | `:636-639` |
| **Active** | — never written to the DLL input | — |

Notes:
- `rm_MinLoss` also sets `bKomprese = false` (`:574`).
- The transformer list (`odbočky TR`, `napětí TR`) is written **unconditionally**,
  regardless of `m_bBrnch_Reg` — the engine requires them present ("Bodor is lunatic, TR have
  to be present even if not used", `:635-653`). Only the `regulace TR: ano/ne` line depends on
  the flag.

**QMIN/QMAX/LOSS override everything** (`:553-596`): objective forced to `Priorita_Q`,
`meze napětí: -8.5%,+8.5%`, `zad.hodnota Q: -99.0 / 99.0 ±0.1`; LOSS also forces
`optim.metoda: nereg`. So the operator's UNet/Qvvn/Qtol/RegMode do **not** influence the
Qmin/Qmax/Loss DLL runs — those measure the envelope with fixed constraints.

### B. Used in C++ control — CONTROLL stage (`cl_104_Connector.cpp`, post-DLL)

Everything below is inside `if (m_Data.m_bActive)` (`:1584`):

- **Active** (`:1584`, also `:1556`, `:1675`) — master dispatch gate. If false, **nothing is
  poked** (the compute stages still ran).
- **SetU (id 8, `:1586-1593`)** — always poked when active (operational U `m_fUcalc`). No
  param dependency.
- **SetUopt (id 14, `:1595-1643`)** — the decision `bDoSet`:
  - Default (`m_bUopt_Ctrl` off, `:1601`): `bDoSet = (m_fUopt > -0.5) || m_bFirstCalc` — send
    whenever the DLL produced a valid optimum.
  - With `m_bUopt_Ctrl` on (`:1602-1633`):
    - `rm_None` → never (`:1607-1608`)
    - voltage out of band (`!bdU_OK`) or first calc → send (`:1609-1610`)
    - else refine by mode:
      - **rm_MinLoss** (`:1613-1626`): send only if
        `(cfOptimLosses − Loss)/Loss < −m_fDelta_Ploss`
      - **rm_MinTransfQ** (`:1627-1631`): send only if `m_fVirtPQ_Q` outside `Qvvn ± Qtol`
        (`:1630`)
- **SetQ (id 9, `:1644-1648`)** — always poked when active.
- **Set-tap (id 10, `:1652-1670`)** — gated by `m_bBrnch_Reg` (`:1659`); the tap value itself
  was computed under `m_bBrnch_Reg` in `SaveResultValues` (`:2206`).

---

## 4. Calc_Kind-9 caveat — `bdU_OK` and the voltage band

`bdU_OK` is initialized **true** (`cl_104_Connector.cpp:1215`) and is only recomputed by
`Check_dU(pResult, m_fUnet[0], m_fUnet[1])` in the **SPLIT** stage (`:1369`). **Kind 9 has no
SPLIT**, so `bdU_OK` stays `true` for the whole cycle. Consequences with `m_bUopt_Ctrl` on:

- The voltage-band trigger (`!bdU_OK`) never fires → **UNet_min/max has no effect on the C++
  Uopt trigger in kind 9**; it only shapes the OPTIMIZE DLL input.
- For a **Mode=1 (rm_MinTransfQ)** deployment the decision falls through to the `Qvvn ± Qtol`
  deadband (`:1630`) every cycle — which *is* evaluated regardless of SPLIT. So in kind 9 the
  effective Uopt trigger is the **reactive-power deadband**, not the voltage band.

`Check_dU` (`:2239-2278`), if SPLIT is later added: for each result node
`fDelta = (fVoltage − fNomVolt)/fNomVolt·100`; returns false the moment any VN node's `fDelta`
falls below UNet_min or above UNet_max.

---

## 5. Migration implications (building txt + doing the calc in DNBridge)

1. **Reproduce two things, not one.** Per stage: build the same Bodor text sections and call
   the DLL. Then **re-implement the CONTROLL C++ decision logic** (`:1584-1670`) — it is *not*
   in the DLL input and the DLL won't do it for you (the Active gate, the `bDoSet` rules, the
   tap gate).
2. **Only the OPTIMIZE input is parameterized.** For QMIN/QMAX/LOSS emit the fixed constants
   (`Priorita_Q`, `±8.5%`, `±99 ±0.1`, `nereg` for Loss). Only OPTIMIZE (`sg_CalcReg`) gets
   the operator's UNet/Qvvn/Qtol/RegMode.
3. **Active never enters the DLL** — keep it purely as a "should I emit pokes" flag.
   As of 2026-07-29 this gate is moot in DNBridge: Active's input/mirror wiring is
   commented out in `XChng.cfg`/`Main104Catalog.cs`, so there is nothing to port yet —
   revisit if SCADA-driven enable/disable is reintroduced.
4. **RegBranch** — emit `regulace TR: ano/ne`, but still emit the transformer
   `odbočky TR` / `napětí TR` lines unconditionally.
5. **Stage order matters for rm_MinLoss** — LOSS must precede OPTIMIZE (baseline loss). For
   Mode=1 you only strictly need QMIN/QMAX (envelope / `m_fVirtPQ_Q`) before CONTROLL's
   Q-deadband test.
6. **If you keep kind-9 "no SPLIT" behavior** — don't implement a voltage-band C++ trigger;
   replicate `bdU_OK = true` (voltage band constrains only the optimizer via the DLL text; the
   Q-deadband is the live trigger). Port `Check_dU` only if you add SPLIT.
7. **Units** — `Qvvn`/`Qtol` are stored in **VAr**, but the two conversions differ — do not
   conflate them (a 1000x error):
   - **DLL text input** — `/1e6` to **MVAr** (`cl_OperCalc.cpp:601`). The engine wants MVAr here.
   - **IEC104 wire, both directions** — **kVAr**. In: `SetParameter` does `fValue * 1.e3`
     (`DNCoRS_Data.cpp:149,155`). Out: `SendData_to_DRS` does `m_fQvvn / 1.e3`
     (`DNCoRS_Data.cpp:172-173`). The single conversion site is commented at
     `DNCoRS_Data.cpp:140-147`, which flags MVAr-on-the-wire as **historic**.
     So DNBridge's cache holds these in **kVAr** — as do the `XChng.cfg` defaults.
   - The `:1630` deadband comparison stays in **VAr** (self-consistent with `m_fVirtPQ_Q`).

---

## Appendix — key source anchors

| What | File:line |
|------|-----------|
| Main loop / stage switch | `cl_104_Connector.cpp:1144`, `:1292-1299` |
| Stage constants | `cl_104_Connector.cpp:36-45` |
| Calc_Kind → sequence | `cl_104_Connector.cpp:327-367` |
| OPER stage | `cl_104_Connector.cpp:1301-1343` |
| SPLIT stage / `Check_dU` call | `cl_104_Connector.cpp:1345-1385`, `:1369` |
| OPTIMIZE stage | `cl_104_Connector.cpp:1387-1453` |
| QMIN / QMAX / LOSS | `cl_104_Connector.cpp:1456-1542` |
| CONTROLL stage | `cl_104_Connector.cpp:1545-1678` |
| `bdU_OK` init | `cl_104_Connector.cpp:1215` |
| `Check_dU` impl | `cl_104_Connector.cpp:2239-2278` |
| DLL calc entry | `cl_OperCalc.cpp:227-250` |
| Text-input builder (params) | `cl_OperCalc.cpp:547-653` |
| Param defaults / ini load | `DNCoRS_Data.cpp:27-68` |
| Poke-out at connect | `DNCoRS_Data.cpp:156-166` |


How RegMode selects behavior (the heart of it)

In the optimizer input (cl_OperCalc.cpp:563-588) RegMode picks the objective keyword:
- rm_BasicQ (0) → ZakladniRegQ
- rm_MinTransfQ (1) → Priorita_Q
- rm_MinLoss (2) → MinZtraty_dP (also disables "komprese")
- rm_None (3) → optim.metoda: nereg (optimizer does no regulation)
