# RbmkSimulator (hartrusion/RbmkSimulator), deep-dive research

**Researched:** 2026-11-25, read-only via GitHub API (`gh.py` helper + repo tree/contents),
the project homepage, and the published startup manual. I did not run the
simulator, did not read the `.ods` dimensioning spreadsheets' contents, and did
not verify download counts (GitHub does not publish them via this API).

**Repo facts (API, 2026-11-25):** Java, GPL-3.0, 96 stars, 7 forks, created
2025-10-22, last push 2026-11-20. Solo developer: Viktor Alexander Hartung
(`hartrusion`, 690 of 694 commits; the other 4 are Copilot). 14 open issues,
44 closed. Homepage: <https://hartrusion.com/en/rbmk-simulator/>.
Latest release v0.4.7, 2026-11-20. Language split: Java 2,253,611 bytes,
PowerShell/Shell ~9 KB each.

**One-line summary:** a Java/Swing simulator of Chernobyl Unit 4 that is
explicitly *not* a reactor-physics simulator, it is a thermo-hydraulic plant
operations simulator with a reactivity puppet-show on top, and it is good
precisely because it admits that and scopes everything else around it.

---

## 1. Stated scope, the most important design decision in the repo

README, verbatim in substance:

> "This simulation focuses on the thermo-hydraulic systems and the general
> operation of power plants. It has no accurate neutron simulation and has many
> systems simplified to allow running even on low end hardware. The general
> approach is to show the connection between various systems, not an exact
> replica of the plant itself. This is about generating steam, condensing it
> and doing a turbine warmup with sync and ramp up to full power."

The homepage adds: "simplified in a way that it can be handled by one person.
It is quite difficult for one person to be honest." Inspiration is the ~30-year-old
Simgenics `rxmodel` simulator behind `gdzx/chernobyl-simulator`, and the 1998
game *Chernobyl: The Legacy Continues*, for which the author once wrote a startup
manual. The author worked in coal-fired plants and on real control systems
engineering before moving to food-machinery manufacturing, the operations
bias is biographical, not accidental.

**Steal this first:** reactor-sim should state its scope this bluntly, in the
README and in-game. "Deterministic PWR sim game" is not a scope statement;
"you will learn to start up, sync, and ramp a 160 MWt SMR; neutronics are
6-group kinetics, thermal-hydraulics are IAPWS-IF97, everything else is
simplified" is. RbmkSimulator's honesty about what it is *not* is what lets
players forgive everything it simplifies.

---

## 2. Systems modeled

### 2.1 Control panels (15, one per subsystem)

From `src/com/hartrusion/rbmksim/gui/panels/`:

| Panel | What it covers |
|---|---|
| PanelCoreControl, PanelRodPositions | Rods, RPS reset, neutron setpoint, ORM, flux log gauge |
| PanelMCP | Main circulation pumps, suction valves, trim valves, cavitation/cooldown margin |
| PanelFeedwater | Feed pumps, 3-valve startup chain (reducing / shutoff / regulation) per drum |
| PanelCoolant | Main + aux coolant pumps, cooling-pond circulation |
| PanelCondensation | Main condenser, vacuum, startup ejectors |
| PanelAuxCondenser | Auxiliary condensation (startup pressure control) |
| PanelDeaerators | Deaerators, DA steam/pressure |
| PanelPreheaters | LP preheaters, condensate |
| PanelTurbine, PanelGenerator, PanelBypassValve | Turbine, generator/breaker, turbine bypass |
| PanelBlowdown | Blowdown/cooldown, aftercooler, regenerator |
| PanelEccs | ECCS (skeletal, see §6) |
| PanelPressureSetpoint | Drum pressure setpoint (separate panel since v0.4.4) |

Plus 9 mnemonic (schematic) displays, Core, Loop, Turbine, Feedwater,
Condensate, Deaerator, Preheaters, Auxiliary Condenser, Blowdown, drawn as
SVG in Inkscape and converted to Java2D drawing code (see §5.3).

Simplifications the author discloses on the homepage: **2 steam drum
separators instead of 4; 37 rods (5 automatic, 28 manual, 4 short) instead of
170; one turbogenerator with steam reheater.**

### 2.2 The full startup sequence (from the published startup manual)

The manual is the best artifact in the project. The cold-start chain, in order:

1. **Cold shutdown, decay heat on blowdown.** "The simulation starts in a full
   shutdown state after some maintenance… the reactor is cooled by natural
   circulation and the decay heat is removed by the Blowdown/Cooldown system."
   Many alarms active, RPS locked.
2. **Fill-up.** Makeup pump → hotwell (never below 5 cm or pumps trip);
   condensate path → fill both deaerators to 100 cm; feed pumps → fill both
   steam drums via the 3-valve startup chain. Manual explicitly warns that
   auto modes only cover full-load operation and startup needs manual overrides.
3. **MCPs.** Trim valves to ~40% *before* starting pumps (cavitation
   avoidance, "you can see a cooldown value in Kelvin on the MCP control
   panel, this refers to the boiling point of water on given pressure").
   Blowdown to passive flow, 1 MCP per side, then 3 per side. Stable ~40 °C
   in drums.
4. **Heatup.** Main coolant pump on, aux condenser as pressure sink; DA steam
   at 10–20%; drum pressure setpoint is a *function of thermal power* (viewable
   as an X/Y diagram). Aux-condenser level setpoint "someone seems to have
   forgotten", set to ~65 cm.
5. **Reactor startup.** Reset RPS; pull auto rods ~1/3, then manual rods one
   by one until reactivity ≈ +0.00100; watch the flux log gauge and the neutron
   *rate* red light; reach 2% flux, enable Global Control, ramp to 150 MWth
   (4.8 %N). "Note that the reactor will scram itself if the auto rods are
   unable to get the reactivity below 0.0."
6. **Vacuum.** At 15 bar, startup ejectors to 100%, evacuate condenser, bypass
   valves to auto, then close aux-condenser steam valves so the bypass takes
   over pressure control.
7. **Turbine warmup, 4 hold points** (the single best table in the project):

   | Speed | Pressure | Reactor power | Neutron setpoint |
   |---|---|---|---|
   | 750 rpm | 15 bar | 140 MWth | 4.7 %N |
   | 1500 rpm | 27 bar | 206 MWth | 7.0 %N |
   | 2250 rpm | 38 bar | 263 MWth | 8.8 %N |
   | 3000 rpm | 50 bar | 326 MWth | 10.9 %N |

   "It is recommended to not operate the turbine on different speeds for a long
   period of time as vibrations outside those hold points will occur."
   Procedure: control-hydraulics pump on → disengage turning gear → reset
   turbine protection → open all 4 fast-closing valves → 750 rpm target →
   startup valves to auto. Lube-oil: 2 aux pumps, one can be switched off once
   shaft-driven oil pressure rises.
8. **Sync and ramp, the manual stops.** It ends mid-sentence:
   "[To be continued] – increase power, set next hold point and so on and sync.
   Reheater will be used when sync is done. watch alarms." The sync/ramp
   phase, the thing the README promises ("sync and ramp up to full power"),
   is the least documented part of the game. That is a real gap, not a nit.

### 2.3 Turbine model (`Turbine.java`, 1,102 lines)

Genuinely detailed for a game: rotor modeled as a mechanical network
(inertia, friction, turning gear as a flow source); HP/LP **differential
expansion trips at 1.5 mm / 3.4 mm** (v0.3.4); turning gear must be disengaged
before spin-up (v0.4.2); 2 aux lube-oil pumps with "need 2 for idle" tuning;
control hydraulics gate the fast-closing valves; turbine protection system
with reset; cooldown to environment modeled. Sync is a real check in code
(`Turbine.java` breaker handler): speed must be **2997–3005 rpm** and the
grid angle within **±0.1 rad** of 0, else "Sync failed." Once synced, speed
is forced to exactly 3000 rpm. Generator breaker opens on reverse current
flow (v0.3.0). There is a dedicated `Synchroscope` GUI widget. Full-load
stationary state is tuned to exactly **3200 MWth → 1000 MWel** (v0.3.0).

### 2.4 The accident

v0.4.5 added a scripted prompt-neutron-excursion experience: hold ~50% load
for ~15 min until xenon effects are gone, reduce to test level, press AZ-5.
v0.4.0 shipped three bundled save files (full load, half load, accident
condition) so players can jump straight to the interesting states, an
onboarding device reactor-sim should copy shamelessly (see §7).

Other modeled faults, from release notes: fuel-channel rupture with leakage
(v0.3.1, v0.4.4), single-channel meltdown with fuel temperature capped at
7000 °C (v0.4.3), MCP cavitation, feedwater reverse flow on extreme drum
pressure, reheater condensate drain crash fix (v0.4.4: "70 cm invisible
level"). ECCS exists as panels/pressurized vessels/sprinkler pumps but was
still being built through v0.3.2–v0.4.3 and its behavior "on accident or
channel rupture is still untested" (v0.4.0 notes).

---

## 3. Thermo-hydraulics without neutron simulation, the tradeoff

### 3.1 How the thermal side actually works

- **Engine:** `PhxNetMod` (`hartrusion/PhxNetMod`, MIT, 10 stars), a
  "physical network simulation" library: the plant is a network of nodes,
  flow resistances, capacitances and effort/flow sources solved by a
  domain-analogy solver (nodal analysis with Norton transform, per v0.3.5
  notes; dedicated solvers per subnet shape added in v0.4.7, e.g.
  `FuelCellSolver`, `ThreeEffortsOneResistorCircle`). Phased (two-phase
  steam/water) fluid elements, heat exchangers, condensers, superheaters.
- **Cycle:** `MainLoop.java` (251 lines) ticks every **100 ms**
  (`docs/CalculationRun.md`): fire GUI commands → pull previous-cycle voiding
  and core temperature → run one core cycle → run one thermal-layout cycle
  (assemblies → control loops → apply thermal power → solve one timestep →
  publish outputs). Pause supported; **I found no time-acceleration control
  in `MainLoop.java`**, only pause. (Verify in the GUI before asserting
  this as a product gap; it may live in a menu I didn't read.)
- **Scale:** `ThermalLayout.java` is **6,279 lines / 309 KB**, a god file
  holding the entire plant wiring. `ReactorCore.java` 1,678 lines,
  `Turbine.java` 1,102 lines. Total in the 13 core files I read: ~12,000 lines;
  the repo is ~2.25 MB of Java.
- **Dimensioning is documented, not just coded:** `docs/` carries
  `CoolantDimensioning.ods`, `PumpAndValveDimensioning.ods`,
  `ReheaterValveDimensioning.ods`, `TurbineCalculation.ods`, plus SVG
  schematics (coolant, blowdown, turbine model, core layout) and
  `docs/ControlLoops.md` describing the Setpoint / PI / actuator architecture
  and the MVC naming convention (`Main#SteamToDAValve`,
  `Deaerator#PressureSetpoint`, action commands vs. property-change events).
  I did not open the `.ods` files, so I can't vouch for their contents, but
  their existence signals the author sizes components from engineering
  numbers, not vibes.

### 3.2 How the neutron side works (i.e. what "no accurate neutron simulation" means)

From `docs/Reactivity.md` and the code, this is the most instructive part
of the whole repo, because it is a deliberate, documented fake:

- Reactivity is **summed percentage contributions, 0–100%**, fed into a
  **nonlinear state-space flux model** (`NeutronFluxModel.java`, 452 lines).
  No neutron transport, no diffusion, no spatial kinetics, no delayed-neutron
  precursor groups as physics, the "delayed" behavior is a 16 s time constant
  (`T_DELAYED_REACTIVITY`) and an integral term (`K_INTEGRAL = 480`).
- The flux model is tuned so a prompt excursion triggers when k exceeds
  1 + β, with **β = 0.005**, `K_REACTIVITY = 0.0005`, `P_INSTANT = 0.6`
  (60% of power responds instantly), `PROMPT_EXCURSION_RATE = 0.8`. The
  class javadoc works out the arithmetic in the open: a 16.6% absorption
  deficit "will definitely trigger the prompt neutron excursion." The AZ-5
  displacer effect supplies that deficit.
- Reactivity terms (all as readable %):
  - **Rods:** 4 short 6.78%, 5 auto 14.12%, 28 manual 76.23%, plus a 2.87%
    "rod tip effect"; a fully withdrawn manual rod contributes 2.74%.
    ORM is computed from rod absorption and scaled to the real plant's 37.
  - **Void:** % of channel mass vs. boiling-point mass; deadband below
    12.5 %V, then `kV = 1.2·(v − 12.5)²`, so full load ≈ 100% and losing one
    MCP pushes it to ~115%. Calibrated so one MCP trip moves "half an
    automatic rod" (9.4% reactivity ≈ 3.4 manual rods).
  - **Xenon:** nonlinear state-space model, but with **time constants
    compressed ~60×**: `T_IODINE = 240 s` with the code comment "A realistic
    value would be 4 hours but that would be ridiculously for a simulator. 5
    minutes however seem to be more reasonable… things are comfortable with
    2 minutes." Full-load equilibrium = 100 %Xe; 50% load settles at 86 %Xe;
    the iodine pit after rundown peaks at ~83% mapped reactivity.
  - **Graphite effect, admitted fiction.** `GraphiteEffectModel.java`
    javadoc: "The graphite effect is named after the influence of the
    graphite temperature… but **has nothing to do with it**. It turned out
    that xenon is not sufficient to trigger the accident so another effect
    has to be taken into account." `docs/Reactivity.md` says the same. It is
    a hidden accumulator that builds at 20–70% power, vanishes above 40%
    with a fast time constant, and does not decay at 0.1–20%, so holding
    50% then dropping to ~1% eats "most of the reactivity, almost terminating
    the chain reaction." It exists to make the AZ-5 accident script work.
  - **Temperature:** exponential `kT = 100·(1 − e^(−(T−310)/100))`,
    310.8 K cold to 848.0 K full power; calibrated to consume ~3 manual rods
    (8.41%) at full load, mostly felt during startup.

### 3.3 What the tradeoff costs and gains

| | |
|---|---|
| **Gains** | Runs on low-end hardware (an explicit design goal; v0.4.7 is largely a perf rework for this). Deterministic, tunable drama: the author can *choose* time constants that make a good game (xenon in minutes, not hours). Every reactivity term is a readable 0–100% number a player can reason about, the "reactivity display" is a game UI, not a physics readout. The accident is scriptable and reproducible. No dependence on nuclear data libraries or transport codes. |
| **Costs** | It cannot teach real kinetics: no spatial flux shape, no real rod-worth curves, no genuine delayed-neutron dynamics, no Doppler/power coefficient as physics. The graphite effect is a fudge the author labels as such, honest, but it means the sim's signature accident is driven by an invented term. Anyone reusing the *numbers* (void coefficient magnitude, xenon timing) as engineering truth would be misled; the docs mostly prevent this, but the in-game numbers look authoritative. Tuning is manual and fragile: release notes repeatedly mention "rebalancing reactivities for better accident simulation" and "time constants balanced for a proper operator experience." |

**For reactor-sim:** this is the key comparison. Reactor-sim *does* have real
kinetics (6-group, per the brief), that is its moat and it should not be
traded away. But RbmkSimulator shows what to do with the *other* 90% of the
game: compress slow physics (xenon) to game timescales **explicitly and in
the open**, expose every feedback as a readable number, and tune for
"operator experience" rather than numerical precision. The lesson is not
"fake the neutrons", it is "fake the *timescales* deliberately, document the
compression factor, and never fake silently." Reactor-sim's xenon/iodine and
decay-heat timescales will face the exact same choice; RbmkSimulator's answer
(240 s iodine, documented in a code comment) is the pattern to copy, with
real kinetics underneath instead of a state-space puppet.

---

## 4. Release and distribution, how a solo dev ships a playable build

- **Cadence:** 14 releases, v0.3.0 (2026-05-09) → v0.4.7 (2026-11-20), roughly
  every 2–4 weeks while active. Two pauses are disclosed in release notes
  (v0.4.6: "I had to pause the development… this won't change for the next 1
  or 2 months"; v0.4.7: "After a few months of paused development, this release
  has no new features but a large rework").
- **Pipeline:** `.github/workflows/release.yml`, manually triggered
  (`workflow_dispatch`, tag input). Ubuntu job resolves dependencies via Maven
  from GitHub Packages (unversioned snapshots of `PhxNetMod`, `utils`,
  `jmplot`), builds a fat jar with Ant (`.github/build.xml`), then a Windows
  job runs **jpackage** to produce a Windows app image, zipped.
  Release is created as a **draft** with auto-generated changelog (git log
  since previous release, per repo including dependency repos, added after
  issue #39).
- **Artifacts per release (v0.4.7):** `RbmkSimulator-Windows-x64.zip`
  (~52 MB, bundled JRE + `.exe`), `RbmkSimulator.jar` (~1.4 MB),
  `RbmkSimulatorClient.jar` (~1.4 MB), `Libraries.zip` (~0.4 MB, the three
  dependency jars), plus `.ic` save files (`FullLoadSave.ic`, ~59 KB).
- **Install experience:** README: Windows users download the zip, "it contains
  a .exe file that you can just run (no installation needed)"; everyone else
  runs the jar on Java 17+. No installer, no auto-update, no macOS/Linux
  bundle, no code signing mentioned anywhere I looked. The zip is the whole
  UX win: a 52 MB download that a non-technical Chernobyl fan can double-click.
- **Saves:** Java serialization (`.ic` files, `SaveGame.java`,
  `serialVersionUID = 8`). README warns in bold: **"save files are NOT
  compatible with newer versions."** Bundled saves are a feature (jump to full
  load / accident), but the incompatibility is a recurring cost, see the
  flux-jump-on-load fix in v0.4.7 and issue #59 (NPE switching saves).
- **Build friction, disclosed:** issue #17 ("The Github action scripts are
  100% vibe coded and might also need attention"); CONTRIBUTING.md explains
  the multi-repo setup (clone 4 repos, VS Code workspace scripts
  `setup_vsc_workspace.ps1/.sh`, NetBeans GUI builder, "once edited outside
  of NetBeans, they probably won't work anymore"). Building from source is
  genuinely awkward; the release artifacts exist to hide that from players.
- **Community posture:** explicit and unusual. CONTRIBUTING.md: "I do not want
  to have a community build around this project (like Discord, forums and so
  on) as I do not have time to manage all this." "Best thing however is not to
  contribute code. It is open source, not open for all contributions." Code of
  Conduct is values-based (UDHR/EU, "No Z supporters wanted" in the README).
  GPL-3.0 for the sim itself ("I do want to keep the authorship and control
  over it"), while the reusable libraries (`PhxNetMod` etc.) are MIT, a
  deliberate split: protect the game, free the engine. That split is worth
  copying in spirit (reactor-sim's C# core vs. Unity front end is a natural
  place to draw the same line, if licensing ever matters).

**For reactor-sim:** the bar is a double-clickable build with zero
prerequisites. RbmkSimulator achieves it with jpackage + bundled JRE; the
Unity equivalent is a standard player build, which is easier, the risk for
reactor-sim is not packaging but *which* build gets shipped (itch.io page?
GitHub Releases? Steam?). RbmkSimulator also shows the value of shipping
**curated save files as playable entry points** and of auto-generated
changelogs. Its weakness to avoid: save incompatibility across versions
(design a versioned save schema now, in Phase 1, before it hurts) and a
build that only the author can reproduce (multi-repo snapshots, vibe-coded
Actions).

---

## 5. Code structure and architecture

```
src/com/hartrusion/rbmksim/          core sim (13 files, ~12k lines in the ones I read)
  MainLoop.java (251)                100 ms tick, save/load, pause
  RbmkSimulator.java (336)           app entry, server mode, MVC wiring
  RbmkSimulatorClient.java (152)     network client (default 192.168.1.105:26486)
  ReactorCore.java (1,678)           rods, RPS/AZ-5, ORM, reactivity sum
  ThermalLayout.java (6,279)         entire plant wiring (god file)
  Turbine.java (1,102)               rotor, lube oil, turning gear, sync, trips
  NeutronFluxModel.java (452)        state-space flux + prompt excursion
  XenonModel.java (155) / GraphiteEffectModel.java (173)
  FuelElement.java (605) / EvaporatorElement.java
  ControlRod.java (495) / ReactorState.java / SaveGame.java / Subsystem.java
  gui/  panels (15) / mnemonic (9 live schematics) / elements (gauges,
        selsyns, synchroscope, control-loop widgets) / diagrams (jmplot)
  gui/diagrams, gui/widgets
test/  GraphiteEffect.java, XenonBuildup.java, ReactorLayoutViewer.java
      , these are dev tools/viewers, NOT a test suite. No unit tests found.
docs/  Reactivity.md, CalculationRun.md, ControlLoops.md, 4× .ods
       dimensioning sheets, SVG schematics, mnemonics/ (SVG + converter)
```

Key architectural choices:

- **Strict MVC.** `docs/ControlLoops.md`: "No calculations are done at the
  view / GUI level." GUI sends `ActionCommand`s to the controller; the model
  fires `PropertyChangeEvent`s back. Components are named
  (`Main#SteamToDAValve`) and the name *is* the wiring, the GUI builder
  sets a string identifier and the model routes by it. Elegant for a
  one-person Swing app; brittle if a name is mistyped (issue #51: "Ejector 2
  condensate valve close button doesn't work", valve 2 was sending valve 1's
  command).
- **Control loops as objects:** Setpoint (with gradient integrator) + PI/P/PID
  element + actuator; assemblies (`HeatValveControlled`, `HeatFluidPump`)
  bundle model element + commands + events so the codebase stays small.
- **Solvers:** the v0.3.5→v0.4.7 arc is a performance story. v0.3.5 replaced
  the solver with nodal analysis; v0.4.0 went per-fuel-channel; v0.4.4
  **reverted to merged evaporators** ("Do not simulate each channel's steam
  water part… Dramatically reduces cycle time"); v0.4.7 removed parallel
  solver threads in favor of fixed solutions for many subnets ("Issues with
  the solvers race conditions solved by removing old AI slop code"). The
  author repeatedly trades fidelity for cycle time *and says so in the
  release notes*. Also notable: v0.4.7's changelog says "Ignore LLM
  documentation and probe scripts and remove them from the repository" and
  "Remove the start colors…", the author actively prunes AI-generated slop.
- **Network play:** server-client over the same MVC layer (issues #16, #43).
  The sim can run headless as a server (`ServerView`, cycle-time statistics);
  clients connect with the full control-room GUI. Issue #43 reports lag/delay
  in network play, closed 2026-11-21 after review. This is the closest thing
  in the genre to reactor-sim's planned LAN co-op, and it was built on the
  MVC split, not bolted on.
- **GUI tech debt:** NetBeans GUI builder `.form` files throughout; Swing
  with a custom dark theme added in v0.4.7 (replacing FlatLaf experiments , 
  the changelog shows themes added and reverted in the same release). Light
  and dark themes, scalable mnemonics (v0.4.7), image-buffered gauge drawing
  for perf. It works, but the GUI layer is the least portable part of the
  project.

---

## 6. Missing features, its own list and what the community asks for

### 6.1 Open issues (14, as of 2026-11-25)

| # | Title | Age / signal |
|---|---|---|
| #59 | NPE in `HeatFluidPump.run()` switching between two `.ic` saves | 2026-11-23, newest, save/load still fragile |
| #57 | Error after scram (`CalculationException`, heat-energy state) | 2026-10-08, post-scram solver state is a crash vector |
| #55 | Add a grid connection + power management system | 2026-09-25, **no grid model at all**; sync is a breaker check, not a grid |
| #53 | "PLEASE!", Russian translation / user localizations | 2026-08-30 |
| #50 | Drum startup valve closable to −5% | 2026-07-14, input validation gap |
| #48 | Crash from "cold" phase via improper values | 2026-07-03, no input clamping on setup |
| #46 | UI: rod labels, unresponsive reactor controls, save layout | 2026-06-01, "not every one using the simulator is a trained nuclear engineer" |
| #40 | Save control-panel layout (author's own) | 2026-05-09, window layout resets every launch/crash |
| #35 | Alarm sounds + documentation | 2026-04-28, 6 comments, alarm sound is system beeps only (v0.4.2) |
| #32 | Documentation extensiveness | 2026-04-26, "leaves too much to chance" |
| #26 | Add Startup Procedure / checklist feature | 2026-04-08, 3 comments, the #1 onboarding ask |
| #25 | Polishers, polisher regeneration, feedwater mineral treatment | 2026-04-08, low priority |
| #20 | Realistic emergency diesel generators for ECCS backup, incl. 70 s ramp-up delay | 2026-04-02, low priority |
| #6 | ECCS (author's own, oldest) | 2026-02-27, "Title says all, there is no ECCS." Partially built since, still skeletal |

### 6.2 What the closed issues show (44)

The closed list is a better roadmap than the open one: per-channel fuel sim
+ leakage + meltdown (#47), BAZ fast rods (#36, with a request to go from 37
to 211 rods), fuel-rod changing (#56), turbine protection (#34), steam
suppression + safety valves (#7), ECCS pressurized vessels (#5/#32-era work),
low-pressure preheaters (#8), main steam ejectors (#9), blowdown (#21),
network support (#16) and its performance review (#43), release CI/CD (#17),
save/load (#10). The pattern: the author closes things in bursts, often
months after filing, and several "closed" items are partial (ECCS, network
lag). Two issues from 2026-09/10 about performance and parallel-solver
exceptions (#54, #58) were closed by the v0.4.7 rewrite, not by fixes.

### 6.3 The honest summary of what's missing

No grid/load model, no diesel generators, no water chemistry, no fuel
handling beyond a recently added rod-change system, no localization, no
in-game procedures/checklists, no persistent window layout, save
incompatibility across versions, and a crash surface around scram/save-load
edge cases. Documentation is explicitly deprioritized by the author
(CONTRIBUTING.md: "Documenting things is also not recommended at the moment
as there are still too much changes"), which is why the startup manual ends
at sync.

---

## 7. What is genuinely good, what is weak, verdict

**Genuinely good (steal these):**

1. **Scope honesty.** One paragraph in the README tells you exactly what is
   simulated and what is faked. It buys enormous forgiveness.
2. **The startup manual as a game design document.** A real step-by-step
   procedure with numbers (5 cm hotwell floor, 100 cm DA level, 40% MCP
   trim, 15 bar ejectors, the 4-point turbine table) turns a sandbox into a
   game with a win state. The manual's *existence* matters more than its
   completeness.
3. **Hold points with steam-parameter tables.** The turbine warmup table
   (speed / pressure / power / neutron setpoint) is the single most
   copyable artifact: a small table that makes a 20-minute procedure
   legible. Reactor-sim needs the PWR equivalent for its turbine (or, for
   the SMR, the equivalent staged heatup/sync table).
4. **Mnemonic schematics.** Control-room-style live P&IDs, drawn in SVG and
   compiled to Java2D so they stay sharp when resized. This is the visual
   identity of the game, and it is cheap: the converter is one JDK-only
   Java file (`MnemonicSvgConverter.java`). In Unity this is a canvas
   redraw, same idea, better tooling.
5. **Bundled save files as entry points.** Full-load, half-load, accident
   saves ship in the release. A new player can experience the interesting
   10 minutes without surviving the 2-hour startup. This is the cheapest
   onboarding in the genre.
6. **Readable reactivity.** Every feedback is a 0–100% number with a
   documented formula (`docs/Reactivity.md`). Players can reason about the
   plant without a physics degree. Reactor-sim has real kinetics, it should
   still *display* feedbacks this way.
7. **Documented timescale compression.** Xenon at 240 s instead of 4 h,
   stated in a code comment with the reasoning. Copy the practice, not the
   number.
8. **Network play from the MVC split.** The client is the same GUI over a
   socket; co-op fell out of the architecture. Reactor-sim's LAN co-op
   (Phase 2–8 scope) wants the same property: sim core with no Unity
   dependency, thin client.
9. **Perf honesty.** Repeatedly merging/simplifying the model when cycle time
   suffered, and saying so. v0.4.4's evaporator merge is the model for
   reactor-sim's own future perf decisions.
10. **The 100 ms fixed tick + decoupled control loops.** Deterministic,
    testable, and the reason the sim can be reasoned about at all.

**Weak or missing (do not copy; fix in reactor-sim):**

1. **God file.** `ThermalLayout.java` at 6,279 lines is the whole plant in
   one file. It works for one author and would be unmaintainable for a team.
   Reactor-sim's C# core should keep per-system classes with the same
   discipline it already has in Phase 1.
2. **No tests.** The `test/` directory holds three dev tools
   (`GraphiteEffect`, `XenonBuildup`, `ReactorLayoutViewer`), not tests.
   The crash issues (#48, #57, #59) are exactly what a golden-replay /
   scenario test suite catches. Reactor-sim already has 50+ xUnit tests and
   golden replay, this is a real, durable advantage; keep widening it.
3. **Save incompatibility across versions.** Java serialization with a
   "NOT compatible" warning. Reactor-sim should version its save schema
   from day one (it plans progression/operations phases, saves will live
   longer than any release).
4. **No grid, no diesels, skeletal ECCS.** The sync is a breaker-closing
   check; there is no load, frequency, or grid to interact with. For a PWR
   game the grid/load-following story is *the* endgame content, plan it
   early (reactor-sim phases covering operations/progression), don't leave
   it as issue #55.
5. **Onboarding stops at the interesting part.** No in-game checklist
   (#26, open since April), manual ends mid-sync. A game that takes 2 hours
   to reach full power needs the procedure *in the game*, not on a website.
6. **Crash surface at the edges.** Scram, save-switching, and cold-phase
   invalid inputs crash the solver with exceptions. The author's own
   CONTRIBUTING.md invites crash reports with save files attached, good
   practice, but the underlying cause (solver state assumptions) is
   architectural.
7. **Single-developer bus factor + GUI lock-in.** NetBeans `.form` files,
   multi-repo unversioned snapshot dependencies, vibe-coded Actions. The
   Windows zip hides this from players but it is the reason development
   pauses for months.
8. **No time acceleration found** (only pause, in `MainLoop.java`). For a
   game whose signature slow process (xenon) takes 15 real minutes even
   compressed, that is a notable omission, and an easy win for reactor-sim
   (deterministic core + fixed tick = time scale is nearly free).

---

## 8. What reactor-sim should steal, prioritized

Mapped to reactor-sim's context: C# core on .NET 8, Unity 6 front end,
160 MWt SMR, Phase 1 (trustworthy plant) done, Phases 2–8 planned
(walkable buildings, crew, radiation, maintenance, campus,
operations/progression, release hardening).

### Steal now (Phase 2, cheap, high value)

1. **A startup manual with numbers, shipped with the game.** Copy the
   structure (cold state → fill → MCPs → heatup → critical → vacuum →
   warmup table → sync → ramp) and write the PWR/SMR equivalent with real
   setpoints from the Phase 1 model. Publish it as a web page *and* bundle
   it in-game. Do not let it end mid-sync the way theirs does.
2. **A turbine warmup hold-point table.** Four rows: speed / steam pressure
   / reactor power / rod or setpoint target. This is the procedure made
   legible; it costs a table and a few lines of tuning.
3. **Bundled scenario saves.** Ship `cold-shutdown`, `hot-standby`,
   `full-power`, and at least one fault scenario as first-class menu entries.
   This is the single cheapest onboarding improvement available, and it
   sidesteps the "2 hours to fun" problem entirely.
4. **Readable feedback display.** Even with real 6-group kinetics, show
   void/Doppler/xenon/rod contributions as normalized, documented numbers
   the way `docs/Reactivity.md` does. Players learn the plant through the
   numbers; the numbers must be honest about what they are.
5. **Mnemonic-style live schematics.** The control-room P&ID is the game's
   visual identity in RbmkSimulator; in Unity it is a 2D canvas overlay
   with live values. Cheaper than 3D, more informative than 3D, and it is
   what players screenshot.
6. **Documented timescale compression.** Decide xenon/decay-heat compression
   explicitly, write the factor down in code comments and docs, and make it
   a config value. RbmkSimulator's 240 s iodine constant with its "ridiculous
   for a simulator" comment is the template.

### Steal in the operations/progression phases

7. **In-game procedure/checklist system** (their open #26). The manual
   should become checkable steps in the UI: "MCP 1.1 ON → wait for flow →
   trim 40%." This is the difference between a sim and a game, and it is
   the most-requested feature they never built. Reactor-sim's
   operations/progression phase is where this belongs, but design the
   procedure data model early so Phase 1 systems can emit checklist events.
8. **A grid/load model.** Their open #55 is reactor-sim's opportunity: the
   PWR endgame is load-following, frequency, and dispatch. Don't repeat the
   "breaker closes, nothing happens" sync.
9. **Fault scenarios as designed content.** Their accident is a *script*:
   hold 50% for 15 min, drop to test level, AZ-5. Reactor-sim should ship
   fault scenarios the same way, named, scripted, with entry saves, rather
   than relying on emergent failure alone. (Its deterministic core + golden
   replay makes scripted scenarios verifiable in a way RbmkSimulator's
   aren't.)
10. **Network co-op from the architecture, not as a bolt-on.** Their client
    works because the sim core never touches the GUI. Reactor-sim's C#/Unity
    split already has this property, protect it: no UnityEngine references
    in the core, ever. Then LAN co-op is a transport, not a rewrite.

### Steal for release hardening (Phase 8)

11. **jpackage-style zero-prerequisite build.** Their 52 MB Windows zip
    with bundled JRE is the bar: a player double-clicks and plays. Unity
    builds clear this easily, the lesson is to ship that build on GitHub
    Releases from the start, with the jar/zip split (small core artifact +
    full bundle) so non-Windows users aren't second-class.
12. **Auto-generated changelogs from git log, per repo.** Cheap, and it
    keeps releases honest.
13. **Versioned saves from day one.** Their bold "NOT compatible" warning is
    the failure mode to avoid; a versioned schema with migration is a Phase 8
    item that must be designed in Phase 1.
14. **Crash-report path.** Their CONTRIBUTING.md asks for crash reports
    *with save files attached*. Reactor-sim should make that one click:
    export save + log + core state.

### Do not copy

- The god file (`ThermalLayout.java`, 6,279 lines). Keep Phase 1's
  per-system decomposition.
- Java serialization saves. Versioned schema instead.
- NetBeans GUI-builder lock-in. Unity canvases are the equivalent risk , 
  keep UI layout in data/code the team can edit, not only in the editor.
- GPL-3.0 for the game + MIT for the engine is *their* answer to authorship
  vs. reuse; reactor-sim is MIT already, no change needed, just note the
  reasoning if licensing ever comes up.
- The invented "graphite effect." Reactor-sim has real kinetics; if a
  scenario needs a fudge to work, that is a signal the scenario (or the
  model) is wrong, not a license to invent a term. If compression is needed,
  compress time, not physics.
- "No community" as a policy. It works for a solo dev with a day job; a
  game with progression, crew, and co-op ambitions needs players talking
  to each other. The Discord/server question is a Phase 8 decision, but
  "no community" should be a deliberate choice, not a default.

---

## 9. Limits of this research

- I did not run the simulator or inspect the `.ods` dimensioning sheets'
  contents, so claims about component sizing rest on file names and the
  author's descriptions.
- I did not verify download counts, the itch/Steam presence (none found in
  the repo), or whether the Windows `.exe` is code-signed.
- Line counts and file lists are from the `main` branch tree at the time of
  research (last push 2026-11-20, v0.4.7). The `test/` directory contains no
  unit tests as far as the tree shows.
- The "no time acceleration" claim is based on `MainLoop.java` (pause only);
  a speed control could exist in GUI code I did not read, treat as
  "not found," not "proven absent."
- Issue bodies #55, #25, #20, #6, #56 are title-only or one line; the
  "missing features" reading leans on titles, labels, and release notes
  where bodies are empty.

## Sources

- Repo: <https://github.com/hartrusion/RbmkSimulator> (API: repo metadata,
  README, full file tree, 14 releases, 14 open + 44 closed issues,
  `docs/Reactivity.md`, `docs/CalculationRun.md`, `docs/ControlLoops.md`,
  `docs/mnemonics/README.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`,
  `.github/workflows/release.yml`, `.github/build.xml`; source files
  `MainLoop`, `ReactorCore`, `ThermalLayout`, `Turbine`,
  `NeutronFluxModel`, `XenonModel`, `GraphiteEffectModel`, `FuelElement`,
  `ControlRod`, `RbmkSimulator`, `RbmkSimulatorClient`, `SaveGame`,
  `ReactorState`, `SpeedSelect`).
- Homepage: <https://hartrusion.com/en/rbmk-simulator/> (feature list,
  simplifications, motivation, "no real neutron simulation" statement).
- Startup manual: <https://hartrusion.com/en/rbmk-simulator/startup-manual/>
  (full cold-start procedure, turbine hold-point table; ends "[To be
  continued]" before sync/ramp).
- Engine: <https://github.com/hartrusion/PhxNetMod> (MIT, 10 stars,
  "modeling and simulation library for complex physical network systems").
