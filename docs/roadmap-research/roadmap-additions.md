# reactor-sim roadmap additions

Researched 2026-11-25. Six comparable repos deep-dived in parallel. Full per-repo notes live in `research/`. Nothing here is a release promise. Items match the phase structure in `ROADMAP.md`:

- Phase 1: trustworthy plant (implementation complete, gate open)
- Phase 2: player and first walkable plant route
- Phase 3: shared plant, visible crew
- Phase 4: radiation and controlled access
- Phase 5: field operations and maintenance
- Phase 6: plant campus
- Phase 7: operations, progression and assistant
- Phase 8: release hardening

How to read each item: **Research** means study, spike, or validate before building. **Implementation** means build it. Priority is **now** (do in the current or next phase of work), **next** (do in its named phase when reached), or **later** (park until the named phase is in scope). Phase placement uses the roadmap's own phases, not the phase numbers guessed in the per-repo notes.

---

## 1. Nucleares (closed source, commercial benchmark)

Site: https://www.nucleares.net. Steam: https://store.steampowered.com/app/1428420/Nucleares. Solo dev, Unity, $14.99, released Mar 2023, Very Positive, 84% of 1,092 reviews. Full PWR game: 200+ parameters, 150+ switches, 9 fuel blocks, 72 control rods, walkable plant, chemistry, xenon, fuel handling, maintenance, daily demand curve, AI assistant "AO", and a public web API on localhost:8785 that spawned a C# PID controller community and a Python RL library (NuCon, Gymnasium envs, ~290 observations, ~30 actions).

Deep-dive summary: its real achievement is coupling. Rods, boron, resin, xenon, fuel, demand, and maintenance all interact, so a small mistake cascades and a fix costs time, travel, and money. Chemistry is its best loop and its worst-documented one: boron is a slow reactivity lever (stable band about 3,300-3,500 ppm), the ion-exchange column is a consumable whose resin capacity depletes and whose regeneration is a manual multi-valve NaOH flush in a physical room, waste fills a tank drained through maintenance tasks, and chemicals arrive by delivery truck with intake valves to set correctly. pH and corrosion are claimed officially but no public source gives a number, a rate, or a named failure. Faults are systemic (integrity degrades to failure to cascade) but feel random to new players, with 39 simultaneous alarms and no diagnostic path. The public API is the biggest steal: flat `SYSTEM_THING_DETAIL` names, ordered vs actual pairs for every actuator, `LIST` (stable) vs `VIEW` (unstable) variables, `BATCH_GET`, and gated `FUN_*` fault triggers. The RL community had to learn a surrogate model of the game because Nucleares is real-time and single-instance. Update cadence was frequent small patches Jan-Jul 2026, then a four-month public gap. Co-op was removed Jan 2024 and rebuilt experimentally as "Fusion 2.0" in 2026, still desyncing. A paid DLC sells faster AO speed, which reads as pay-to-win in review aggregations.

### Research

- R-N1. Read the NuCon Gymnasium env definitions and the NuclearesController PID rules to fix reactor-sim's own API surface: which variables a controller needs, what ordered vs actual means for each actuator, what a goal-conditioned env observes. Phase 1. Priority: now.
- R-N2. Study Nucleares' chemistry loop from the community tutorial and threads, and write down what reactor-sim's chemistry must show to be playable: boron stable band, resin capacity gauge, regeneration steps, waste volume, supply lead time. Decide which of those exist in Phase 1 and which wait for Phase 5. Phase 1. Priority: now.
- R-N3. Study the demand-curve plus shutdown-request penalty design: hourly demand shape, daily energy goal, prestige income per MWh, fuel and maintenance costs. Publish the numbers or do not ship the economy. Phase 7. Priority: next.
- R-N4. Audit Nucleares' save/load and AO failure history (patch notes Jan 2024-Jul 2026) and list the exact failure modes reactor-sim's save and crew systems must test against. Phase 3 and Phase 8. Priority: next.

### Implementation

- I-N1. Public HTTP API from the first public build: flat `SYSTEM_THING_DETAIL` names, ordered vs actual pairs, `LIST` vs `VIEW`, `BATCH_GET`, gated fault-trigger namespace default-off and labelled as cheats, valve and pump state as enums (including requires-maintenance, dry-run, overload, stuck), not booleans. Document it in-repo, versioned with the build. Phase 1. Priority: now.
- I-N2. Headless, fixed-step, faster-than-real-time execution of the same core the Unity build uses, with a seed. Ship collect, train, evaluate as a documented loop. Phase 1. Priority: now.
- I-N3. Chemistry as three timescales with the documentation Nucleares lacks: fast boron dose and filter lever with a published stable band, slow xenon and iodine with a visible time-to-restart after scram or load drop, and a consumable resin capacity with a named in-game regeneration procedure, a waste stream with a volume, and a supply delivery with a lead time and a correct-valve check. Show pH and corrosion as numbers with a rate and a named failure, or do not claim them. Phases 1 and 5. Priority: next.
- I-N4. In-game checklists plus a live P&ID that highlights the fault path. The 13-step startup becomes an interactive checklist; the P&ID is the control-room schematic with live values. Phases 1 and 2. Priority: now.
- I-N5. Daily demand curve with a grid counterparty: hourly demand (dinner peak, night trough), a daily energy goal, a formal shutdown-request procedure with a penalty for cutting supply unannounced. Prestige or XP spent on physical things (fuel, gear, waste, repairs) with lead times, never on assistant power. Publish the income per MWh, fuel price, and maintenance cost. Phase 7. Priority: later.
- I-N6. Faults as a diagnosable cascade with audits as the long tail: integrity degrades to failure, fires, floods, and equipment failure as categories, audits that score hazards including fires. Named fault catalogue with distinct procedures, a fault-injection menu with seeds for practice, and alarms that point at evidence (which pump, which valve, which reading changed). Phases 5 and 7. Priority: next.
- I-N7. Walkable plant where location is a mechanic: controls in rooms, crane fuel handling manual and tablet-dispatched, repairs that require presence, crew that must travel to a repair point. Show travel time ("crew arrives in 40 s"), give the tablet remote monitoring and ordering so walking is a choice, protect frame rate. Phase 2. Priority: next.
- I-N8. Embodied helper with a battery, a queue, and a location, but deterministic and trustworthy: state machines with explicit task queues persisted in the save, never acting without an order or a standing order the player set. No paid speed upgrades. Phases 3 and 7. Priority: later.
- I-N9. Maintenance as a report, not a chore list: colour-coded report with tasks generated from analysis, repair costs charged once and surviving save and load, equipment states visible at a glance. Tie it to the chemistry waste stream. Phase 5. Priority: later.
- I-N10. Radiation as a budget, not a vibe: dose rate by zone on the map, a personal dose budget per shift, a dosimeter readout, gear that changes the number. Phase 4. Priority: later.
- I-N11. Co-op only after single-player is stable, and say so publicly. Phase 3. Priority: later.

### Do not copy

No-hand-holding as a design identity. Save-breaking major updates. Paid assistant power. A blog as API documentation. Faults that feel random rather than diagnosable.

---

## 2. hartrusion/RbmkSimulator (Java 17, GPL-3.0, 96 stars)

Repo: https://github.com/hartrusion/RbmkSimulator. Solo-dev Chernobyl Unit 4 RBMK sim, created Oct 2025, v0.4.7 Nov 2026, 14 releases. Explicitly a thermo-hydraulic operations sim with a faked neutron side, and good because it admits that.

Deep-dive summary: the README states plainly that it has no accurate neutron simulation and many systems simplified for low-end hardware. Reactivity is summed 0-100% terms fed into a tuned state-space flux model, xenon time constants compressed about 60x (iodine 240 s instead of 4 h, documented in a code comment), and a "graphite effect" the author labels in the javadoc as invented because xenon alone could not trigger the AZ-5 accident. The thermal side is real work: a physical network solver, 15 control panels, 9 live mnemonic P&IDs drawn as SVG and converted to Java2D, and a turbine model with a 4-point warmup hold table (750/1500/2250/3000 rpm at 15/27/38/50 bar) and a real sync check (2997-3005 rpm, grid angle within 0.1 rad). The published startup manual is the best artifact in the project, but it ends "[To be continued]" before sync and ramp. Distribution is a manual GitHub Actions release: a ~52 MB Windows zip with bundled JRE, double-click to run, plus a ~1.4 MB jar. Saves are Java-serialized and explicitly not compatible across versions. Open issues list the gaps honestly: no grid or load model, no diesels, skeletal ECCS, no water chemistry, no in-game checklist (most requested), no unit tests. Network co-op exists because the MVC split keeps the core GUI-free.

### Research

- R-R1. Study the RbmkSimulator startup manual structure and write the PWR/SMR equivalent from the Phase 1 model: cold state, fill, pumps, heatup, critical, vacuum, warmup table, sync, ramp, with real setpoints. Do not let it end mid-sync. Phase 1. Priority: now.
- R-R2. Study its `docs/Reactivity.md` and decide reactor-sim's readable feedback display: which feedbacks (void, Doppler, xenon, rods, boron) get shown as normalized documented numbers on top of real 6-group kinetics. Phase 1. Priority: now.
- R-R3. Decide the xenon and decay-heat timescale compression explicitly, with the factor written down in code comments and docs, as a config value. Phase 1. Priority: now.
- R-R4. Study its release pipeline (manual dispatch, jpackage, auto-generated changelog from git log) and spec reactor-sim's equivalent for Unity builds on GitHub Releases. Phase 8. Priority: next.

### Implementation

- I-R1. A startup manual with numbers, shipped with the game and as a web page. Phase 1. Priority: now.
- I-R2. A turbine warmup hold-point table: speed, steam pressure, reactor power, rod or setpoint target per hold. Phase 1. Priority: now.
- I-R3. Bundled scenario saves as first-class menu entries: cold shutdown, hot standby, full power, and at least one fault scenario. Cheapest onboarding available. Phase 2. Priority: now.
- I-R4. Mnemonic-style live schematics: a control-room P&ID as a 2D canvas overlay with live values. Cheaper than 3D, more informative than 3D, and what players screenshot. Phase 2. Priority: next.
- I-R5. In-game procedure and checklist system: the manual becomes checkable steps in the UI. Design the procedure data model early so Phase 1 systems can emit checklist events. Phases 1 and 7. Priority: next.
- I-R6. A real grid and load model: load following, frequency, dispatch. Do not repeat the "breaker closes, nothing happens" sync. Phase 7. Priority: later.
- I-R7. Fault scenarios as designed content: named, scripted, with entry saves, verified by golden replay. Phase 5 and Phase 7. Priority: next.
- I-R8. Protect the C# core and Unity split so LAN co-op stays a transport, not a rewrite: no UnityEngine references in the core, ever. Phases 2 and 3. Priority: now.
- I-R9. Zero-prerequisite build on GitHub Releases from the first public build, plus auto-generated changelogs from git log. Phase 8. Priority: next.
- I-R10. Versioned save schema with migration, designed in Phase 1, tested in CI. Phase 1 design, Phase 8 enforcement. Priority: now.
- I-R11. One-click crash report: export save, log, and core state. Phase 8. Priority: later.

### Do not copy

The 6,279-line god file. Java-serialization saves. GUI-builder lock-in. An invented physics term to make a scenario work. Compress time, not physics. "No community" as a default policy.

---

## 3. gdzx/chernobyl-simulator (VB6, 252 stars)

Repo: https://github.com/gdzx/chernobyl-simulator. Port of Gatekeeper's 1998 *Chernobyl: The Legacy Continues*. Operate RBMK Unit 3, fix faults, stay in budget. The plant model lives in a closed DLL not in the repo; the decompiled VB3 UI is published with permission. License is all-rights-reserved original, so design lessons only, no reusable code or assets.

Deep-dive summary: the malfunction system is the core finding. Random failures roll on the sim timer (0-999 mapped to malfunction 1-100, catalog in the closed DLL). Three kinds: video from the supervisor plus a contractor offer, silent faults you must diagnose from trends and schematics, and menu-only repairs (HEPA filter, condenser tube leaks). Detection is layered and deliberately late: alarms only on serious deviation, so trend-watching is the skill. The killer mechanic is the 10:1 contractor vs DIY repair economy, confirmed in code (DLL price times 10 for the contractor, base price for DIY). Faults cascade physically with priced links: DA level loss ruins feed pumps in about 3 minutes, scrams the reactor, and costs a 2-day restart. The budget loop: income is power (100% makes money, 80% breaks even, 50% loses money), costs span four orders of magnitude (5 rubles per gallon of water to over 2,000,000 rubles per ruined feed pump), and the fail state is a soft lock: over budget means no fuel and no repairs, no game-over screen. Difficulty is starting state, not rules: four initial-condition files from full power down to 0% with substantial maintenance problems. The 48-step startup is the tutorial, the pacing, and the difficulty curve in one. Distribution is the cautionary tale: a 596 MB admin-required installer for a 900 KB repo, launched through an emulator shim, with the number one support issue being "I clicked the exe."

### Research

- R-C1. Study the manual's system-by-system section and the decompiled Field Operator's Report to design reactor-sim's fault economy: which faults get a contractor price, which get a DIY path, what the ratio is, and what the budget ledger shows. Phases 5 and 7. Priority: next.
- R-C2. Study the 48-step startup and the offline-cooling exercise, and write reactor-sim's shutdown procedure with the same care as its startup: decay-heat management as a game in itself. Phase 7. Priority: next.
- R-C3. Decide reactor-sim's win and lose conditions explicitly. Chernobyl has none beyond survival; reactor-sim should define scored startup, budget targets, and a campaign of scenarios alongside open-ended survival. Phase 7. Priority: next.

### Implementation

- I-C1. The 10:1 contractor vs DIY repair economy: every fault offers an expensive instant fix and a cheap fix that requires correct diagnosis. Price it in the same ledger as fuel and consumables so the trade is always visible. Phases 5 and 7. Priority: next.
- I-C2. Diagnosis as the core skill, with layered imperfect detection: alarms late and only on serious deviation, trend charts early if the player looks, schematics for tracing, physical symptoms. Then improve on Chernobyl in the one place it fails: keep a fault log and post-mortem (what failed, when, what it cost, what the player did). Phases 5 and 7. Priority: next.
- I-C3. Physical cascades with priced links: each fault is a causal chain with time constants, not a stat debuff, with a cost attached to each link. The player should be able to narrate, after the fact, why the plant tripped. Phase 5. Priority: next.
- I-C4. The startup as a designed, checkable procedure with hold points that verify a second parameter before proceeding. Keep the 13-step startup and add hold-point checks. Phase 1. Priority: now.
- I-C5. Difficulty as starting state via save files: a named initial-condition file is a difficulty level, from full power with a fat budget down to cold, broken, and broke. Any saved state is a scenario players can author for free. Phase 7. Priority: next.
- I-C6. Malfunction off-switch and an Extras menu (pause, malfunctions off, fuel-burn freeze, cutscene skip) as first-class features, built in from the start, not as debug flags. Same for a sandbox and no-budget mode. Phase 1 and Phase 8. Priority: now.
- I-C7. The manual as a real textbook, in-game and linked: a system-by-system reference stating controls, permissives, alarm thresholds, and repair costs; an interactive schematic linking each component to its page; a contextual help cursor. Phases 2 and 7. Priority: next.
- I-C8. Slow faults as the long game: fuel burnup, xenon poisoning, filter loading, decay heat, visible, slow, and economically meaningful. Phases 4 and 5. Priority: next.
- I-C9. Budget as the theme with a soft-lock fail state, but fix Chernobyl's opacity: show the income rate, show projected income at current power, and show the budget summary with labelled rows. Phase 7. Priority: later.

### Do not copy

MDI window chaos and fixed tiny panels. Silent faults with no debrief. Fragile drag-and-drop as a core ritual. A closed core. A 596 MB installer for a small game. No win condition at all.

---

## 4. whatisnuclear/simulator (MIT, 34 stars, dormant since Jul 2020)

Repo: https://github.com/whatisnuclear/simulator. Web-based educational LWR sim, AP1000-like first, MSR and SFR as design goals.

Deep-dive summary: headline correction, this repo is not a simulator. Verified via the GitHub API, it contains exactly two files (LICENSE.txt and README.md, 9 KB), zero lines of code, 3 commits, all README-only, 0 PRs ever. Everything real lives in the README's thinking and in a side project that shipped elsewhere. The README is a crisp requirements document: R1, dashboard reacts within 1 second; R2, capture a 14-year-old's attention for 4 minutes; R3, simulate an LWR, an MSR, and an SFR; R4, no install; R5, mobile to 50-inch screen; R6, open source. The best ideas: SVG.js animated schematic (stress-tested at 100 rotors on a 2013 laptop), latency thinking (issue 2 corrects the 1 s bar to 350 ms, acknowledge input immediately), contextual hints on wrong-order actions, and the one-slider point-kinetics applet (one verb, one goal: hold 10,000 W steady) that still runs today at live.whatisnuclear.com. Why it stalled: generality before instance one (R3 demanded three reactor types and a multi-reactor DB schema before any reactor existed), the physics critical path became rebuilding Sandia's 1984 TOPAZ in Python, single maintainer, zero PRs, no forcing function. The org itself is alive; the simulator was deprioritized. The 2026 issues are drive-by boilerplate from a one-day-old account, not a revival.

### Research

- R-W1. Write reactor-sim's first-4-minutes spec as a testable requirement: cold iron to a held power level, and measure the completion rate. Phase 1. Priority: now.
- R-W2. Decide the explicit modes early (simulation vs training vs game) and design each mode's difficulty and realism separately. Do not let one mode serve children and engineers at once. Phase 1. Priority: now.
- R-W3. Study the keepinpace point-kinetics solver and the live applet to spec reactor-sim's one-slider kinetics sandbox: one control, one goal, one concept. Phase 1. Priority: now.

### Implementation

- I-W1. An R2-style first-4-minutes requirement, written down and instrumented: raise rods, reach a power level, hold within a band for 60 s. Phase 1. Priority: now.
- I-W2. The animated live schematic as the primary UI: flow speed, pump rotation, and color tied to actual state variables, not a table of numbers. Cheap to render; the 100-rotors-on-a-2013-laptop anecdote is the reminder that schematic animation does not need GPU budget. Phase 2. Priority: next.
- I-W3. Contextual hints on wrong-order actions, keyed to action preconditions in the deterministic core, so hints are derived from sim state rather than hard-coded strings per button. Phases 1 and 2. Priority: now.
- I-W4. Acknowledge input immediately, even if the sim step lags: the control visibly responds in tens of milliseconds. Keep the core's step budget explicit so this stays true as systems are added. Phase 1. Priority: now.
- I-W5. The one-slider kinetics sandbox as tutorial level zero and as a shareable web demo: a URL that teaches delayed neutrons with no install, funneling into the Unity game. The one place a browser build earns its keep. Phase 1 for the in-game sandbox, Phase 8 for the web demo. Priority: now for the sandbox, later for the web demo.
- I-W6. Coolant-agnostic state variables at the physics boundary: pressure plus internal energy as the lookup key, so a future coolant or reactor variant configures the core rather than rewriting it. Do this at the API level inside the existing C# core, not as a database schema. Phase 1. Priority: next.
- I-W7. Plain-language explainer next to the controls: one or two sentences per system, in the control-room view, explaining what the player is seeing and why it matters. Phases 1 and 2. Priority: now.

### Do not copy

Generality before instance one. Escalating the physics core to 1-D transient thermal-hydraulics before Phase 1 is playable. Server-side physics for a game. README-driven development: gate every phase on a playable build, not on a decision document. Leaving the audience decision open. Announcing deliverables before they exist.

---

## 5. ijs-f8/Research-Reactor-Simulator (C++, GPL-3.0, 32 stars)

Repo: https://github.com/ijs-f8/Research-Reactor-Simulator. Real-time simulator of the 250 kW TRIGA Mark II at the Jozef Stefan Institute, Ljubljana. Peer-reviewed (Malec et al., Annals of Nuclear Energy 146, 2020). Explicitly a complementary teaching tool for institutions without a reactor, not a replacement for the plant.

Deep-dive summary: 6-group point kinetics, RK4, fixed 1 ms step, temperature feedback via a semi-empirical TRIGLAV cubic polynomial with an editable two-segment alpha(T) curve, xenon and iodine by Euler every 125 steps (samarium explicitly not implemented), 3 rods of 4,000 pcm each, solved steady-state starts (`pushStableState`), period shown as a 700-sample weighted average mimicking the real analogue meter. Dose-rate and water-level features are dead code, never called or hidden. Validation: the 2020 paper is paywalled and no quantitative validation figures were recovered from any open source; the real credibility mechanism is parameter provenance, where every fitted constant names its source (rod speeds measured with a stopwatch, TRIGLAV polynomial, reactivity-meter beta and lambda, regression on measured pool temperatures), plus explicit scope limits ("NOT for thermal hydraulics, NOT for accident analyses"). Independent confirmation it teaches: a JSI and Uppsala pandemic remote course, and Uppsala's Reactor Physics with Python course, whose six-exercise sequence (1/M startup, rod oscillation and precursors, point of adding heat, pulse reactivity-squared vs peak power, equilibrium xenon at 50 kW with time speed-up, hand-check against source constants) is a ready-made curriculum template. UI: one main graph with power, temperature, and reactivity on shared axes, inserted vs actual reactivity as separate lines, nine tabs, keyboard-first operation, speed 0.01x to 500x, scripting plus TSV export, demo modes (Ctrl+D critical at 0.5 W, Ctrl+F at 12 kW), a pulse-debrief tab with peak power, FWHM, peak fuel temperature, and energy plus a scrubbable 5 s window, and a hardcore mode hiding reactivity. Weaknesses: no tests ever (issue 3 open since 2020, CI test step commented out), a single 3,885-line GUI file, a heavily modified vendored NanoGUI fork frozen in the repo, docs drift from code (period default 4 s manual vs 6 s code, max speed 1000x vs 500x), single-platform shipping despite cross-platform claims.

### Research

- R-T1. Study the Uppsala six-exercise sequence and adapt it as reactor-sim's in-game lesson curriculum, with expected numbers printed. Phase 7. Priority: later.
- R-T2. Study the paper's parameter-provenance pattern and build reactor-sim's provenance table: every fitted constant names its source, including "chosen arbitrarily" where that is true. Phase 1. Priority: now.
- R-T3. Decide reactor-sim's public NOT-for list per phase, in the same spirit as the RRS scope box. Phase 1. Priority: now.

### Implementation

- I-T1. Inserted vs actual reactivity, always on screen: two lines, one axis, rods and boron worth inserted vs net reactivity after feedbacks. Include the hardcore variant that hides net reactivity so the player must infer it from power and period. Phase 1. Priority: now.
- I-T2. The pulse-debrief pattern, generalised to the PWR signature transient: a dedicated debrief screen for any fast transient (rod drop, turbine trip, loss of load, manual scram) with headline numbers (peak power, time-to-peak, FWHM-equivalent or power-doubling time, peak fuel and clad temperature, energy released, minimum DNBR if modeled) plus a scrubbable time window with millisecond readouts. Phase 7, with the screen scaffolded in Phase 1. Priority: next.
- I-T3. Solved steady-state starts as named menu states: hot zero power, 30% with equilibrium xenon, 100% post-refuel, xenon pit after trip. This is also what makes demo and tutorial scripting cheap. Phases 1 and 2. Priority: now.
- I-T4. Speed as a teaching variable, both directions: 0.01x slow motion for fast transients, up to 100-500x for xenon (hours) and shift-length soaks. Keep the discrete-factor ladder and the real-time and paused labelling. Phase 1. Priority: now.
- I-T5. SCRAM taxonomy with per-cause annunciation: distinct, named, individually enable and disable-able trip signals (power, period, fuel and clad temp, coolant temp and pressure, water level, manual), each with its own alarm light showing the offending value, a 0.5 s persistence rule for period trips, and a post-trip recovery procedure (rods dropped is not rods reset; you must work out of the state). Extend to PWR trips (high pressuriser pressure, low DNBR margin, turbine trip) with the same pattern. Phase 1. Priority: next.
- I-T6. Scripted runs plus data export as regression tests: a human-readable script format driving a run (rod moves, mode changes, trip) and a TSV or CSV export of the full history, so a scripted run is a unit test of the physics core. Publish one script per validation case. Phase 1. Priority: now.
- I-T7. Rod worth curves as editable, plotted calibration: differential and integral curves with the maximum marked, dropped-rod state visible. The same screen is a lab instrument. Phase 1. Priority: next.
- I-T8. Delayed-group population display: six precursor groups as percent of steady state, colour-keyed. Small UI element, outsized teaching value for why power lags rods. Phase 1. Priority: next.
- I-T9. Operation modes as experiments: programmed rod stepping, automatic turbine-following and T-avg control with deadband and avoid-trip logic, boron dilution and boration ramps, each with a preview of the intended trajectory before it runs. Phase 7. Priority: later.
- I-T10. Automatic hold-power mode, inspectable: a simple controller the player can watch, tune (deadband, setpoint), and override. It teaches control without a lecture and seeds crew and automation behaviour. Phase 7. Priority: next.
- I-T11. Demo modes as one-key states: HZP critical, 30% with equilibrium xenon, just after a trip with xenon rising. One keypress, pedagogically chosen. Phases 1 and 2. Priority: now.
- I-T12. Three-manual structure, kept in the repo and shipped together: User manual, Physics manual (models, provenance, stated approximations, including the beyond-scope sentence), Developers manual (build, program flow, file structure). Keep doc and code numbers in sync. Phase 8. Priority: next.

### Do not copy

The pulse itself (a PWR SMR has no pulse rod and must never be pulsed). The lumped-pool model as a PWR thermal model. A vendored frozen GUI fork. A 3,885-line UI file. A commented-out test step. Dead features shipped. Hidden cheat codes; make sandbox and god-mode explicit labelled toggles. Single-platform binary while claiming cross-platform. A validation claim without public numbers.

---

## 6. imronuke/ADPRES (Fortran, MIT, 30 stars, obsolete, superseded by KOMODO)

Repo: https://github.com/imronuke/ADPRES. Academic neutron-diffusion core solver, single author, 9,939 lines, 9 files. README declares it obsolete in favour of KOMODO. Verified in Imron (2019), Annals of Nuclear Energy 133, 580-588, against MOX/UO2, LMW, and NEACRP benchmarks.

Deep-dive summary: ADPRES solves static and transient multi-group neutron diffusion in 1D/2D/3D Cartesian geometry with optional thermal-hydraulics feedback. Solver stack is the real one: SANM (semi-analytic nodal method, sinh and cosh flux shapes) with PNM and FDM kept as alternate kernels, CMFD acceleration via two-node non-linear iteration plus BiCGSTAB (claimed 10-20x faster than the original NEM version), IAEA3D converging in 129 outer iterations to k-eff 1.029082 vs PARCS 1.029096 (1.4 pcm). Six delayed-neutron precursor groups stored per node with analytic exponential update, theta-method time integration (default fully implicit), exponential transformation for hot-zero-power ejections, two time-step sizes for rod-ejection accidents. Branched cross-section tables (Serpent and HELIOS format) or linear feedback coefficients, assembly discontinuity factors with rotation rules, critical-boron search as a secant iteration with hard guards (0-3000 ppm, 20 iterations). Thermal-hydraulics is the weakest part physically: enclosed-channel, single-phase, 10-mesh radial fuel-pin conduction, Dittus-Boelter, fixed gap conductance 1e4, 9-point steam table, lumped fuel temperature 0.3 times centerline plus 0.7 times surface for Doppler. No xenon, no decay heat, no depletion, no pin-power reconstruction, no boiling or DNB. The game already exceeds ADPRES on xenon pit and decay heat. Verification culture is strong on benchmark problems as fixtures and weak on automated regression: CI ran 12 sample inputs with zero assertions. KOMODO, the live successor (85 stars, last push Nov 18, 2026), is the same code with partial OpenACC GPU, HDF5 output, CMake, and, most importantly, a `test.sh` with numeric baselines. ADPRES died of naming, maintenance consolidation, and missing test infrastructure, not wrong physics.

### Research

- R-A1. Read the ADPRES method docs (SANM, CMFD, transverse leakages) and the KOMODO `test.sh` to spec reactor-sim's golden-case harness: which cases, which asserted numbers, which tolerances. Phase 1. Priority: now.
- R-A2. Study the `%XTAB` branched-table philosophy and decide reactor-sim's state-dependent feedback tables: which coefficients become tables over power, burnup, and boron, and what published PWR values calibrate them. Phase 1. Priority: next.
- R-A3. Study the rod-cusping problem and the 2019 cusping commit to decide how reactor-sim treats a partially inserted bank. Phase 1. Priority: next.
- R-A4. Study the critical-boron secant search and the adjoint-weighted reactivity and beta calculations to decide what reactor-sim computes at scenario setup vs what it tables. Phase 1. Priority: next.

### Implementation

- I-A1. Two-step RIA time stepping: fine dt during rod motion and power excursion, coarse dt after (ADPRES sample: 5 ms then 50 ms). Expose the step schedule in scenario definitions. Phase 5 fault scenarios, with the mechanism in Phase 1. Priority: next.
- I-A2. Rod-cusping-aware partial insertion: do not linearly interpolate rodded and unrodded worth for a bank partway through an axial node. Use a cusping correction or a precomputed worth-vs-position curve with the correct S-shape. Phase 1. Priority: next.
- I-A3. State-dependent feedback coefficients: replace any constant Doppler, moderator, or boron coefficients with small tables over power, burnup, and boron. Even a 3-point table per coefficient, calibrated against published PWR values, is a large credibility step over constants. Phase 1. Priority: next.
- I-A4. Per-region precursor inventory: split precursors into 2-4 radial or axial regions tracking local power history, instead of one inventory per group. Cheap (a few arrays), and it makes local power tilts after a rod move behave correctly instead of relaxing uniformly. Phase 1. Priority: later.
- I-A5. Critical-boron search as a calibration and scenario tool: implement the secant search (iterate boron until computed reactivity is 0 within tolerance, hard guards at 0-3000 ppm and 20 iterations, fail loudly). Use it at scenario initialisation to guarantee every scenario starts exactly critical. Phase 1. Priority: next.
- I-A6. Adjoint-weighted rod worth, or at minimum provenance per table: weight any published worth curve by the game's own assembly power shape, or document the assumed flux shape next to the number. One paragraph of provenance per table. Phase 1. Priority: next.
- I-A7. Golden-case regression harness with numeric tolerances, KOMODO's `test.sh` pattern: a script that runs golden replay plus scripted scenarios and asserts numbers (peak power, time-to-peak, final reactivity, max fuel temp) against baselines with tolerances. The golden replay is the fixture; the missing piece is the assertions. Phase 1. Priority: now.
- I-A8. Visible consistency diagnostics: a dev overlay or log line per scenario start showing initial reactivity (must be about 0), feedback-coefficient set in use, time-step schedule, and any clamped or extrapolated table lookup. Refuse to start from an inconsistent initial state instead of integrating through it. The KNE1 lesson minus the fudge. Phase 1. Priority: now.
- I-A9. Fuel-temperature convention documented explicitly: expose which temperature drives Doppler and the weighting used (ADPRES convention: 0.3 times centerline plus 0.7 times surface). Do not copy the fixed gap conductance or the 9-point steam table; IAPWS-IF97 plus a proper gap model is already better. Phase 1. Priority: next.

### Do not copy

A nodal solver, CMFD, BiCGSTAB, assembly discontinuity factors, or multi-group diffusion in-game. If spatial flux ever becomes a real requirement, the correct move is an offline reference calculation (KOMODO is free, MIT, and runs IAEA3D in 0.2 s) whose shapes are baked into the game's tables, not a runtime solver. Do not copy the 9-point steam table, the fixed gap conductance, the nu-sigma-f rescaling fudge, the global-state architecture, the STOP-on-error handling, or the "CI runs it and hopes" test style.

---

## Ranked top 10 across all repos

1. **Ship a playable build, double-click and it runs, no admin, no shim.** RbmkSimulator's 52 MB Windows zip is the bar; Chernobyl's 596 MB emulator-shim installer is the failure mode. Put the first build on GitHub Releases with the jar and zip split so non-Windows users are not second-class. Phase 8 work pulled forward to the first public build. Priority: now.
2. **A public HTTP API from the first public build.** Nucleares' habits (flat names, ordered vs actual, LIST vs VIEW, BATCH_GET, gated fault triggers) plus in-repo versioned docs. This is what turns players into controller authors and RL researchers, and it is the single highest-leverage steal. Phase 1. Priority: now.
3. **Headless, fixed-step, faster-than-real-time core.** Nucleares forced its RL community to learn a surrogate model because the game is slow and single-instance. Reactor-sim's determinism is the answer, but only if headless is a first-class mode with a seed. Phase 1. Priority: now.
4. **Bundled scenario saves plus solved steady-state starts.** RbmkSimulator's full-load, half-load, and accident saves and RRS's `pushStableState` are the same idea: a new player experiences the interesting ten minutes without surviving the two-hour startup. Hot zero power, 30% with equilibrium xenon, 100% post-refuel, xenon pit after trip, as first-class menu entries. Phases 1 and 2. Priority: now.
5. **Faults as diagnosable cascades with the 10:1 contractor vs DIY economy.** Chernobyl's price ratio plus Nucleares' integrity cascade, fixed with a fault log and post-mortem that Chernobyl lacks. Every fault is a causal chain with time constants and a cost on each link; alarms point at evidence. Phases 5 and 7. Priority: next.
6. **Chemistry as three timescales with a documented regeneration chore.** Nucleares' shape (fast boron lever, slow xenon, consumable resin with a manual multi-valve regeneration and a waste stream) with the documentation Nucleares never shipped. Show pH and corrosion as numbers with a rate and a named failure, or do not claim them. Phases 1 and 5. Priority: next.
7. **Interactive checklists plus a live P&ID.** Nucleares players made their own checklists and piping diagram; RbmkSimulator's players asked for checklists for months. Ship the 13-step startup as an interactive checklist and the control-room schematic with live values. Phases 1 and 2. Priority: now.
8. **Golden-case regression harness with numeric tolerances.** KOMODO's `test.sh` pattern plus RRS's scripted runs and TSV export: scripted scenarios asserting peak power, time-to-peak, final reactivity, and max fuel temp against baselines. ADPRES proves "it runs" is not a test. Phase 1. Priority: now.
9. **First-four-minutes spec plus the one-slider kinetics sandbox.** whatisnuclear's R2 as a testable requirement (raise rods, reach a power level, hold within a band for 60 s, measure completion rate) and the live applet's hold-10,000-W pattern as tutorial level zero and the shareable web demo. Phase 1. Priority: now.
10. **Inserted vs actual reactivity plus the transient-debrief pattern.** RRS's two-line feedback display and hardcore mode, and its pulse-debrief screen generalised to the PWR signature transient (rod drop, turbine trip, loss of load, manual scram) with headline numbers and a scrubbable window. Phase 1. Priority: now.

---

## Cross-cutting guardrails

These apply to every phase, and each one comes from a named failure in the repos above:

- Gate every phase on a playable build, not on a decision document. whatisnuclear produced three README commits in fifteen months and then silence.
- One deep reactor, done well, beats three shallow ones. whatisnuclear's multi-reactor schema before reactor one is the cautionary tale.
- Version saves from day one and migrate them in CI. RbmkSimulator warns in bold that saves are not compatible across versions; Nucleares broke saves with major updates.
- Co-op only after single-player is stable. Nucleares removed co-op in 2024 and rebuilt it experimentally in 2026, still desyncing.
- Never paywall helper or crew capability. Nucleares' paid AO speed DLC reads as pay-to-win.
- Document timescale compression explicitly and in the open. RbmkSimulator's 240 s iodine constant with its "ridiculous for a simulator" comment is the template.
- No validation claim without public numbers. The RRS paper's validation is paywalled and its numbers were not recoverable; reactor-sim's validation dossier must be public and numeric.
- Scope honesty in the README and in-game, in RbmkSimulator's tone: what is simulated, what is faked, what is not for. Publish a NOT-for list per phase.

---

## Sources

- Per-repo research notes: `research/nucleares.md`, `research/rbmk-simulator.md`, `research/chernobyl-simulator.md`, `research/whatisnuclear-simulator.md`, `research/research-reactor-simulator.md`, `research/adpres.md`
- reactor-sim roadmap: https://github.com/duketopceo/reactor-sim/blob/master/ROADMAP.md (Phases 1-8, read 2026-11-25)
