# Research-Reactor-Simulator (ijs-f8), deep dive for reactor-sim

**Subject:** `ijs-f8/Research-Reactor-Simulator`, https://github.com/ijs-f8/Research-Reactor-Simulator
**Researched:** 2026-11-25, read-only (GitHub API via `gh.py`, official site, Physics / User / Developers manuals, ScienceDirect abstract page). The Malec et al. 2020 paper itself is paywalled; only its abstract/highlights were readable, so quantitative validation claims below are flagged as unverified where they rest on the paper alone.
**For:** `duketopceo/reactor-sim`, deterministic PWR sim game, C# core on .NET 8, Unity 6 front end, 160 MWt SMR, Phase 1 (trustworthy plant) done, Phases 2–8 planned (walkable buildings, crew, radiation, maintenance, campus, operations/progression, release hardening).

## 1. What it is, in one paragraph

A real-time graphical simulator of one small pool-type research reactor: the 250 kW TRIGA Mark II at the Jožef Stefan Institute (JSI), Ljubljana, first criticality 31 May 1966. It is explicitly a *complementary teaching tool* for classrooms and future-operator training, "especially in institutions with limited access to a physical reactor" (README). The JSI Reactor Physics Department (F8) lists its purposes as: education where there is no reactor, classroom presentation of experiments *before* the real-reactor exercise, preparation so real-reactor time is used efficiently, and training future operators. That framing, a rehearsal tool that makes scarce real-plant time more valuable, not a replacement for the plant, is the whole product idea, and it is the single most transferable lesson for reactor-sim.

## 2. Repo facts (read from the API, 2026-11-25)

| Fact | Value |
|---|---|
| Created / last push | 2019-08-29 / 2023-10-31 (API `updated_at` 2026-09-25, no new commits since Oct 2023) |
| Stars / forks / watchers | 32 / 8 / 32 |
| License | GPL-3.0 |
| Language (API) | C++ ~2.69 MB, C ~378 kB, CMake ~24 kB, mostly vendored GUI code, see §7 |
| Default branch | `master`; 28 commits total; contributors: `janmalec` 27, `partofthething` 1 |
| Version | v1.4.1 (2023-03-28), the only GitHub release; its only asset is a Windows x86-64 zip |
| Open issues | 2 of 6 total: #1 "Split code from Nanogui and add Nanogui as module" (open since 2020-02-13), #3 "Create unit tests" (open since 2020-07-09). Closed: #2 submodule versions, #5 cereal build break on Ubuntu 22.04, #6 cereal bump |
| CI | `.github/workflows/cmake.yml`: Ubuntu-only build, Release. The `ctest` step is commented out. A dead `.travis.yml` is still in the tree |
| README | 1,246 bytes: one paragraph, citation, Ubuntu build commands, run requirements (OpenGL 3.3+, 400 MB RAM, VS 2017 redistributable on Windows), one screenshot link |

The commit history is essentially: initial commit Feb 2020, a burst of build fixes Jul 2020, save/load switch to cereal May 2020, LICENSE Dec 2021, a community PR (#6, `partofthething`) Mar 2023 fixing submodules/CI and producing the v1.4.1 binaries, one macOS universal-binary CMake tweak Oct 2023, then silence. This is a finished academic tool in maintenance-free stasis, not an actively developed product. That matters when weighing what to copy: the design survived contact with real students; the engineering hygiene did not keep up.

## 3. The physics model, what is actually simulated

Source: `include/Simulator.h` (682 lines), `src/Simulator.cpp` (1,488 lines), `include/Settings.h` (407 lines), `include/ControlRod.h` (403 lines), and the Physics Manual (IJS-DP 12880, Toškan / Malec / Snoj).

### 3.1 Core kinetics

- **6-group point kinetics, explicit RK4, fixed step `DT_STEP = 0.001` s** (`Simulator::mainLoop`, `neutronChange`). One prompt population + six delayed groups. Per-frame, the loop computes how many 1 ms steps wall-clock time (× speed factor) requires and runs them, so physics is decoupled from frame rate.
- Delayed-group defaults (from the digital reactivity meter source, per the Physics Manual): β = [0.23097, 1.53278, 1.3718, 2.76451, 0.80489, 0.29396]×10⁻³ (sum ≈ 0.007), λ = [0.0124, 0.0305, 0.1115, 0.301, 1.138, 3.01] s⁻¹. Each group can be individually enabled/disabled and its β/λ edited live from the GUI; setting βᵢ = 0 removes delayed neutrons entirely. That is a teaching feature, not a physics need: a student can feel what prompt-critical means.
- Prompt neutron lifetime default 39 µs (`PROMPT_NEUTRON_LIFETIME_DEFAULT = 39e-6`); ν̄ = 2.43; σ_f (U-235 fission) 586.691 b, capture 99.3843 b (ENDF/B-VIII, quoted in the header); fuel density 6 g/cm³, 20% enrichment, 12% uranium content; 59 fuel elements (`no_fuel_elements = 59`, comment: "Can be counted by looking at them").
- Reactivity bookkeeping is the clean part: ρ(t) = ρ₀(rod positions) − α(T)·ΔT − ρ_Xe(t). Inserted reactivity (rods only) and actual reactivity (after temperature + xenon defects) are tracked as *separate* time series and plotted as separate lines (grey vs blue). That split is the single best UI idea in the program (§5).
- **Period is not analytic.** It is an exponentially weighted moving average (weight 0.95) over the last 700 samples of log-power ratios, computed once per frame in `solvePerFrame`. The Physics Manual says this deliberately mimics the analogue period meter on the real TRIGA, which cannot show instantaneous jumps. Asymptotic period is also computed (ℓₑ/ρ). Lesson: display what the *instrument* would show, not the raw state variable.
- **Constant-power initialisation** (`pushStableState`): given a target power, it solves the steady-state neutron populations (derivatives = 0), the stable fuel/water temperatures from the fitted polynomial, and the rod positions that give exactly that reactivity, rod by rod (Safety, then Shim, then Regulating). Demo modes use it: Ctrl+D puts the reactor exactly critical at 0.5 W; Ctrl+F at 12 kW "where temperature effects are evident" (User Manual §2.3). Starting a sim from a solved steady state instead of from zero is what makes the tool usable in a 10-minute classroom slot.

### 3.2 Temperature feedback, semi-empirical, and honest about it

- Fuel temperature follows an asymptotic (first-order lag) model: dT/dt = (P − P_cooling(T))/C_f(T), with C_p(T) = 2.04 + 4.17×10⁻³·T J/(K·cm³) (from Simnad 1981) and a *reversed* third-order polynomial for the cooling term. The polynomial coefficients (67.18×10⁻³, −8.381×10⁻⁶, 0.3843×10⁻⁹) come from the TRIGLAV code for a typical TRIGA element under natural convection, fitted to experimental data.
- The temperature coefficient α(T) is a two-segment piecewise-linear function the user can edit: α₀ at 0 °C (default 6 pcm/°C, note the sign convention in the code/manual is confusing; the manual describes it as the negative feedback coefficient), a peak value at T₁ (default 240 °C, 9 pcm/°C), then a slope after the peak (−0.004 pcm/°C²). The GUI draws this α(T) curve live as you drag its four parameters.
- Water heating: one lumped pool, volume default 20 m³, heated by all fission heat, cooled by losses to air (∝ ΔT^(4/3)) and to concrete, plus optional active cooling at a fixed 240 kW. The cooling coefficient (0.022 kWh/K × 19.6 kWh/K) is "determined from experimental data" / linear regression on measured temperatures.
- The Physics Manual is explicit about the limit: "proper description of heat transfer and temperature profiles in the core can be calculated only by using 3D CFD simulations, which are beyond the scope of this real-time simulator. Hence approximations had to be used." That sentence is the model for how reactor-sim should document its own thermal model.

### 3.3 Xenon, source, dose, water level

- **¹³⁵Xe / ¹³⁵I:** Euler integration (not RK4, "a fourth order solver would introduce unnecessary computational overhead"), recomputed every 125 steps (0.125 s) because poisons move slowly; saved to file every 5,000 steps. γ_I = 0.064, γ_X = 0.0023, λ_I = 0.1035/h, λ_X = 0.0753/h, σ_Xe,a = 2.6×10⁻²² m². Fission-poisoning effects are **off by default** (`FISSION_POISONS_ENABLED_DEFAULT = false`); the manual warns that toggling it at sustained high power "will noticeably lower or increase reactivity". **¹⁴⁹Sm is not implemented**, the manual says so plainly ("cross section lower by two orders of magnitude").
- **Neutron source:** insertable/withdrawable, activity default 10⁵ Bq ("chosen arbitrarily", the comment in `Settings.h` says so), with its own periodic modulation modes (square/sine/sawtooth, same machinery as rod oscillation). Source neutrons enter the kinetics directly.
- **Dose rate and water level are dead code.** `recalculateDoseRate` (flux attenuated through 5 m of water, θ = atan(1 m / 5 m), exponential absorption −0.3635 cm⁻¹, "measured at IJS TRIGA in 2016") is never called anywhere; `getDoseRate()` is never displayed. Water level: the display and the scram label are `setVisible(false)`, the limit box is commented out in the GUI, and the scram check is commented out in `checkOperationalLimits`. The scaffolding shipped; the feature did not. Do not copy the scaffolding.

### 3.4 SCRAM / operational limits

Checked every 1 ms step (`checkOperationalLimits`). Defaults in `Settings.h`: period 6 s, power 250 kW, fuel temperature 300 °C, water temperature 80 °C, water level 0.2 m. (The User Manual says the period default is 4 s, a doc/code discrepancy; the code's 6 s is what ships.) Each limit has its own enable/disable toggle. Period SCRAM only fires if the period stays under the limit for > 0.5 s, and is suppressed while the regulating rod is in Simulation (oscillation) mode. Hard absurdity guards sit above the user limits: power > 10¹³ W or fuel temperature > 950 °C triggers a "Severe error" dialog and SCRAM ("Power exceeded 10 GW, an absurd limit. The simulator can't work with infinite numbers"). A `godMode` flag (typed cheat code `GODMODE`, per the User Manual) disables all automatic SCRAMs. Pulse mode auto-SCRAMs 5 s after firing, "to mimic the behaviour of IJS TRIGA" (Physics Manual §2.5; User Manual §3.5).

On SCRAM, all rods drop (magnet detaches, rod falls to bottom in 0.5 s); the magnet stays where it was. To recover you must drive the magnet back down to the rod and press RESET (or `0`). That magnet-vs-rod distinction is a real TRIGA behaviour turned into a teaching mechanic: a scram is not a reset button, it is a state you have to work out of.

## 4. Training and validation design, what makes it credible

### 4.1 What the paper claims (and what I could actually verify)

Malec, Toškan & Snoj, "PC-based JSI research reactor simulator", *Annals of Nuclear Energy* 146, 107630 (Oct 2020), doi:10.1016/j.anucene.2020.107630. ScienceDirect abstract + highlights (the full text is paywalled; I did not obtain it):

- Highlights: "Research Reactor Simulator useful when access to physical research reactor is limited." / "Validation by comparison with real reactor." / "Graphics card accelerated interface offers real-time simulation." / "Point kinetics equations with a semi empirical thermodynamics model is used." / "Simulator is fast, robust, flexible, and easy to use."
- Abstract: real-time simulator for education/training "especially in countries with limited or no access to a research reactor"; "simulates time behaviour of reactor power, fuel temperature and reactivity by using the 6-group point kinetics equation with feedback. It features temperature feedback mechanisms and xenon poisoning. Using graphics acceleration to render simulation results and a simple integration scheme with a short simulation step to propagate physics achieves low latency between a user's input and display of simulation results, which is important for research reactors that can perform fast transients. The simulator targets TRIGA-type reactors, but all physical parameters can be changed on the fly."

**What I could not verify:** any quantitative validation result. The abstract gives no error percentages, no named transients, no figures. I found no open-access copy of the paper's validation section, and the manuals do not contain validation plots either. Treat "validated against the real TRIGA" as: the authors state it, and the parameter provenance below is consistent with it, but the *numbers* are not in the open record I could reach. reactor-sim should not cite this as "validated to X%", and should note that its own validation package needs to be public and numeric precisely because this one is not.

### 4.2 Validation by parameter provenance (the real credibility mechanism)

What the code and manuals *do* show is a consistent pattern: every fitted number names its source in a comment or the manual.

- Rod speeds "similar to measured values with a stopwatch at IJS TRIGA" (`Settings.h`); rod worths "based on calibration curves available in the operating room in printed form"; rod curves editable because real worth curves are S-shaped.
- βᵢ/λᵢ "available from the source code of the digital reactivity meter" (Physics Manual §2.1, ref. Lengar et al. 2012).
- Temperature polynomial from TRIGLAV (Peršič et al. 2017); cooling coefficient from regression on measured pool temperatures; water-absorption coefficient "measured at IJS TRIGA in 2016".
- Fuel heat capacity from Simnad 1981; water properties from the IAEA thermophysical-properties tutorial (2008); cross sections ENDF/B-VIII.
- The pulse auto-scram delay (5 s, code) vs "6 seconds" (Physics Manual §2.5) is another small doc/code mismatch, but the *intent*, copy the real plant's procedure, not an idealised one, is the point.

The credibility claim is therefore not "our model is high-fidelity" but "every parameter is traceable to this specific reactor's measurements, and where we approximated we say so." That is a cheaper and more defensible claim than a fidelity percentage, and it is exactly the right model for reactor-sim's Phase 1 "trustworthy plant" story: a parameter-provenance table beats a marketing number.

### 4.3 Independent evidence it works as a teaching tool

Two sources beyond the authors:

- **Pandemic remote teaching** (EPJ Web of Conferences 253, 10001, 2021, "On teaching experimental reactor physics in times of pandemic"): JSI ran a five-day experimental reactor physics course for Uppsala University remotely, and notes it "can also organize a remote course during a full lockdown when the reactor is not able to operate using the in-house developed Research Reactor Simulator based on a point kinetics approximation and a simple thermohydraulic module." Student feedback was "generally positive". The simulator's job here was continuity: the course happens even when the reactor cannot run.
- **Uppsala "Reactor Physics with Python" course** (Zenodo, project ideas paper): students use the JSI TRIGA simulator as the closest available stand-in for a reactor Sweden no longer has. The prescribed exercises are a ready-made curriculum and a direct template for reactor-sim's training design: (1) set a realistic control-rod worth curve, then start up while doing a **1/M exercise** (using inverse power in lieu of detector counts); (2) bring the reactor critical at zero power, then investigate **control-rod oscillation and the temporal change of delayed-neutron precursors**; (3) raise power to observe **the point of adding heat and the effects of reactivity feedback**; (4) run several **pulse experiments** (optionally scripted) and explore the relationship between reactivity squared and peak power; (5) determine the **equilibrium xenon worth at a stable 50 kW**, using the simulator's time speed-up (which is "actually advantageous compared to a real experiment"); (6) benchmark results by extracting constants from the simulator's source code and computing the expected xenon worth by hand. Note the last step: the source code is part of the lab. Students verify the sim against theory, not just play with it.

### 4.4 What "validation" does *not* cover

The official website's own scope box is the clearest statement: "Low power reactor, thermal power lower than 1 MW. Reactor physics with simple feedback. For training and education. Robust, fast, works on PC. **NOT for thermal hydraulics. NOT for accident analyses.**" The Physics Manual repeats the CFD disclaimer. There is no spatial kinetics, no burnup, no samarium, no secondary side, no containment, no accident progression. Validation, whatever its numbers, is validation *of point kinetics + lumped temperature + xenon on one TRIGA core*, nothing broader. reactor-sim's PWR scope is far wider, so its validation burden is far larger, and it should publish the same kind of explicit NOT-for list per phase rather than letting scope creep silently.

## 5. UI and interaction design for learners

One window, three vertical zones (User Manual §1): the **main graph** on top, a **tab area** (control panel) in the middle, a **bottom row** (version, FPS, serial-box status, speed factor, elapsed time).

### 5.1 The main graph, the whole lesson on one screen

Three vertical axes share one time axis (right end = now, left end = N seconds ago, default 30 s, settable 0.5 s–7200 s):

- **Power, red**, watts, auto-scaled, exponential notation, optional log scale.
- **Temperature, green**, °C, default 0–400 °C, user-settable limits.
- **Reactivity, blue + grey on a shared axis**, pcm: blue = *actual* reactivity (rods minus temperature defect minus xenon), grey = *inserted* reactivity (rods only). The gap between the two lines *is* the feedback lesson, visible without opening any tab.
- Coloured triangles on each axis mark current values; 10×10 grid; optional curve fill. A "hardcore" toggle hides both reactivity lines/readouts entirely, the trainee must infer reactivity from power and period, like a real operator with no reactivity meter. That is a one-checkbox difficulty mode, and it is excellent.

Data displays on the Main Controls tab repeat the numbers: power, actual/inserted reactivity, period, fuel temperature, water temperature, water-level change (hidden, dead, §3.3).

### 5.2 The nine tabs

1. **Main controls**, SCRAM (red, 150×45) and FIRE (pulse) buttons; operation-mode dropdown (Manual / Square wave / Sine wave / Saw tooth / Automatic / Pulse); RESET SCRAM; a vertical **rod-position display** (Safety S / Regulating R / Shim C as dark bars, magnet = white triangle, exact step count underneath, low-contrast scheme when a rod is detached); per-rod magnet-position text boxes; Cooling and Neutron-source checkboxes; **alarm lights** MAN / PER / FTEMP / WTEMP / POW / WLEVEL that glow red with the scram reason and the offending value ("Power too high | 251234 W").
2. **Graph controls**, graph height (30–70% of window), display time, reactivity/temperature axis limits with Reset, curve fill, rod-reactivity line on/off, log scale, hide-reactivity.
3. **Physics settings**, two sub-tabs. *Physics:* a 6×2 editable table of βᵢ and λᵢ with per-group enable toggles and slowest/fastest labels; core volume, water volume, temperature-effects and xenon toggles, excess reactivity, cooling power, source activity, prompt lifetime; the four α(T) parameters with a **live graph of the α(T) curve**. *Neutron source:* its own periodic-mode settings + a graph of the source waveform.
4. **Control rods**, per rod: steps (1–100,000), worth (pcm), speed (steps/s), and two sliders shaping the **integral worth curve** (numerical approximation of a cubic Bézier; linear, exponential, asymptotic, S-curve). A graph shows the integral curve + current position, and a second graph the **differential worth curve** (pcm/step vs position) with its maximum marked. The red region where magnet position exceeds rod position (detached/falling) is drawn on the curve. This tab is the calibration lab: worth curves are not hard-coded, they are fitted by the student.
5. **Delayed neutrons**, six plots of each group's population relative to its steady-state value (0–300%), colour-keyed. You watch precursors lag power. Almost no other sim exposes this.
6. **Operation modes**, Square wave / Sine wave / Saw tooth sub-tabs, each with period, amplitude, waveform-shape parameters (as % of period), and a **live preview graph of the rod trajectory** with a moving tracker line; plus an Automatic sub-tab (hold current power vs reach set power, steady power in watts, avoid-period-scram toggle, max deviation %, default 2%). These are scripted *experiments*, not just settings: rod oscillation is how you measure the transfer function.
7. **Operational limits**, the five scram limits with values + per-signal enable toggles, "all rods at once" toggle, "SCRAM after pulse" toggle.
8. **Pulse**, after a pulse: a dedicated 4-plot graph (power / reactivity / temperature / rod reactivity over the pulse) with an **interval slider scrubbing a 5 s window** (millisecond readouts), and four headline numbers: **peak power (MW), FWHM (ms), peak fuel temperature (°C), pulse energy (MJ)**. Before any pulse it reads "Perform a pulse experiment to view data". This is the debrief screen; it is the best-designed tab in the program.
9. **Other**, Save/Load settings (`.rrs`, cereal JSON, all 94 settings), Save data (full history TSV: time, reactivity, inserted reactivity, power, temperature, xenon, iodine), Rod curves (export calibration tables), Load script, Reset simulator.

### 5.3 Keyboard, speed, scripting, demos

- **Keyboard-first operation:** S/R/C selects a rod (chordable, multiple at once), ↑/↓ moves it, X scrams, `0` resets, `1`/`2`/`3` enable/disable each rod, P fires the pulse, N toggles the source, Space pauses, ←/→ changes speed, Shift+Tab cycles tabs. Typed cheat-words (no Enter): `RESET`, `DEBUG` (console), `GODMODE`. The manual documents all of them. A trainee can run the whole reactor without touching the mouse, which is also why it works as a classroom demo driven from a laptop.
- **Speed:** 21 discrete factors from 0.01× to 500× in code (`simulationTimes`, index 8 = 1×), labelled "real-time" at 1×, "paused" at 0. The website and User Manual claim 1000× fastest; the shipped code tops out at 500×. Slow motion (0.01×) is what makes a ~10 ms pulse examinable; 500× is what makes a xenon transient (hours) completable in a lab session. Both directions matter, and the Uppsala xenon exercise depends on the fast end.
- **Scripting:** a plain-text script file, one command per line (`<time_s> <command> <value>`), executed by `doScriptCommands` on the physics thread. Commands: `setRegulatingRod`, `moveRegulatingRod`, `setShimRod`, `setSafetyRod`, `setStablePower`, `setAlpha0/AtT1/T1/K`, `saveToFile`, `exitSimulator`, `setSimulationSpeed`, `setSimulationMode`, `holdPower`, `firePulse`, `setDataLogDivider`, `setRegulatingSteps`, `setCvCoeffPropA/B`. Issue #3 ("Create unit tests") notes explicitly: "The scripting interface allows unit tests for the physics engine." Nobody ever wrote them, but the hook is the right one: a scripted run + `saveToFile` is a deterministic regression test for free.
- **Demo modes:** Ctrl+D (exactly critical, 0.5 W), Ctrl+F (critical at 12 kW, temperature feedback visible). One keypress to a pedagogically chosen state.
- **Hardware box:** Windows-only (`#if defined(_WIN32)`) serial code auto-detects a COM-port device ("The Box Mk. III"), authenticates it with an 11-byte handshake, and shows its status in the bottom row. Its purpose is not documented in any manual I read, and the GUI contains an uncommented reference to sending image bits over serial; I cannot say what it does beyond authentication + status. Treat as an undocumented curiosity, not a feature to copy.

### 5.4 What the UI deliberately does not do

No 3D view, no plant mimic diagram, no walkable control room, no crew, no procedures, no scoring, no progression, no sound (none found in the code I read), no multiplayer. The trainee's entire job is: move rods, watch three curves, don't scram. The feedback loop is seconds long and the causal chain (rod → reactivity → power → temperature → feedback → power) is visible on one screen. That is why it teaches.

## 6. Scope discipline, how narrowness serves it

The narrowness is the design:

- **One reactor, one core model.** Point kinetics, one lumped fuel temperature, one lumped water temperature, one poison pair. No spatial model, no loop model, no turbine, no electrical side. The 250 kW TRIGA needs none of those to teach kinetics, and adding them would have buried the lesson.
- **One signature phenomenon, done properly: the pulse.** TRIGA's pulse (fast reactivity insertion, terminated by prompt negative temperature feedback) is the thing a TRIGA can do that a power reactor cannot, and the sim is built around it: Pulse operation mode, FIRE button, auto-scram after 5 s, the dedicated Pulse debrief tab with FWHM/peak/energy. reactor-sim's PWR has no pulse rod; the transferable part is not the pulse, it is *picking one signature transient per plant and building the debrief around it* (for a PWR SMR: a turbine trip / loss-of-load, or a rod-drop, with the same peak/FWHM-style headline numbers).
- **Parameters editable, model fixed.** "All physical parameters can be changed on the fly. Thus, the simulator can be easily adapted to simulate other reactors." (abstract). The 94 settings are the adaptation mechanism; there is no second reactor model. That is honest generality: re-parameterise, don't re-model.
- **Explicit NOT-for list** (website + manual). reactor-sim should publish the equivalent per phase.

The cost of the narrowness is also visible: the sim teaches *kinetics*, not *operations*. There is no startup procedure, no checklist, no shift, no maintenance, no radiation protection (dose is dead code), no abnormal-event catalogue beyond scram. A student who masters it can run a TRIGA; they cannot run a shift. Phases 2–8 of reactor-sim are precisely the parts this sim never attempted, which is why it is a good reference for Phase 1 physics and a bad template for the rest.

## 7. Cross-platform approach (C++, OpenGL) and what it costs

- **Stack:** C++ (CMake, min version 2.8.12, ancient), OpenGL 3.3+, GLFW + GLAD + NanoVG for rendering, Eigen, cereal (JSON save/load), pybind11/coro vendored but the GUI target does not obviously use them. GUI is a **heavily modified, vendored fork of NanoGUI**, frozen in the repo: `src/` mixes NanoGUI widget sources (`button.cpp`, `slider.cpp`, `screen.cpp`, …) with simulator sources (`Simulator.cpp`, `SimulatorGUI.cpp`) with no separation, and `include/nanogui/` carries the forked headers. The Developers Manual admits it: "Since the library was so heavily modified that practically no classes are used in the original form, the library is frozen in time and not up to date with the mainstream version."
- **Measured cost of that choice:**
  - `SimulatorGUI.cpp` is a single 3,885-line file containing the entire UI, keyboard handling, serial handling, settings persistence, and cheat codes. `Simulator.cpp` (1,488 lines) is the physics; the split between the two files is the only architectural boundary, and it is a good one, but inside the GUI file there is no further structure.
  - Issue #1 (split NanoGUI out as a submodule) has been open since the first week, Feb 2020, never done. Issue #3 (unit tests) open since Jul 2020, never done; the CI test step is commented out. The cereal submodule's own unit tests broke the Ubuntu 22.04 build (issue #5, `altStackMem` / `SIGSTKSZ` in doctest), vendored test code breaking your build is the vendoring tax in miniature.
  - Cross-platform is real but uneven: README claims Windows/Linux/Mac tested; the only shipped binary is Windows; the website's system requirements say "A 64 bit Windows PC/laptop (Linux and iOS are not supported yet…)"; CI builds Ubuntu only; the last commit (Oct 2023) is macOS universal-binary CMake flags. A user on Linux builds from source with `apt install … xorg-dev libgl1-mesa-dev g++-multilib`, workable, but "cross-platform" here means "compiles on three OSes", not "ships on three OSes".
  - Performance claim (Developers Manual): on an i7-7700K + GTX 1050 Ti, drawing takes ~100× longer than propagating physics at 1× speed, "so the effect of the physics simulation on graphics refresh rate is negligible"; 40 fps target; 400 MB RAM. The physics is trivially cheap at 1 ms steps because the model is tiny. The GPU is used to *render graphs fast*, not to compute, the abstract's "graphics acceleration" is about latency of the display loop, and the fixed-step RK4 is what keeps input-to-display latency low on fast transients. That distinction is worth preserving: low latency is a solver + render-loop property, not a GPU-compute property.
- **What this means for reactor-sim:** the C#/Unity choice already avoids the worst of this (no vendored GUI fork, real cross-platform shipping, a managed test story). The caution is the *pattern*, not the language: a single 3,885-line UI file, a frozen forked dependency, and a commented-out test step are what happen when the physics is the product and the engineering is an afterthought. reactor-sim Phase 8 (release hardening) should explicitly include the things this repo deferred forever: CI on all shipped platforms, tests actually running, no dead code shipped (dose/water level), docs that match the code (period default 4 s vs 6 s; speed 1000× vs 500×; pulse auto-scram 6 s vs 5 s).

## 8. Genuinely good / weak / missing

### Genuinely good
- **Inserted vs actual reactivity as two lines.** The feedback lesson, always visible. Steal directly.
- **Editable rod worth curves + differential worth plot.** Calibration as a lab exercise, not a config file.
- **Delayed-group populations plotted per group (0–300% of steady state).** Makes precursor lag tangible.
- **Live α(T) curve editor.** Feedback coefficient as a drawable function.
- **Pulse debrief tab** (peak power, FWHM, peak fuel temp, energy + scrubbable 5 s window). The model for every transient debrief.
- **Automatic (hold-power) mode with avoid-period-scram and 2% deadband.** An autopilot you can watch work and outsmart.
- **Scripting + TSV export + `.rrs` settings files.** Reproducibility is the teaching feature; the script format is also the unit-test hook.
- **Speed 0.01×–500× + pause + solved steady-state starts + one-key demo modes.** Time is a teaching variable.
- **Hardcore mode** (hide reactivity). One toggle, real difficulty.
- **Magnet-vs-rod scram recovery.** A physical procedure embedded in the interaction, not a dialog box.
- **Three manuals** (User / Physics / Developers), and a public NOT-for list. Documentation as part of the credibility claim.
- **Parameter provenance.** Every fitted constant names its measurement or source.

### Weak
- **No tests, ever.** Issue #3 open 6+ years; CI test step commented out; the only test code in the tree is a vendored dependency's, and it broke the build.
- **Monolithic GUI file; vendored frozen NanoGUI fork.** Issue #1 open since day one. Upstream NanoGUI moved on; this one cannot.
- **Dead code shipped:** dose-rate computation (never called/displayed), water-level display + scram (hidden/commented out), `DEMO_VERSION` remnants, an undocumented serial hardware box.
- **Docs drift from code:** period scram default 4 s (manual) vs 6 s (code); max speed 1000× (manual/website) vs 500× (code); pulse auto-scram 6 s (manual) vs 5 s (code). Small, but these are exactly the numbers a student would quote.
- **Single-platform shipping** despite cross-platform claims; one Windows binary, 2023; no Linux/Mac artefacts; CI Ubuntu-only.
- **Ancient build floor:** CMake 2.8.12, Travis file left in tree, VS 2017 redistributable requirement.
- **Hidden cheat codes** (`GODMODE`, `DEBUG`) typed blind, documented in the manual but invisible in the UI. In a training tool that is a mixed blessing; in a game it would be a leak. Make sandbox toggles explicit instead.
- **No accessibility/UX modernisation:** fixed minimum window size, dense 18 pt NanoGUI theme, no localisation, no help overlay in-app (help lives in a PDF).

### Missing (relative to its own mission, let alone reactor-sim's)
- No lesson plans, exercises, or assessments in the repo, the curriculum lives in other people's course papers (Uppsala), not in the product. No built-in 1/M exercise, no scripted lab sequence, no scoring or mastery record.
- No multi-user / instructor mode (no way for a teacher to see a student's run, inject a fault, or freeze the sim).
- No fault injection beyond scram limits (no stuck rod, no instrument failure, no loss of cooling as an *event*, cooling is a checkbox).
- No samarium, no burnup, no spatial effects, no secondary side, by design, but it bounds what "operator training" can mean here.
- No telemetry/assessment export beyond the raw TSV; nothing a teacher can grade without hand analysis.

## 9. What reactor-sim should steal, concrete, prioritised

Ordered by value per effort, mapped to reactor-sim's phases. "Steal" means the *mechanism*, adapted to a 160 MWt PWR SMR; the TRIGA-specific content (pulse rod, pool, 250 kW) does not transfer literally.

### Steal now (Phase 1 hardening / Phase 2 walkable control room foundations)

1. **Inserted-vs-actual reactivity, always on screen.** Two lines, one axis: rods/boron worth inserted vs net reactivity after feedbacks (fuel/moderator temp, xenon, boron). The gap between them is the feedback lesson in a PWR exactly as in a TRIGA. Include the "hardcore" variant: a mode that hides net reactivity so the player must infer it from power/period, that is your difficulty toggle, nearly free.
2. **The pulse-debrief pattern, generalised to the PWR signature transient.** A dedicated debrief screen for any fast transient (rod drop, turbine trip, loss of load, manual scram): headline numbers (peak power, time-to-peak, FWHM-equivalent or power-doubling time, peak fuel/clad temperature, energy released, minimum DNBR if you model it), plus a scrubbable time window with millisecond readouts. Do not copy the pulse; copy the *debrief*.
3. **Solved steady-state starts.** `pushStableState` is the feature that makes a 10-minute session possible: "start critical at 5% power, xenon equilibrated" as a solved state, not a 40-minute ramp. Ship named starting states (hot zero power, 30% with equilibrium xenon, 100% post-refuel, xenon pit after trip) as first-class menu items. This is also what makes demo/tutorial scripting cheap.
4. **Speed as a teaching variable, both directions.** 0.01× slow motion for fast transients, up to 100–500× for xenon (hours) and shift-length soaks. The Uppsala xenon-at-50 kW exercise only works because time can be accelerated; reactor-sim's xenon-pit and shift-progression loops have the same dependency. Keep the discrete-factor ladder and the "real-time"/"paused" labelling.
5. **SCRAM taxonomy with per-cause annunciation.** Distinct, named, individually enable/disable-able trip signals (power, period, fuel/clad temp, coolant temp/pressure, water level, manual), each with its own alarm light showing the offending value, a 0.5 s persistence rule for period trips, and a post-trip recovery procedure (rods dropped ≠ rods reset; you must work out of the state). Extend to PWR trips (high pressuriser pressure, low DNBR margin, turbine trip) with the same pattern.
6. **Scripted runs + data export = your regression tests.** The RRS script format (`<t> <command> <value>`) plus TSV export is the cheapest deterministic test harness available. reactor-sim already has golden replay; add the RRS half: a human-readable script that drives a run (rod moves, mode changes, trip) and a TSV/CSV export of the full history, so a scripted run *is* a unit test of the physics core (exactly what RRS issue #3 proposed and never built). Publish one script per validation case.

### Steal for the training/operations phases (Phases 2, 6)

7. **The Uppsala exercise sequence as a curriculum template.** Adapt verbatim: (a) 1/M startup approach to critical (inverse count rate / inverse power); (b) critical at zero power, then rod-oscillation + delayed-precursor observation; (c) point of adding heat, raise power until feedback appears; (d) a set of scripted transients varying one parameter, plotting the response law (for PWR: rod worth vs peak power, boron worth vs critical position); (e) equilibrium xenon worth at a held power, using time acceleration, then hand-calculation from source constants to check the sim. Ship these as in-game lessons with the expected numbers printed, not as a wiki page.
8. **Rod worth curves as editable, plotted calibration.** PWR rods have worth curves too (differential/integral, axial offset). Let the player (or the instructor) see and edit them, with the differential curve and its maximum marked, and the magnet-vs-rod / dropped-rod state visible. This is the bridge between "game" and "training tool": the same screen is a lab instrument.
9. **Delayed-group population display.** Six precursor groups as % of steady state, colour-keyed, is a small UI element with outsized teaching value for why power lags rods. In a PWR the same display explains the difference between prompt jump and delayed response after a rod step.
10. **Operation modes as experiments.** Square/sine/sawtooth rod oscillation with live trajectory preview is a transfer-function lab. For a PWR, the equivalents are: programmed rod stepping, automatic (turbine-following / T-avg) control with deadband and avoid-trip logic, and boron dilution/boration ramps, each with a preview of the intended trajectory *before* it runs.
11. **Automatic hold-power mode, inspectable.** A simple controller (move rod if |P − P_set|/P_set > 2%, pause moves that would trip on period) that the player can watch, tune (deadband, setpoint), and override. It teaches control without a lecture, and it is the seed of crew/automation behaviour in later phases.
12. **Demo modes as one-key states.** Ctrl+D / Ctrl+F equivalents: "critical at 0.5 W", "critical at 12 kW with feedback visible" become "HZP critical", "30% with equilibrium xenon", "just after a trip, xenon rising". One keypress, pedagogically chosen, no menu spelunking.

### Steal for credibility and release (Phases 1, 8)

13. **Parameter-provenance documentation.** A table in the Physics Manual (and in-repo) where every fitted constant names its source: measurement, code (TRIGLAV / ENDF / IAEA tables), or "chosen arbitrarily" where that is true. The RRS authors wrote "chosen arbitrarily" for the source activity and "measured with a stopwatch" for rod speeds, that candour is more credible than false precision. reactor-sim should do the same for its fitted thermal coefficients.
14. **A public NOT-for list.** Copy the website's scope box verbatim in spirit: what the model is *not* for (thermal-hydraulics detail, accident analysis, licensing) per phase. It protects the project from being judged against a scope it never claimed.
15. **Three-manual structure, kept in the repo.** User manual (what you see/do), Physics manual (models + provenance + stated approximations, including the CFD-style "beyond scope" sentence), Developers manual (build, program flow, file structure). The RRS Developers Manual was honest that it was "not yet released" in the Physics Manual's introduction and then shipped anyway, ship all three together, and keep the doc/code numbers in sync (see §8, docs drift).
16. **The anti-pattern list (do not steal).** No vendored frozen GUI fork; no 3,885-line UI file; no commented-out test step; no dead features shipped (dose/water level); no hidden cheat codes, make sandbox/god-mode an explicit, labelled toggle; no single-platform binary while claiming cross-platform; no validation claim without public numbers.

### Do not steal
- The pulse itself (TRIGA-specific; a PWR SMR has no pulse rod and must never be pulsed). The debrief transfers; the transient does not.
- The pool/lumped-water model as a PWR thermal model. A PWR needs at least a primary loop + steam generator + pressuriser; the RRS lumped-pool approximation is the *floor* for a research reactor and would be the wrong ceiling for a power reactor. Steal the *pattern* (semi-empirical fitted polynomial + named source, with the CFD disclaimer) not the *equations*.
- NanoGUI / the CMake 2.8 build / the serial hardware box. Unity + .NET already solve these better; the lesson is only "do not let the UI framework become a frozen fork inside your repo."

## 10. Caveats and what was not checked

- The full Malec et al. 2020 paper is paywalled (Elsevier, ScienceDirect). I read the abstract and highlights only. **No quantitative validation figures (error %, named transients, comparison plots) were recovered from any open source.** Any claim that RRS was "validated to X%" would be invented; I have not made one.
- I did not build or run the simulator (read-only task; also the only release binary is Windows). UI claims rest on the code (`SimulatorGUI.cpp`, 3,885 lines) and the three IJS manuals, which agree with each other except for the small discrepancies noted (period default, max speed, pulse scram delay).
- The official site (reactorsimulator.ijs.si) currently states Linux/macOS builds are "not supported yet" while the README says all three were tested, both statements are reproduced above as found; the shipped artefact is Windows-only.
- JSI TRIGA background facts (250 kW Mark II, first criticality 31 May 1966) are from the IAEA/JSI papers found in search; the 59-fuel-element count is from a code comment in `Simulator.h`.
- reactor-sim phase mapping uses the phase list given in the task (Phase 1 done; Phases 2–8 planned). I did not re-read the reactor-sim repo for this report.

## Sources actually read

- GitHub API: repo metadata, 28-commit history, 6 issues (#1, #2, #3, #4, #5, #6), 1 release (v1.4.1), contributors, languages, recursive file tree.
- `README.md` (full text, 1,246 bytes); `.github/workflows/cmake.yml`; `.gitmodules`; `CMakeLists.txt` (target definition, NanoGUI vendoring).
- `include/Simulator.h`, `include/Settings.h`, `include/ControlRod.h`, `include/PeriodicalMode.h`, `include/ScriptCommand.h`, `src/ScriptCommand.cpp`, `src/Simulator.cpp` (full), `src/SimulatorGUI.cpp` (full, via targeted reads: tabs, graph, pulse, serial, keyboard, speed, save/load).
- Physics Manual (IJS-DP 12880, Toškan/Malec/Snoj, reactorsimulator.ijs.si), full text; User Manual (IJS-DP 12393), full text; Developers Manual (IJS-DP 12882), full text.
- Malec, Toškan & Snoj 2020, ScienceDirect abstract/highlights page only (doi:10.1016/j.anucene.2020.107630).
- Official site reactorsimulator.ijs.si (homepage scope box + system requirements); JSI F8 programs page (purposes list).
- EPJ Web of Conferences 253, 10001 (2021), "On teaching experimental reactor physics in times of pandemic" (remote-course use, student feedback).
- "Project Ideas from the Reactor Physics with Python Course" (Zenodo, Uppsala), the six-exercise sequence using the JSI simulator, §4.3.
- Snoj et al., "A half-century of nuclear research, education and training: Story of the JSI TRIGA reactor", *Annals of Nuclear Energy* 214 (2025) 111122, background on the reactor's training role (via search result, not full text).
