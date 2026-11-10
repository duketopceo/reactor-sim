# Simulation verification strategy

> **Scope.** ReactorSim is educational entertainment. Nothing here is safety analysis,
> a licensed-operator-training claim, or ANSI/ANS (or any other) certification.
> Replay and regression checks show *the code still does what it did*; they never show
> that the physics is *right*. Those are different claims and this document keeps them apart.

Companion docs: [observability](../development/observability.md),
[architecture](../ARCHITECTURE.md).

## 1. Run it

```bash
dotnet build
dotnet run --project ReactorSim.Runner                       # existing scenario suite (unchanged)
dotnet run --project ReactorSim.Runner -- --verify           # determinism / numerical / edge-case checks
dotnet run --project ReactorSim.Runner -- --verify --check NUM   # id-prefix filter
dotnet run --project ReactorSim.Runner -- --list             # discover checks + deferred items
```

`--verify` exits **0** when there are no unexpected results and **1** otherwise (verified by
temporarily breaking a check). `scripts/ci.sh` builds, runs the scenario suite and `--verify`, and
returns non-zero if any step fails. Code: `ReactorSim.Runner/Verification.cs`. No test-framework
dependency was added; the harness reports `PASS`, `FAIL`, `XFAIL`, `XPASS`.

* **PASS / FAIL** — contract held / violated.
* **XFAIL** — a *known defect* on current master is reproduced. It is documented below with a
  stable ID (`KD-nn`), does not fail the run, and is printed with the observed field values.
* **XPASS** — a known defect stopped reproducing. This **fails** the run so the marker and the
  docs get updated rather than rotting.
* **Deferred** — feature absent on master (see [§7](#7-deferred-not-passing)); listed by `--list`, never counted as passing.

## 2. Five kinds of confidence (do not mix them)

| Kind | Question | Evidence | Tolerance | Status |
|---|---|---|---|---|
| Determinism / reproducibility | Same build + same inputs → same state? | Hash/bit equality | **exact** (same build only) | `DET-*` implemented |
| Numerical verification | Does the code solve its own equations accurately? | Analytic benchmarks, convergence, conservation residuals | relative error, observed order | `NUM-*`, `DOM-*` implemented (partial) |
| Physical validation | Do the *equations/coefficients* match reality? | Public reference data, offline codes | uncertainty-based | **not done**; calibration candidates only |
| Gameplay validation | Is it fun, readable, recoverable? | Playtests, scripted missions | pass/fail by design goals | not done |
| Performance budgets | Does it fit in frame/tick budget? | Timed benches on pinned hardware | budget | not done (no number invented) |

## 3. Determinism contract

What "deterministic" means here, and what is **not** promised:

1. **Pinned environment.** Same assembly build (hash recorded), same .NET runtime, same OS/CPU
   architecture. **Cross-platform bitwise floating-point identity is not promised.** `Math.Exp`,
   `Math.Pow`, `Math.Sin`, `Math.Log` come from the platform libm/runtime and may differ in the
   last bit across runtimes/architectures. Cross-platform comparison must use tolerances (§6).
   Cross-platform lockstep netcode would need either a fixed-point/own-math core or host-authoritative
   state sync (the architecture's chosen model) — an open decision, not a result.
2. **Complete initial and hidden state.** Hash includes private state that no property exposes:
   `ReactorCore._precursors[0..5]`, `_decayStore[0..2]` (read-only accessors were added to
   `ReactorCore` for this), plus rod, chemistry and thermal state. See `Verification.Capture`.
3. **Ordered, tick-stamped inputs.** Commands are applied between ticks in a defined order.
   Master has no command queue, so tests script direct property sets keyed by tick index.
4. **Seeded, isolated random streams.** Master's Core uses **no random source** (grep for
   `Random` in `ReactorSim.Core` is empty at this commit). When sensor noise/faults arrive,
   each consumer gets its own seeded stream whose state is part of the saved state.
5. **Stable iteration order.** No `Dictionary`/`HashSet` iteration may influence physics.
   Core currently iterates arrays only.
6. **Authoritative simulation clock.** `Simulation.SimSeconds` advances only in `Tick()` by
   `TickDt`; wall-clock and rendering never feed physics (`RealMsPerTick` is pacing metadata).
7. **Process-wide mutable state is part of the contract.** `Physics.PoisonTimeWarp` (static,
   mutable) and `Physics.SanitizeHits` (static counter) are global. The former is hashed; the
   latter is diagnostic and excluded from state equality. Two simulations in one process share
   both. `DET-02` interleaves runs to catch accidental sharing of other state.
8. **Canonical hash.** Fields are enumerated in a fixed order as `path=<IEEE-754 bits, hex>`,
   then SHA-256. Wall-clock, log text, and diagnostic counters are excluded.
9. **Same-build equality vs physical tolerance.** Replay equality is *exact*. Comparisons
   against analytic or reference values are *tolerance-based* and live in separate checks.

### Fixed tick ≠ accurate

`Simulation.TickDt` is 5 s (`SecondsPerGameMinute / TicksPerGameMinute`). Core kinetics now
sub-steps power *and* precursors at 0.02 s and the plant step at 0.2 s, so the 5 s tick is converged
(NUM-03/04; KD-01 is fixed). `Plant.Step` rejects NaN, negative and > 3600 s dt. A future solver may use
implicit/adaptive sub-steps; the schedule (number and size of sub-steps) must be a deterministic
function of state and `dt`, and recorded in the journal ([observability](../development/observability.md)).

## 4. Implemented checks (what they actually exercise)

| ID | Kind | What it checks (real `ReactorSim.Core` types) | Result on master |
|---|---|---|---|
| DET-01 | determinism | Two fresh `Simulation`s, scripted 300-tick start-up + SCRAM; SHA-256 of canonical state equal **every tick**; failure names first differing field path | PASS |
| DET-02 | determinism | Interleaving two different scenarios tick-by-tick equals isolated run | PASS |
| DET-03 | determinism | `RunGameMinutes(10)` equals ten `RunGameMinutes(1)` | PASS |
| DET-04 | determinism | Hash is not trivially constant: late input change, SCRAM change the final hash; hidden fields present | PASS |
| NUM-01 | numerical | Σβᵢ-fractions = 1, λᵢ positive/ascending, β, Λ ranges, I/Xe half-lives implied by constants | PASS |
| NUM-02 | analytic | Fixed ρ = −1000 pcm, source only: `P → S/|ρ|` (1e-4) and `Cᵢ = βᵢP/(Λλᵢ)`, dt = 0.1 s, 1 % | PASS |
| NUM-03 | convergence | Kinetics at t = 120 s is independent of the caller's dt (0.1–6 s, includes the 5 s production tick) vs a dt = 0.0125 s reference, rel. err ≤ 1e-4 (`StepKinetics` substeps at 0.02 s) | PASS |
| NUM-04 | numerical | Same benchmark at the production tick 5 s | PASS (KD-01 fixed) |
| NUM-05 | conservation | `ThermalHydraulics.Step`: ΔE(fuel+primary) = (Q − q_amb − q_SG)·dt to 1e-9 of throughput, with explicit source/sink accounting and no clamp active (the pressurizer heater biases pressure only) | PASS |
| NUM-06 | numerical | Rod integral worth: endpoints, monotone, symmetric | PASS |
| NUM-07 | reference | `SaturationPressureBar` vs 6 IF97 points (0.5 %) + inverse round trip | PASS |
| NUM-08 | accounting | Decay heat equilibrium ≈ 6.3 % of fission power; `TotalHeatPower = PromptHeatFraction (0.937) · Power + DecayHeatPower` | PASS |
| DOM-01 | domain | Saturation call outside the valid domain should fault, not clamp | **XFAIL KD-02** |
| SCR-01 | scenario | From 70 % power: fission collapses to ≤1e-4, rods fully in within one tick, ρ<0, decay heat >0, bounded by ~6.3 % of prior power, monotone decline for 10 game-min, SCRAM stays latched, decay heat > fission at 10 min | PASS |
| SCR-02 | latch | `Scrammed` should block rod withdrawal until `ResetScram()` | **XFAIL KD-03** |
| EDGE-01 | edge | `Plant.Step(0)` advances no integrated state | PASS (see O-1) |
| EDGE-02 | edge | NaN/±Inf `dt` is detected by `AssertFinite` | PASS |
| EDGE-03 | edge | Negative/NaN/Inf `dt` rejected at `Step` | PASS (KD-04 fixed) |
| EDGE-04 | edge | `SetAllRods(NaN)` rejected | PASS (KD-05 fixed) |
| EDGE-05 | edge | A NaN command is rejected, or if accepted at least detected by `AssertFinite` | PASS |
| EDGE-06 | edge | Out-of-range bounded commands saturate (rods to [0,1]; `SteamDemandFrac` 5.0 ≡ 1.0) | PASS |
| EDGE-07 | edge | Zero primary flow finite; less flow never gives a lower primary temperature | PASS |
| EDGE-08 | edge | Loss of heat sink at power: finite, but should not sit on a model clamp | **XFAIL KD-06** |

Fixture provenance: NUM-07 values are IAPWS-IF97 saturation points (`Physics` now implements the
IF97 Region 4 closed form exactly; it was an Antoine fit, 0.76 % low at 300 K), cross-checked on 2026-11-07
with the `iapws` Python package (GPL-3.0, used only as an offline oracle; no code or data files from it are in the repo).
NUM-02/-03 reference values are derived analytically in the check, not hard-coded outputs.

### Observed notes and known defects (reproducible with `--verify`)

| ID | Observation (field paths) | Contract / impact | Disposition |
|---|---|---|---|
| **KD-01** | `ReactorCore.StepKinetics`: `_precursors[g] += (src − λ_g C)·dt`, explicit Euler. At `dt = 6 s`, λ₆·dt = 18; stability needs λ·dt < 2. Subcritical-multiplication benchmark gives `Core.Power` 1.696e-4 vs analytic 1.0e-4 (+70 %), `_precursors[5]` pinned at 0 by the `<0` clamp. Dt ≤ 0.5 s is correct to 1 %. | Delayed-neutron dynamics are wrong at the production tick; all scenarios were tuned on it | **Fixed**: `StepKinetics` now substeps at 0.02 s, so the 5 s tick is converged (NUM-03/04). |
| **KD-02** | `Physics.SaturationPressureBar(700 K)` returns 220.64 bar; `SaturationTempK(500 bar)` returns 647 K, no fault | Invalid domain silently clamped | Deferred; needs a model-limit fault type |
| **KD-03** | After `Plant.Scram()`, `SetAllRods(0)` withdraws all banks in one tick at the 50 %/s scram speed; only the Runner's `TrimPower` guards `Scrammed` | Trip is not latched in Core | Deferred; belongs with protection/intents |
| **KD-04** | `Plant.Step(dt)`: `-6` runs time backwards (`Banks[3].Insertion` can exceed 1, precursors change); NaN/Inf make rods jump to target (`Physics.Clamp` passes NaN) and trigger `SanitizeHits`; no rejection | Should be rejected, not applied | **Fixed 2026-11-07**: `Plant.Step` throws `ArgumentOutOfRangeException` for NaN, negative or > `Plant.MaxStepSeconds` (3600 s) dt before mutating state. Also fixes a hang: `(int)Ceiling(+Inf)` saturates to `int.MaxValue` substeps on .NET 9+/ARM64 |
| **KD-05** | `Plant.SetAllRods(NaN)`: `Physics.Clamp` maps NaN to the lower bound, so a NaN rod target meant **fully withdrawn** (silently) | Reject at the boundary | **Fixed 2026-11-07**: `SetAllRods` throws `ArgumentOutOfRangeException`; `Simulation.EnqueueIntent` throws `ArgumentException` for non-finite intent values (HTTP API → 400, co-op host drops and logs) |
| **KD-06** | Loss of sink from 50 % power: `Thermal.PrimaryTempC` reaches the hard clamp 360 °C (FuelTempC ≈ 684 °C, `PrimaryPressureBar` ≈ 188 bar in the observed run); `Thermal.Step` clamps silently | No controlled model-limit fault; this is where meltdown/damage logic will have to live | Deferred (README: damage model not implemented) |
| O-1 | `Plant.Step(0)` still refreshes derived ledger terms (`Core.DopplerPcm`, …) from current temperatures: the ledger lags the thermal state by one tick (operator splitting) | Documented, excluded from EDGE-01 | Design note |
| O-2 | At `P = 1`, `DecayHeatPower/Power ≈ 0.063` and `TotalHeatPower = 1.063` → thermal model receives 106.3 % of `RatedThermalKW` at "rated" fission power | Unit-convention question: is "rated" fission-only or total? | Calibration question; no coefficient changed |
| O-3 | Poison equations use normalised power as flux (no flux-per-power conversion) and a static `PoisonTimeWarp` = 200 | Documented gameplay compression; must be explicit in tuning data | Design note |

No physical coefficient was changed to make a check pass. Core changes were limited to
four read-only accessors (`GetPrecursor`, `GetDecayStore`, group counts) so hidden state can be hashed.

## 5. Test taxonomy (what to add, in priority order)

* **Unit** — pure functions (`ReactivityPcmAt`, `SaturationPressureBar`, `Chemistry.BoronWorthPcm`).
* **Analytic benchmark** — NUM-02 style: closed forms (subcritical multiplication, prompt jump
  `P₁/P₀ ≈ β/(β−ρ)` for 0<ρ≪β, inhour period, exponential decay of a group, Bateman I→Xe,
  lumped RC cooling `T(t)=T∞+(T₀−T∞)e^{−t/τ}`).
* **Convergence** — halve `dt` (and sub-step count) against a fine reference; report observed order
  and the stability limit, not just "no NaN".
* **Conservation residual** — NUM-05 style, with *every* boundary named: source (core power, heater),
  sinks (SG, ambient), storage (fuel/primary Cp·T). Residual = ΔStorage − ∫(sources − sinks). Mass:
  once inventories exist, ΔM = ∫(ṁ_in − ṁ_out). Shared fluxes between nodes must cancel exactly.
* **Property / invariant** — over random inputs and seeds: finite state; `0 ≤ insertion ≤ 1`;
  concentrations ≥ 0; decay heat never negative; energy residual below tolerance; hash(replay)=hash(run).
  (For .NET: [CsCheck](https://github.com/AnthonyLloyd/CsCheck) Apache-2.0 or [FsCheck](https://github.com/fscheck/FsCheck) BSD-3-Clause, both inspected 2026-11-07; neither added now to keep the harness dependency-free.)
* **Metamorphic** — relations without an oracle: more boron → lower power at fixed rods; doubling
  `dt` with convergent solver changes results within the error budget; scaling rated power and
  heat capacity together leaves temperatures unchanged; time-shifting a scenario leaves it unchanged.
* **Equilibria and hysteresis** — steady states stay steady; trip set/reset thresholds have a dead band;
  latched trips stay latched until an explicit reset (SCR-02 is the first such test).
* **Command ordering/duplicates** — same command twice = once (idempotent set-points) vs twice
  (incremental commands); two commands in one tick resolve in a defined order. Needs a command queue.
* **Scenario / regression** — the existing `--scenario` suite. Golden values must record provenance
  (who/when/which build produced them and why they are believed) in the fixture header.
* **Save/replay** — snapshot at tick *k*, restore, run to *n*; hash equals uninterrupted run. Replay of the
  input journal from t=0 equals the live run.
* **Render independence** — run headless and with the front end attached; hashes equal.

## 6. Edge-case matrix

Disposition vocabulary: **reject** = input refused with an explicit error and no state change;
**physical** = the model handles it as physics; **fault** = explicit controlled *model-limit fault*
(visible to developers, mapped to a gameplay consequence). **Saturate** is allowed only for physically
bounded controls (a rod cannot be inserted past 100 %) with documented semantics and a test.
Invalid numbers are never silently clamped to make a run look healthy.

| Case | Required disposition | Master status |
|---|---|---|
| dt = 0 | no-op on integrated state | tested EDGE-01 ✔ |
| dt < 0, NaN, ±Inf | reject | `ArgumentOutOfRangeException` at `Plant.Step` (EDGE-02/03) |
| Invalid command value (NaN/Inf) | reject | `SetAllRods` / `EnqueueIntent` throw (EDGE-04/05) |
| Out-of-range bounded command | saturate, documented | tested EDGE-06 ✔ |
| Zero flow | physical (heat removal drops) | tested EDGE-07 ✔ (floor `sgEff = 0.15` is a modelling choice, documented) |
| Zero/full tank, flow reversal | physical/fault | deferred: no inventories or network |
| Pump trip/coastdown | physical (inertia) | deferred: no pump model |
| Stuck valve/rod | fault injection | deferred: no actuator fault model |
| Sensor bias/dropout/disagreement | physical sensor layer + alarm | deferred: no sensor layer |
| Controller saturation/anti-windup | saturate + tested | deferred: no in-Core controller |
| Threshold chatter | hysteresis | deferred: no trips |
| Shutdown with decay heat | physical | tested SCR-01 ✔ |
| Cold start / low-power log instrumentation | physical; log-scale instrument with floor | partially: startup scenario; source floor `SourcePower` = 1e-6 |
| I/Xe buildup after power change | physical | existing `xenon_pit` scenario; no analytic fixed-power test (needs a test hook) |
| Loss of cooling | physical → fault at model limit | **KD-06** |
| Invalid thermodynamic domain / phase boundary | fault | **KD-02**; liquid-only primary assumption not enforced |
| Simultaneous commands | defined order | deferred: no queue |
| Pause / time acceleration | pause = no ticks; acceleration = more ticks, never a different `dt` | deferred; DET-03 covers chunking |
| Long-run drift | bounded drift/residual | existing `soak` (finite only); residual-based drift check deferred |
| Save corruption / version migration | reject with reason | deferred: no save format |
| PRNG restoration | stream state in snapshot | deferred: no PRNG |
| Solver nonconvergence | fault with residual | deferred: no iterative solver |

## 7. Deferred, not passing

`DEF-SAVE, DEF-PRNG, DEF-CMD, DEF-TRIP, DEF-SENSOR, DEF-CTRL, DEF-PUMP, DEF-TANK, DEF-SOLVER,
DEF-PAUSE, DEF-RENDER, DEF-PERF` — printed by `--list` with the reason each is blocked. They are
placeholders for future work and are **not** counted in the pass total.

## 8. Physical validation path (not done)

Calibration candidates, each needing a cited public reference before any claim: delayed-neutron
data (Keepin), I-135/Xe-135 yields and half-lives (public nuclear data libraries), decay-heat curves
(public standards/data), saturation properties (IAPWS-IF97), reactivity coefficients (generic textbook
ranges only — game-tuned values in `Physics.cs` are labelled "game-tuned" and are **not** universal).
High-fidelity codes (e.g. [OpenMC](https://github.com/openmc-dev/openmc), MIT-style license) are offline
benchmark sources, not runtime components.
