# Chernobyl Simulator, deep-dive research for reactor-sim

**Subject:** `gdzx/chernobyl-simulator`, https://github.com/gdzx/chernobyl-simulator
**Original game:** *Chernobyl: The Legacy Continues*, Gatekeeper Technology Group / Silsoft Software Development, 1998, Windows 3.1 / Windows 95
**Researched:** 2026-11-25, read-only (GitHub API via `gh.py`, release assets, author's blog; game not run)
**Repo state at research time:** 252 stars, 12 forks, 10 watchers, 0 open issues (20 closed, 0 PRs ever), created 2021-11-02, last push 2026-05-02, default branch `master`, language Visual Basic 6.0 (actually VB3 sources, see §8), license GitHub-classified "Other" / `NOASSERTION`

## Sources actually read

- `README.md` (repo, 3,548 bytes), port description, release table, Extras menu, hacking notes
- `setup.nsi` (repo), NSIS installer script, version 0.3.0
- `LICENSE.rtf` (repo), © 1998 Silsoft / Gatekeeper, all rights reserved; otvdm GPLv2
- Releases API: v0.1.0 (2021-12-08), v0.2.0 (2025-10-04), v0.3.0 (2026-05-02), with asset sizes and download counts
- Issues API, all 20 issues (state=all), all closed
- Full recursive tree (458 files) and file-level reads of the decompiled VB3 sources: `RXMODEL/MODULE3.BAS` (58 KB, core UI glue), `RXMODEL/MODEL_TI.FRM` (timer / random-failure roll), `RXMODEL/MAINFORM.FRM` (menus), `RXMODEL/FO_REPOR.FRM` (Field Operator's Report), `RXMODEL/DIAGNOST.FRM` (diagnostic mode), `RXMODEL/BUDGET_C.FRM` (Budget Change Summary), `RXMODEL/ALARMMDI.FRM`, `ICSELECT/ICDISPLA.FRM` (level select), `ACCOUNT/BALANCE_.FRM` (Accounting Balance Sheet)
- Converted manual PDF (`chernobyl-manual-conv.pdf`, v0.1.0 asset, 2,384,108 bytes, 6,064 downloads), full text extracted with `pdftotext`, 3,251 lines. This is a conversion of an original WordPerfect draft; README warns its content "may slightly differ from the final version." The scanned manual (`chernobyl-manual-scan.pdf`, 41,883,647 bytes, 6,825 downloads) was not text-extracted.
- Author's guide: https://dzx.fr/blog/getting-started-with-chernobyl-simulator/ (gdzx, "Getting started with Chernobyl Simulator"), read in full. This is the single best explanation of how the game actually plays; the manual is more reference than tutorial.

## What I did NOT do / limits of this research

- I did not run the game (625 MB Windows installer, needs otvdm on Windows). All gameplay claims come from the manual, the blog, and the decompiled UI code.
- The actual plant model lives in `chrnob.dll`, which is **not in the repo**, only the VB3 UI was decompiled and published. Malfunction catalog, trigger probabilities, the budget math (`extfn0492` etc.), and the physics are all DLL calls with obfuscated names (`extfn026E`, `extfn110F`, `extfn0264`). Anything about exact malfunction probabilities or the $/MWh income rate could not be verified from public sources and is flagged as unknown below, not guessed.
- The manual's cost figures are in rubles; the in-game UI shows `$`. The two are used interchangeably in different places (see §4).

---

## 1. What the game is

You operate Chernobyl Unit 3, an RBMK-1000 (3,000 MWt, 1,000 MWe per the manual). Three programs ship in the installer, each launched through otvdm (a 16-bit Windows emulator based on Wine):

| Program | Role |
|---|---|
| `Chrnobyl.exe` | Intro video, leads into Icselect |
| `Icselect.exe` | Level select: four difficulty levels = four initial-condition (.ICD) files |
| `Rxmodel.exe` | The simulator itself |

Plus a fourth VB3 program, `ACCOUNT`, the Accounting Balance Sheet (Budget menu → Office). The port author added an **Extras** menu that was not in the original: Pause, Malfunctions on/off, Fuel (freeze burnup), Videos (block popups). The port was done with explicit permission from SimGenics (successor to the original developers), README names them in Acknowledgements. That permission matters: this is not abandonware redistribution, and it is also **not open source**. `LICENSE.rtf` states "No portion of the associated software may be reproduced in written or electronic form without prior written permission of the author." reactor-sim can steal design, not code or assets.

The game is explicitly *not* a Chernobyl-disaster reenactment. The author's blog: "Chernobyl Simulator doesn't provide a scenario to reproduce the exact conditions of the test on that dreadful night. It is more of an RBMK nuclear power plant simulator than a Chernobyl accident simulator." The manual's §2 is a 10-page history of the 1986 accident (positive void coefficient, graphite control-rod tips, 30+ second rod insertion, no containment, xenon poisoning), presented as context and warning, not as a playable scenario.

---

## 2. What the manual documents

~100 pages (author's description), 12 sections. It is a plant-operations textbook with a game attached, not a game manual with some plant facts:

1. **System requirements / install**, Win 3.1, 8 MB RAM, 486/66 MHz, 10 MB disk, 640×480 256-color VGA (1024×768 24-bit "optimal"), quad-speed CD-ROM, Sound Blaster. The resolution note is telling: "designed to operate best in 1024 by 768… If you must use it in 640 by 480, the screens will be very crowded."
2. **The accident**, history plus the five RBMK design flaws (online refueling, unenriched fuel / poisoning, graphite moderation / positive void coefficient, graphite rod tips + slow scram, no containment).
3. **File and menu basics**, Initial Condition (.ICD) files: save/restore any state at any time. This is the game's save system and its scenario system in one.
4. **Thermal plant overview**, teapot-and-pinwheel analogy, Rankine cycle, ~33% efficiency, steam tables, why drum level is "one of the single most critical concerns of an operator."
5. **How to start a large piece of equipment**, the universal pump-start ritual: suction valve open, discharge valve closed, start, wait for speed, open discharge. Cavitation, NPSH, permissives. This one procedure is reused for every pump in the plant and is the game's core mechanical verb.
6. **Plant cycle, system by system**, a numbered P&ID walkthrough (water treatment pumps, condensate storage tanks, condenser, hotwell, deaerator, feed pumps, ECCS, HEPA/ventilation, turbine, generator…). Each item gives controls, permissives, alarm thresholds, and **repair costs** (see §4). This section is the game's real content: every system is a small puzzle with failure modes priced in rubles.
7. **Reactor "quick" operational basics**, rods, flux, xenon, voiding, scram.
8. **Reactor refueling**, online refueling, fuel burnup, the drag-and-drop fuel handling ritual (see §3.4).
9. **Steam turbine basics**, rolling, vibration, differential expansion, steam seals, vacuum.
10. **Generator and electrical basics**, synchronization with a synchroscope, breaker closure angle.
11. **Step-by-step plant startup procedure**, 48 numbered steps from cold/offline to 100% power (see §5).
12. **User tips, secrets**, malfunction toggle, diagnostic mode (`/d` command-line flag: pick a malfunction by number, force a fuel-channel leak, play movies).

Also shipped in-game: a "training room" help system with slide decks and an interactive schematic that links each system to its description, plus a "5 minute tutorial on Chernobyl."

---

## 3. Malfunction system design

### 3.1 How faults are introduced

Random failures are rolled on the simulation timer. From `RXMODEL/MODEL_TI.FRM` (the timer tick handler, decompiled VB3): every tick batch, if random failures are on (`gv0090`), the code draws `Int(Rnd(...) * 1000)`, a number 0–999, maps it to a malfunction number 1–100 (`m0142 = 1 + roll − Int(roll/100)*100`), asks the DLL (`extfn110F`) whether that malfunction fires, and if so triggers it via `extfn0264(m0142, 1)`. The timer also tracks the running failure rate for the diagnostic display (`m013E / m0140`, shown as a percentage). The catalog of ~100 numbered malfunctions and their per-tick probabilities live in `chrnob.dll`, not in the repo, so the exact list and rates are unverifiable from public sources. What is verifiable:

- Failures are **random, continuous, and backgrounded**, they can strike any system at any time while the plant runs, not only at scripted moments.
- Difficulty scales the *starting* breakdown load: the four IC files are described in `ICSELECT/ICDISPLA.FRM` as "Minimal breakdowns" (easiest) → "More breakdowns" → "Substantial breakdowns" → "Substantial maintenance problems" (hardest). Random failures then add more on top.
- There is a global kill switch (malfunctions on/off) in the Timer/Options window and in the port's Extras menu. The manual says beta testers asked for it "to operate the plant without malfunctions… and allow them time to learn the operation without constantly fighting breakdowns and failures." The author's blog advises new players to turn malfunctions off until comfortable: "If you don't, you will receive alarms about random failures in some part of the plant you may not know about yet."

### 3.2 The three kinds of malfunction (author's blog, §5, the clearest taxonomy)

1. **Video + contractor offer.** Some malfunctions trigger a video from the supervisor describing the issue (the game calls these "sermons", they "teach you how (not) to operate Chernobyl under pressure from your supervisor"). After the video, the **Field Operator's Report** opens and offers to have a contractor fix it automatically for **$2,000**, "but it is cheaper to repair it yourself."
2. **Silent malfunction, self-repair.** No video, no hint. You must notice it yourself from trends/schematics, diagnose it, and fix it from the relevant control panel. Example from the blog: DA level dropping while hotwell level rises → not enough water hotwell→DA → DA flow valve closing instead of opening → automatic control fault. Fix: switch DA level control to manual and force the valve open, **or** click "A" to confirm automatic control, which opens the Field Operator's Report to repair the equipment for **$200**, "1/10th of the contractor's rate."
3. **Menu-only repair.** Faults you cannot fix from any control panel, e.g. replacing an exhausted HEPA filter, repairing condenser tube leaks. These live only in the **Repairs / Replacements** menu (`MAINFORM.FRM` lists 9 items):
   - Repair Condenser Rupture Disk
   - Repair Air Ejector
   - Repair S/D Heat Exchanger Loop-1 / Loop-2
   - Replace HEPA Filter
   - Cleanup Dropped Fuel Bundles
   - Repair Condenser Tube Leak
   - Repair Diesel ECC Pump
   - Repair One Ruptured Fuel Channel

The 10:1 contractor-vs-DIY price ratio is confirmed in code, not just the blog. In `MODULE3.BAS`, function `fn0528` has two branches: when equipment "cannot be operated until it has been repaired," the Field Operator's Report shows the DLL's base price; when a malfunction is active, the contractor price is the DLL price **× 10** (`l0202% = extfn026E(gv008C, 1) * 10#`) while DIY repair costs the base price. The report's own text: "There is an equipment malfunction. An expensive contractor can troubleshoot and repair it, or you can find the problem and repair it yourself for much less money."

### 3.3 How the player detects faults

Detection is layered, and deliberately imperfect, this is the game's central skill:

- **Alarms window** (`* ALARMS *`, with an ACK button; "while unmuted, new messages ring the alarm until you click on the acknowledge button"). The blog's warning: "by the time a message appears, it may already be too late."
- **Trend charts**, the manual's startup procedure insists on 9 trends up at all times (reactor level, DA level, hotwell level, feed flow, steam flow, neutron rate, neutron flux, thermal power, condenser vacuum), each assigned a pen color. The blog: "Most of the malfunctions won't cause any alarm unless a serious deviation from nominal parameters is detected, so you should always keep an eye on the trend charts and the schematics to identify issues before they can cause any harm."
- **Schematics**, flow/pressure/temperature overlays per system; used to trace a symptom to its source (the blog's offline exercise is entirely a schematic-deduction puzzle: rising hotwell level → reactor drain → expanding water → rising core temperature → decay heat with no cooling → start the Off-line Core Cooling System).
- **Videos/sermons**, only for kind-1 malfunctions; they hand you the diagnosis but cost you the contractor temptation.
- **Physical symptoms in the plant**, e.g. a dropped fuel bundle is only revealed when "the ventilation stack monitor detects abnormal radioactivity levels."

### 3.4 What makes them fun rather than frustrating (and where it fails)

**Fun:**
- **Diagnosis is the game, not the repair.** The repair itself is often one click (a menu item, a mode switch, a valve). The challenge is *finding* the fault from indirect evidence, a level drifting the wrong way, a valve moving opposite to its command, two trends diverging. The blog's DA-valve example is a perfect miniature detective story: two pumps on, control in AUTO, level falling, valve closing = the automatic control itself is the fault.
- **The 10:1 price ratio makes knowledge pay.** Every fault is a bet: pay $2,000 to make it go away, or diagnose it yourself for $200. That is a clean, legible skill-reward loop, the better you understand the plant, the cheaper it is to run. It also creates the game's best tension: you are *allowed* to buy your way out, which makes choosing not to feel clever rather than forced.
- **Faults cascade physically, not arbitrarily.** A DA level loss doesn't just subtract money: in ~3 minutes the feed pumps lose suction and are ruined, the reactor scrams, ECCS fires cold water into a hot core, and restart takes "at least two days." A discharge temperature over 140°F in the shutdown-cooling loop damages the heat exchanger ("radiation leak in SDC HX-1"). A flooded DA floods the HEPA filters. The player learns causality, not a fault list.
- **The off switch exists and is guilt-free.** Malfunctions off is a first-class, documented feature, recommended for learning. The port adds the same toggle to the Extras menu. The game never punishes you for playing without faults, it just makes less money sense (see §4).
- **Faults have a time dimension, not just a trigger.** The offline-cooling exercise shows decay heat falling over time, forcing the player to keep re-trimming flow. Fuel burnup, xenon poisoning, and HEPA loading are slow faults that reward planning over reaction.

**Frustrating (documented, not inferred):**
- **Silent faults + no in-game fault list.** The blog is blunt that most malfunctions give no alarm until serious deviation. For a new player this is indistinguishable from "the plant broke for no reason." The only mitigation is the manual and the tutorial, a 100-page reading assignment before play.
- **Input unreliability as a fault amplifier.** The fuel-monitor drag-and-drop "doesn't always register the click release event, which keeps a channel selected and blocks further refueling operations" (blog). The v0.3.0 release (May 2026) fixed "fuel monitor becoming stuck and blocking refueling operations", a bug that *looked like* a game mechanic for years. Issue #15 ("Can't refuel," 6 comments) and #16 (recirc pumps not turning on in `ic0004.ics`) are the same class: the player cannot tell a bug from a malfunction.
- **Overflow crashes.** Issue #4: frequent "Overflow" errors under winevdm 0.9.0 "when operating pumps or valves", the exact moment a player is responding to a fault.
- **No fault history or post-mortem.** Nothing in the sources I read logs what failed, when, or what it cost. You learn by losing.

### 3.5 Refueling as a recurring "malfunction-adjacent" loop

Worth separating because it is the game's other core loop. Fuel burnup is a slow, visible depletion: the Fuel Burn-up display shows remaining reactivity; when all absorber rods reach 0% inserted you are "guaranteed to lose criticality." Diagnosis is per-channel: drag the Fuel Monitor over a fuel channel, align the radiation symbols, wait for blue = selected, read remaining reactivity and rod count. Each channel holds two bundles of 5 rods. Refuel the lowest-reactivity channels first; refueling too early wastes money three ways (lost reactivity, spent-fuel storage cost, new bundle cost). Mishandling is punished physically and financially: dropping a bundle spills radioactive particles, the stack monitor alarms, the HEPA filter wears out, and cleanup is an expensive Repairs-menu operation. This is the game's best-designed ritual: slow, tactile, consequential, and entirely avoidable by paying attention. It is also the most fragile input in the game (see above).

---

## 4. Budget loop

### 4.1 How money works

- **Income is power.** The four difficulty levels are defined in money terms, not just power terms: full power = "The plant is making money (easiest)"; 80% = "barely breaking even"; 50% = "losing money and power must be increased"; 0% = "losing a substantial amount of money" (hardest). The startup procedure ends at step 48 with: "This concludes the startup procedure. Congratulations, you are now making money." No explicit tariff ($/MWh) was found in the manual, the blog, or the decompiled UI code, the income math lives in the DLL. I do not know the rate, and the manual never states one.
- **Budget menu → Office / Display / Budget Change.** The `ACCOUNT` program is a separate Accounting Balance Sheet; the `Budget Change Summary` window shows five line items (labels 0–4 in `BUDGET_C.FRM`; the row labels are baked into the form's background bitmap, so their text is not recoverable from the decompiled source). The blog's screenshot of the offline exercise shows the summary after a bout of off-line operation, the point being that even *not running* costs money.
- **Starting budgets are small and concrete.** The blog's offline exercise file `IC_INIT.ICD` gives "maximum fuel, malfunctions off, and a $12,000 budget." Against the cost table below, $12,000 is about six contractor repairs, or one mistake.

### 4.2 Verified cost table (manual, rubles; game UI shows $, the manual and UI do not use the same currency symbol)

| Item | Cost | Source |
|---|---|---|
| Demineralized makeup water | 5 rubles / gallon (tanks 960,000 gal each; "Do not waste it by forgetting to shut the pump off… will dump precious condensate down the drain") | Manual §6 items 1–3; §11 step 4 |
| Contractor repair (any malfunction) | $2,000 flat (blog example) | Blog §5; code ×10 rule |
| DIY repair (same malfunction) | $200 (1/10th) | Blog §5; `MODULE3.BAS` fn0528 |
| HEPA filter replacement | 100,000 rubles | Manual §6 |
| DA flood → HEPA repair | 500,000 rubles | Manual §6 item 16 |
| Condensate / circ-water pump ruined (run dry / no flow path, "within a few minutes") | ~400,000 rubles each | Manual §6 |
| Reactor feed pump ruined (DA level loss) | "slightly over 2,000,000" rubles each | Manual §6 item 15 |
| New fuel bundle + spent-fuel storage | Priced but no figure stated in the passages I read; refueling early "dings your budget heavily" three ways | Manual §8 |
| Scram + ECCS + 2-day restart | "a tremendous amount of rubles due to down time" (no figure) | Manual §6 item 15 |

Note the manual's own inconsistency: feed pumps are "slightly over 2,000,000" in one passage and a 3,500,000 figure appears for a pump class elsewhere in §6 (context not fully verified, do not quote the 3.5M figure without re-checking). The *shape* is what matters: costs span four orders of magnitude (5 rubles/gal → millions), so small carelessness (leaving a makeup pump on) and big carelessness (running a pump dry) are priced on the same ledger and both hurt.

### 4.3 What happens when the budget runs out

The blog states it plainly: **"When you are over budget, you can no longer buy new fuel bundles or repair malfunctioning equipment."** That is the fail state, not an explosion, not a game-over screen, but a soft lock: the plant keeps degrading, faults accumulate unrepaired, fuel burns down, criticality is eventually lost, and there is no way back except restoring an earlier .ICD save. No explicit bankruptcy screen, score, or "game over" text was found in the manual, README, or decompiled UI. The punishment is economic and slow, which fits the game's thesis (the manual's §2 closes by noting the other Chernobyl units were restarted "because of the severe financial desperation of the former Soviet republics" and asking "whether or not they have the financial means to properly operate and maintain" them, the budget *is* the theme).

### 4.4 How money pressure shapes decisions

- **Run vs. repair:** every fault is a $200-vs-$2,000 decision, and every repair competes with fuel purchases and water costs on the same small budget.
- **Waste is priced:** leaving a demineralized-water pump running dumps money down the drain at 5 rubles/gal; running a polisher regeneration is "expensive, but less expensive than having to shut the plant down due to bad water"; keeping pumps in service off-line raises "the electrical bill" (blog §4.3 advises shutting condensate/feed pumps down off-line and controlling drum level with OCCS alone).
- **Prevention is cheaper than cure by ~10–1000×:** a $200 DIY valve fix vs. a 2,000,000-ruble feed pump vs. a 2-day outage. The game teaches this by letting you pay the big price once.
- **Fuel timing is a budget decision, not just a physics one:** burn a bundle to the last percent of reactivity or pay storage + new-bundle costs early. The optimal play is genuinely an economic optimization, not a reflex test.

---

## 5. Pacing, difficulty curve, win/lose

### 5.1 Pacing

- **Model tick:** 100 ms default, 200 ms option for slow machines (manual §12; `MODEL_TI.FRM`). Simulation time is real-time; there is no fast-forward mentioned anywhere in the manual, README, or blog. The port's Pause exists precisely because the game never stops on its own.
- **The 48-step startup is the tutorial, the pacing, and the difficulty curve in one.** It takes the player from cold/offline (OCCS running, everything else off) through condensate → DA → circ water → feed → recirc → steam dump → rod pull to critical → turbine roll in staged speed holds (900 → 1800 → 2700 → 3600 rpm, checking vibration and differential expansion at each hold) → synchroscope → breaker closure → power escalation in 5% steps (5 → 10 → 15 → 20 → 25% → 100%), adding the second condensate pump, second feed pump, second recirc pump (~250 MW), second circ-water pump (~500 MW) at defined milestones. Each step has a reason stated in the manual. This is a genuinely good piece of game design: a long, ordered, checkable procedure where each step unlocks the next system's behavior.
- **After startup, pacing is fault-driven and seasonal, not scripted.** "The rest of the game consists in performing part of this start-up procedure with malfunctions on and trying to survive" (blog). Slow processes set the rhythm: fuel burnup, xenon transients (the manual warns the RBMK "poisons out" below ~80% power, the exact trap of 1986), decay heat after shutdown, HEPA loading.
- **Suggested endgame experiments** (blog §6): regular shutdown from full power, reactor SCRAM, maximum sustained power, low-power operation with high xenon poisoning. The game offers no goals beyond these self-set ones.

### 5.2 Difficulty curve

The four levels invert the usual job-title prestige: **Plant Operator is easiest, Plant Manager is hardest.**

| Level (Icselect) | Power | Breakdowns | Money |
|---|---|---|---|
| 1, Plant Operator | 100% | Minimal | Making money |
| 2, Shift Supervisor | 80% | More | Barely breaking even |
| 3, Chief Engineer | 50% | Substantial | Losing money, must raise power |
| 4, Plant Manager | 0% | Substantial maintenance problems | Losing a substantial amount, "people are tense" |

Difficulty is thus *starting-state* difficulty, not a separate ruleset: lower power = less income = less room to absorb fault costs = harder. Elegant and honest, the same plant, the same faults, just less slack. The blog's recommended learning path exploits this: start at level 1 with malfunctions off, learn refueling; do the offline exercise from `IC_INIT.ICD`; do the startup from level 4 (`IC_0004.ICD`) with malfunctions off; then replay with malfunctions on "and try to survive."

### 5.3 Win/lose conditions, what I found and did not find

- **Found:** soft economic fail (over budget → cannot buy fuel or repair → slow death spiral); physical fail states described in the manual (uncovered fuel → "meltdown and/or explosion"; loss of all circulation → "meltdown and/or explosion"; scram → 2-day restart); the accident history as a cautionary tale.
- **Not found:** any score, any victory screen, any campaign end, any "game over" text, any time limit, any leaderboard or mastery record, in the manual, README, blog, or decompiled UI forms I read. The game appears to be open-ended survival: the win state is "the plant is running and making money," the lose state is "it isn't, and you can't afford to fix it." The closest thing to a goal is the manual's closing dare: "See if you can make the necessary decisions and keep the reactor operating as it should. Good luck! You are definitely going to need it."
- This absence is itself a design fact worth stealing-or-avoiding deliberately (see §10).

---

## 6. Interface and presentation (what the player actually sees)

From the repo's `screenshot.png` (read directly) and the menu/form sources:

- **MDI window chaos, by design.** The main window ("Chernobyl RBMK Reactor Unit - 3 (Malfunctions On !)") is a gray control board of small labeled panels; clicking a panel opens a nested child window (Trend Charts, Steam Drum Flows, Feedwater System, Core Status, Reactor Power Regulation, Feed Pumps, Alarms). The manual says outright the game "is designed to operate best in 1024 by 768" and at 640×480 "the screens will be very crowded", window management *is* part of the difficulty. Right-click brings background graphics forward. The blog calls the interface "a little bit convoluted."
- **Trend Charts** are the primary instrument: black background, multiple colored pens, live traces. The screenshot shows them top-left, always visible in the recommended layout.
- **Core Status** shows the two recirc loops, thermal power, max fuel temp, xenon, recirc flow, the reactor's health at a glance, with the two hottest channels in red.
- **Reactor Power Regulation** is the scariest panel: Automatic Reactor Control on/off, power setpoint, neutron flux (log), thermal power correction, fuel burnup %, AUTO SCRAM control with RESET/TRIP.
- **Alarms window** is a scrolling text list (screenshot shows "REACTOR FEED PUMP TRIP / FUEL TEMPERATURE HIGH IN LOOP-1 / FUEL TEMPERATURE HIGH IN LOOP-2" in cyan on black) with a volume slider and ACK button.
- **Schematics** are the navigation aid: Overview, Reactor Core Status, Steam Turbine Status, Steam Drum Flows, Deaerator Flows, Feedwater System, each a live P&ID with flows, pressures, temperatures.
- **Contextual help:** a help cursor (top-right) that gives info on any panel; the training room's interactive schematic links each system to its description.
- **Videos:** full-screen AVI popups, intro, supervisor "sermons" when things go wrong, hints for fixing malfunctions. Originally Intel Indeo 4; v0.2.0 re-encoded them to Cinepak because Windows 11 removed the Indeo codec. Issue #19 is a forensic analysis of an 8-frame corruption artifact in `INTRO01.AVI` at ~45.5 s, the community cares about this game's videos at the bitstream level.
- **Units toggle:** metric/American in the original Options menu. Issue #7 reports the toggle seems missing in the port, the blog advises selecting American units "to follow the manual indications, although the Soviet Union relied on the metric system."

---

## 7. Distribution

- **Installer:** NSIS (`setup.nsi`, version 0.3.0), installs to `C:\Chrnobyl`, **requires admin**, writes `HKLM\SOFTWARE\Chernobyl Simulator` version keys and an uninstaller. It silently installs the MSVC++ x86 redist (`vcredist_x86.exe /passive /install`), bundles otvdm, icons, and the game files (`chrnobyl\*`, `patch\*`, note: those source directories are **not in the git repo**; the installer is built from the maintainer's local machine, `OutFile "C:\Users\User\Desktop\chernobyl-setup.exe"`).
- **Size:** v0.3.0 installer is **624,964,744 bytes (~596 MB)** for a 1998 game whose repo is 903 KB, the bulk is videos + otvdm + redist. Download counts (as of 2026-11-25): v0.1.0 setup **18,653**, v0.2.0 **4,793**, v0.3.0 **899**; manuals 6,825 (scan) / 6,064 (converted). The v0.1.0 installer is still the most-downloaded by far, most players are on the oldest build.
- **Reach:** three Start Menu shortcuts (Chrnobyl / Icselect / Rxmodel), each a shortcut to `otvdmw.exe` with the 16-bit exe as argument. This indirection is the #1 support burden: issues #3, #6, #8, #11 are all "I launched the .exe directly and Windows says this app can't run on your PC." The game only works when launched *through* otvdm.
- **Release hygiene:** only three releases in five years; v0.2.0 fixed the Win11 video-codec break; v0.3.0 (May 2026) fixed one bug, "fuel monitor becoming stuck and blocking refueling operations." The README's release table still lists v0.2.0 as current and is stale relative to `setup.nsi` (0.3.0). No changelog beyond one-line fixes, no CI visible, no tests.
- **Maintenance reality:** single maintainer (gdzx). All 20 issues closed, 0 PRs merged. Recent issue traffic (2025–2026) is dominated by: hi-res scaling (game "nearly unplayable" on high-res displays, #2/#14, the fixed 1024×768-era panels render tiny, numbers replaced by boxes at 2560×1440), Win11 launch confusion, refueling bugs, and two educational-use enhancement requests (#12/#13: resizable windows, more trend windows, all windows size-adjustable) from a user planning classroom use. The maintainer's decompiled sources were only published in April 2026 (commit `27487af`, "Add decompiled sources"), five years after the port, and issue #18 asks for explicit permission to redistribute the decompiled version, i.e. the community wants to build on it and currently cannot cleanly.

---

## 8. What is genuinely good, what is weak or missing

### Genuinely good

1. **The 10:1 contractor/DIY repair economy.** A single number that turns plant knowledge into money. It is the game's best mechanic and it generalizes to any sim.
2. **Diagnosis-before-repair.** Faults present as *symptoms in trends and schematics*, not as icons with labels. The DA-valve example (valve moving opposite to command = the controller is the fault) is a better puzzle than most "find the broken part" games.
3. **Physical cascades with priced consequences.** Every fault is a chain (level → pump → scram → 2-day outage), and every link has a ruble figure attached. The player learns systems, not checklists.
4. **The 48-step startup as a designed learning curve.** Ordered, checkable, each step explained, milestones at real power levels. It is a procedure a real operator would recognize, and it doubles as the game's pacing.
5. **Difficulty as starting state, not rules.** Four IC files, same plant, less slack. Cheap to build, honest to play, trivially extensible (any saved state is a new scenario).
6. **The manual as a real textbook.** 100 pages that teach Rankine cycle, steam tables, cavitation, xenon, and the accident itself. The game is a pretext to learn; the manual is the payload. The in-game training room + interactive schematic + contextual help cursor make it navigable.
7. **The malfunction off-switch as a learning tool.** Explicitly requested by beta testers, documented, guilt-free. The port's Extras menu (Pause / Malfunctions / Fuel / Videos) is the right kind of modernization: four toggles that remove frustration without touching the model.
8. **Save-anywhere as .ICD files.** Any state is a scenario; scenarios are files players can share. Issue #20 ("Editing saves") shows players poking at this.

### Weak or missing

1. **No goals, score, or end state.** Open-ended survival only. Nothing to *complete*, no mastery record, no reason to return after the first successful startup except self-set experiments. The blog has to invent endgame goals for the player.
2. **Silent faults are indistinguishable from bugs, and sometimes are bugs.** Fuel-monitor stuck bug shipped for years; overflow crashes on pump/valve operation; recirc pumps not starting in one IC file. When the core loop is "notice something is wrong," any engine bug reads as gameplay.
3. **The interface is window-management labor.** MDI child windows, fixed tiny panels, no scaling (the #1 complaint in 2025–2026: "nearly unplayable" on hi-res). No fast-forward, no time control beyond pause. Trend selection is a fiddly click-pen-color ritual.
4. **The model is a closed DLL.** The repo publishes decompiled VB3 UI only; `chrnob.dll` (physics, malfunctions, budget) is a binary blob. The community cannot fix, extend, or verify the model, issues #17/#18 are users asking for the decompiled code and redistribution permission. For reactor-sim this is a cautionary tale about what "open" means.
5. **No fault history, no post-mortem, no tutorialization of failure.** When you lose the plant you get no debrief: what failed, in what order, what it cost. The manual is the only teacher and it is 100 pages.
6. **Refueling input is fragile by design.** Drag-and-drop with symbol alignment and a click-release bug that blocks further refueling. The most tactile loop is also the most brittle.
7. **Distribution is a 596 MB admin-required installer for a 900 KB repo**, launched through an emulator shim, with the #1 support issue being "I clicked the exe." No portable build, no Steam/itch page, no macOS/Linux path (issue #10: macOS via Porting Kit fails).
8. **License limbo.** "All rights reserved" original + permission to distribute this port, but no license grant for reuse, translation (issue #5 asks), or modification. The community's educational-use requests (#12/#13) have nowhere to go.
9. **The disaster is context, not content.** Deliberate (see §1), but it leaves the game's most famous hook, the night of April 26, 1986, entirely unused. The author says he plans to "remedy" this in a future article.

---

## 9. What reactor-sim should steal (concrete, mapped to its phases)

reactor-sim context for this section: C# core on .NET 8, Unity 6 front end, 160 MWt SMR, Phase 1 (trustworthy plant) done; phases 2–8 planned covering walkable buildings, crew, radiation, maintenance, campus, operations/progression, release hardening. The steals below are ordered by value, not by phase.

### Steal 1, The 10:1 contractor/DIY repair economy (Phase 6 operations/progression, Phase 5 maintenance)
Copy the shape exactly: every fault offers an expensive instant fix and a cheap fix that requires correct diagnosis. In reactor-sim terms: a "call the vendor / fly in a specialist" button priced ~10× the parts-and-labor cost of doing it yourself from the right panel. This single mechanic does three jobs: it prices knowledge, it gives stuck players an out that costs them (so using it feels like a decision, not a cheat), and it creates the budget tension that makes the rest of the economy matter. Price it in the same ledger as fuel and consumables so the trade is always visible.

### Steal 2, Diagnosis as the core skill, with layered, imperfect detection (Phases 2, 5, 6)
Do not label faults. Present them as Chernobyl does: alarms (late, and only on serious deviation), trend charts (early, if the player looks), schematics (for tracing), and physical symptoms. Then improve on Chernobyl in exactly one place it fails: keep a **fault log / post-mortem** (what failed, when, what it cost, what the player did). Chernobyl teaches diagnosis but never confirms the lesson; reactor-sim can close the loop after every incident without dumbing down detection.

### Steal 3, Physical cascades with priced links (Phase 1 core, Phase 5 maintenance)
Every fault in Chernobyl is a chain with a ruble figure on each link (DA level → feed pump ruined → scram → 2-day outage). reactor-sim's deterministic core is *better placed* to do this than Chernobyl's DLL was. Design each fault as a causal chain with time constants ("in ~3 minutes the pumps lose suction"), not as a stat debuff. Attach a cost to each link. The player should be able to narrate, after the fact, why the plant tripped.

### Steal 4, The 48-step startup as a designed, checkable procedure (Phase 6 operations/progression)
Chernobyl's startup is the best onboarding in the game: ordered steps, each with a stated reason, gated by real plant milestones (rpm holds with vibration/differential-expansion checks, power steps with equipment additions at 80/250/500 MW). reactor-sim's planned 13-step startup is the same idea, keep it, and steal two details: (a) **hold points with checks** (don't just reach a value; verify a second parameter before proceeding), and (b) **the procedure as the pacing device**, the startup *is* the early game, not a chore before it. Also steal the inverse: a documented shutdown procedure with the same care (Chernobyl's offline-cooling exercise shows decay-heat management is a game in itself).

### Steal 5, Difficulty as starting state, via save files (Phase 6, Phase 8 release)
Four IC files, same plant, less slack. reactor-sim should ship its scenarios the same way: a named initial-condition file *is* a difficulty level. "Full power, healthy plant, fat budget" → "cold, broken, broke." And since any saved state is a scenario, players (and the team) can author new ones for free. This is cheaper and more honest than a separate difficulty ruleset.

### Steal 6, The malfunction off-switch, and the port's Extras menu, as first-class features (Phase 8)
Pause, malfunctions off, fuel-burn freeze, video/cutscene skip, four toggles that Chernobyl's port added because players asked. Build these in from the start, not as debug flags: a new player who can turn faults off while learning the plant will stay; one who cannot will bounce. Same for a "sandbox / no budget" mode. Chernobyl proves the toggles don't cheapen the game, they are what make the hard version playable.

### Steal 7, The manual as a real textbook, in-game and linked (Phases 2, 6)
A 100-page manual is absurd and also the reason this game still has a community 28 years later. reactor-sim should steal the *structure*, not the length: a system-by-system reference where every system page states controls, permissives, alarm thresholds, and repair costs; an interactive schematic that links each component to its page; a contextual help cursor. The in-game training room (slide decks + clickable P&ID) is the 1998 version of what reactor-sim's walkable control room can be, the same content, spatial instead of windowed.

### Steal 8, Slow faults as the long game (Phases 3 radiation, 5 maintenance)
Fuel burnup, xenon poisoning, HEPA loading, decay heat, faults that develop over hours and reward planning over reaction. These are what make the plant feel alive between emergencies. reactor-sim's planned radiation and maintenance phases are the natural home: make filter loading, fuel depletion, and xenon transients visible, slow, and economically meaningful (early refuel = wasted money, three ways).

### Steal 9, The budget as the theme, with a soft-lock fail state (Phase 6)
"Over budget → cannot buy fuel or repair" is a better fail state than a game-over screen for a sim: it is diegetic, it is the player's own decisions compounding, and it makes the manual's §2 point (plants fail for financial reasons too). But reactor-sim should fix Chernobyl's opacity: **show the income rate**. Chernobyl never states its $/MWh; players cannot plan what they cannot see. Publish the tariff, show projected income at current power, and show the five-line budget summary with *labeled* rows (Chernobyl's Budget Change Summary bakes its row labels into a bitmap, don't do that).

### Do NOT steal (anti-patterns)
- **MDI window chaos and fixed tiny panels.** The #1 modern complaint. reactor-sim's Unity front end should be the fix: one coherent control room, scalable UI, no window juggling as difficulty.
- **Silent faults with no debrief.** Keep silent detection; add the incident log and post-mortem Chernobyl lacks.
- **Fragile drag-and-drop as a core ritual.** Refueling's tactility is worth copying; its click-release bug and symbol-alignment fussiness are not. If a physical interaction can soft-lock the game, it needs a cancel/reset path.
- **A closed core.** Chernobyl's community is stuck asking for permission to extend a game they love. reactor-sim's deterministic C# core is its moat, keep it inspectable, testable, and versioned (golden replays already do this; don't regress into a DLL-shaped black box).
- **596 MB installer for a small game, admin-required, emulator-shim launch.** The distribution lesson is negative: the port's reach is limited by its packaging, and its most-downloaded build is five years old. reactor-sim's Phase 8 should treat "double-click and it runs, no admin, no shim" as a feature, and keep the README release table current (Chernobyl's still lists v0.2.0 after v0.3.0 shipped).
- **No win condition at all.** Chernobyl survives on its community's self-set goals. reactor-sim's Phase 6 (operations/progression) should define explicit ones, a scored startup, a budget target, a campaign of scenarios, while keeping the open-ended survival mode alongside. The 1998 design proves the sim is fun; it also proves that without goals, retention depends on the player inventing their own.

### One-line summary for the roadmap
Steal Chernobyl's **economy of diagnosis** (10:1 contractor/DIY, priced cascades, budget soft-lock), its **startup-as-tutorial**, and its **difficulty-as-starting-state**, and fix everything around them (goals, debrief, scalable UI, open core, sane distribution) that 1998 couldn't or didn't.

---

## Appendix

### A. Numbers verified in this research (all from sources listed above)

- Repo: 252 stars, 12 forks, 0 open issues, 20 closed issues, 0 PRs; created 2021-11-02; last push 2026-05-02.
- Installer v0.3.0: 624,964,744 bytes; downloads v0.1.0: 18,653 / v0.2.0: 4,793 / v0.3.0: 899; manuals: scan 6,825 / converted 6,064 (counts as of 2026-11-25 via API).
- Contractor $2,000 vs DIY $200 (blog + code ×10 rule); makeup water 5 rubles/gal; HEPA 100,000; DA flood 500,000; condensate pump ~400,000 each; feed pump >2,000,000 each; scram restart ≥2 days.
- IC_INIT.ICD budget $12,000 (blog). Four levels: 100% / 80% / 50% / 0% power.
- Startup: 48 steps; turbine holds 900/1800/2700/3600 rpm; power steps 5/10/15/20/25→100%; second recirc ~250 MW, second circ-water ~500 MW; DA normal level 120", alarms at 100/130/160/180"; OCCS discharge limit 140°F.
- Original specs: Win 3.1, 8 MB RAM, 486/66, 10 MB disk, 1024×768 24-bit optimal.

### B. Open questions I could not answer from public sources

- The ~100-malfunction catalog and per-tick trigger probabilities (in `chrnob.dll`, not in repo).
- The income rate ($/MWh or equivalent) and the five Budget Change Summary row definitions.
- Whether any explicit game-over/score screen exists in the shipped binary (none found in manual, README, blog, or decompiled UI, but the binary itself was not run).
- The 3,500,000-ruble pump figure in manual §6, context not fully verified; do not quote without re-checking the scan.

### C. Links

- Repo: https://github.com/gdzx/chernobyl-simulator
- Releases: https://github.com/gdzx/chernobyl-simulator/releases (v0.3.0 installer, v0.1.0 manuals)
- Author's guide: https://dzx.fr/blog/getting-started-with-chernobyl-simulator/
- Original homepage (archived): https://web.archive.org/web/19990429163349/http://www.gatekeepsw.com:80/
- otvdm (emulator used by the port): https://github.com/otya128/winevdm
