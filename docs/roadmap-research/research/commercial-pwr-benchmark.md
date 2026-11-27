# Island PWR Sim, Deep-Dive Research for reactor-sim

**Researched:** 2026-11-25 (read-only; no purchase, sign-in, play session, or code change)
**Subject:** Island PWR Sim, closed-source commercial PWR plant simulator game
**Official site:** https://[commercial-pwr-product-url-omitted] **Steam:** https://[major-PC-storefront-url-omitted]
**Price / release:** $14.99, published 2023-03-13; Early Access 2020-11-10 (per Steam's Japanese store listing)
**Reception (as read 2026-11-25):** official product site states "Very Positive (84% of 1,092 reviews)", rating 4.2/5. SteamDB's developer page, crawled separately, shows 80.04% and a 227-player peak, treat the two figures as different snapshots/methods, not as a contradiction to resolve here. Steam lists 11 achievements; a GameHypes mirror lists 10. Do not quote either achievement count as settled.
**Developer:** Iván Marcelo Pretti (solo, per multiple secondary sources). Publisher originally Aerilian Games; MicroProse announced Island PWR Sim joining its lineup (GamesPress / MicroProse, undated in the copy read, with a 15% discount at announcement). Unity engine, Photon SDK per SteamDB detected technologies.
**Reactor-sim baseline for comparison:** deterministic PWR sim game, C# core + Unity 6, 160 MWt SMR, phases 1–8 planned covering walkable buildings, crew, radiation, maintenance, campus, operations/progression, release hardening. Phase 1 of 8 done at time of writing (per MEMORY.md).

## Method and limits

Sources actually read: official product site home page and two operator guides (PWR explainer, reactor-accidents explainer, both updated 2026-08-06); the current Steam store page (which now reads as rewritten marketing copy, not the older feature list); SteamDB patch notes for V 2.2.25.205–219 (Jan–Jul 2026) and selected 2023–2025 patches; Steam Community discussion threads on chemical treatment; NuCon README (Python library, Gitea); Dutchevil/Community PWR PID controller README (C#, fork of GHXX's original); GamesPress/MicroProse announcement; GameBrain and Niklas Notes review aggregations; Deku Deals / Techmash summaries; SteamDB DLC listings.

Not verified by playing the game, and therefore **not claimed here**: exact numeric pH range or corrosion rate, exact fault spawn rates / probabilities, exact Prestige costs or XP curve, the full AO skill tree, exact demand-curve numbers, and whether any given bug discussed in 2024–2025 threads still exists in V 2.2.25.219. Where a number comes from a community tutorial rather than the developer, it is labelled as such. Where the official copy and a community source disagree, both are noted.

## TL;DR verdict

Island PWR Sim' real achievement is not fidelity. It is **coupling**: 200+ live parameters, 150+ switches, chemistry, xenon, fuel handling, maintenance, a living demand curve, and a walkable plant are all one system, so a small mistake can cascade and a fix costs time, travel, and money. That is the thing to steal.

Its real weaknesses are the mirror image: the coupling is largely undocumented in-game, the tutorial covers startup only, save/load and AO bugs recur across years of patch notes, performance is inconsistent even on high-end hardware, and multiplayer was removed in Jan 2024 and only returned as an experimental "Fusion 2.0" rebuild in 2026 that is still visibly unfinished. A deterministic reactor-sim can beat it not by being deeper on day one, but by being **explainable, replayable, and stable**: a public API, golden replays, checklists, and faults that are diagnosable from evidence rather than guessable from alarms.

---

## 1. What it is

First-person, single-player (co-op experimental, see §8) operation of a full PWR plant on an island. Cold start, ramp to full power, meet a city's hourly demand, keep the plant alive. The current Steam copy's pitch, verbatim in spirit: "Every switch, every gauge, every alarm is yours to touch. Start it cold, push it to full power and keep an entire city running, while the reactor tries to get the upper hand." Four difficulty levels, from a guided tutorial to Advanced; time freeze is available ("Freeze time when you need to think, and learn at your own pace", Steam).

Core scale (current version, V 2.2.x): **nine fuel blocks, 72 control rods** (nine rod banks of eight, per the controller community's `ROD_BANK_POS_0..8` variables), three-tier resistance banks, improved circulation pumps, real-time monitoring panels for rods and fuel blocks. Older marketing (still indexed on Steam search results) describes the same plant as "200+ parameters and 150+ switches across the Control Room, Containment Building, Turbine Building, and Cooling Towers."

What it explicitly is not, per its own accidents guide: "not a certified training replica of any specific historical plant," and it does not claim "to simulate radiological consequences to populations." Its stated intent is systems intuition and discipline, not certification training. That is an honest scope statement reactor-sim should copy verbatim in tone.

## 2. Chemistry system, the best loop in the game, and the messiest

### 2.1 What the developer claims

From official product site and the Steam feature list:

- Boric acid dosing and filtration to fine-tune reactivity.
- Ion exchange columns: "Maintain absorption capacity and prevent corrosion by managing coolant pH levels."
- Xenon-135 and iodine-135 dynamics that reshape stability.

That is the whole official description. There is no public official chemistry manual found in this research; the detail below comes from players reverse-engineering it, which is itself a finding (see §9).

### 2.2 How it actually plays (community-documented)

**Boric acid as a slow reactivity lever.** Dosing boric acid in raises neutron absorption, lowers reactivity, cools the core; filtering it out through the ion-exchange column does the reverse. A widely referenced community tutorial (Steam Community, "Chemical Treatment (Tutorial)", Mudraxus, last edited 2024-05-02) gives the working numbers players actually use:

- Dosing pump to 50 (max) during startup with rods ~96% inserted.
- Around 3,000 ppm boron, cut dosage to 25 and start withdrawing rods.
- Around 3,400 ppm, dosage to 10, rods fully out.
- Fine-tune with dosage ~2–3 and filter pump ~5% for maximum precision; stable point roughly 3,400–3,500 ppm at reactivity ~10.
- "Concentrations of 3500 or more do not yield more absorption and should be avoided."

The community C# controller independently converges on the same region: a PID loop holding **~3,300 ppm boron**, with rule-based overrides (see §7). The convergence of two independent community sources on 3,300–3,500 ppm is good evidence this is the real stable band, not a single player's superstition, but it is still community evidence, not a developer spec.

**The ion-exchange column as a consumable with a regeneration chore.** This is where chemistry stops being a slider and becomes a loop. From the Steam thread "How do you add chemicals other than boron?" (the most detailed chemistry thread found):

1. Two large green valves connect the core to the ion column. Opening them routes primary coolant through the exchanger, which lowers boric-acid concentration, but also **consumes the sorption capacity of the ion-exchange resin (AN-31)**.
2. When capacity / cleaning efficiency falls (players report watching a "cleaning efficiency gauge", green = healthy), filtering stops working: "I can set boron filtering to 100% (fast) with no effect."
3. Regeneration is a manual, multi-step, physical procedure: isolate the column from the core (close its two valves), open the valve from the white tank upstairs (sodium hydroxide, NaOH), run the ion-column cleaning pump to restore sorption capacity with NaOH, stop the pump, close the NaOH valve, open the adjacent water-tank valve, run the pump again to flush boric acid out of the column.
4. The flush fills a **waste tank** with sludge. Draining it is not a button press: AO's preventative-maintenance analysis adds a "remove chemical contaminant" task to the maintenance list, and the drain happens through that workflow.
5. Resupply is logistical, not a menu: refill water via valve MO1 above the water tank plus the internal primary pump (switch on the treatment-room console); refill boric acid and NaOH via the tablet's "supplies and spare parts" app, which summons a **delivery truck**; when it arrives the player gets instructions and must set intake valves (next to the swinging door) correctly for the solution being taken, working from the RCC console or the hall above the parking lot.

**Xenon/iodine as the time-delayed trap.** Iodine-135 decays to xenon-135, a strong neutron poison. Power changes, shutdowns, and restarts therefore have hours-long consequences: xenon builds after a power drop or scram and can make restart impossible until it decays ("xenon pit"). The C# controller's emergency rule is explicit: if xenon > 60, temperature < 300 °C, and reactivity < 0, withdraw all rods gradually to rescue the core. The official accidents guide frames this conceptually: "reactivity, xenon dynamics, and the danger of pushing a plant outside the envelope where feedbacks remain stabilizing."

**pH and corrosion: claimed, not evidenced.** The official copy ties ion-exchange management to coolant pH and corrosion prevention. No public source read in this research gives a pH number, a pH gauge reading, a corrosion rate, or a corrosion failure mode with a name and a consequence. It is possible pH is a real modelled variable exposed only in-game; it is equally possible "pH/corrosion" is mostly narrative framing over the resin-capacity mechanic. Reactor-sim should not copy the claim without the mechanic. If reactor-sim models pH, it should show the number, show the corrosion rate, and show a named failure, otherwise it is marketing, and players will notice.

### 2.3 What is genuinely good about the chemistry design

- **Three coupled timescales:** rods (seconds), boron dose/filter (minutes), resin capacity/regeneration (hours/days). A player who only learns rods can start the plant; a player who ignores resin eventually cannot control boron at all. That is a real progression of understanding, not a difficulty slider.
- **Chemistry has a physical location.** The treatment room, the upstairs NaOH tank, the green valves, the waste tank, you walk there, you turn valves in sequence, you can do it in the wrong order. The loop is embodied, which makes it memorable in a way a chemistry panel would not be.
- **Chemistry has a supply chain.** Ordering chemicals, waiting for a truck, setting intake valves correctly, is a small logistics game that makes boron a resource rather than a free slider. It also creates a failure mode (wrong intake valve) that is the player's fault, not RNG.
- **Xenon makes history matter.** The plant remembers what you did hours ago. That is the single most "nuclear" design decision in the game, and the one most sims skip.

### 2.4 What is weak

- **Undocumented to the point of broken-seeming.** Multiple threads are players unable to tell a bug from a mechanic: filtering "has no effect" (actually exhausted resin), a filter that turns green and leaks water on load (reported as a bug; another player says the filter is damaged and AO should scan it), valve labels "glitched". When the only way to learn a system is a 2024 forum tutorial, the system is not finished as a game system, whatever its simulation quality.
- **A known bug in the supply chain itself:** the tanker-truck pump overloads when pumping NaOH; the community workaround is to fill boric acid first and open both intake valves simultaneously ("the pump makes a grating sound, the integrity loss is pretty minimal anyways"). A logistics loop whose workaround is "accept minor integrity loss and a grating sound" is a bug players have normalised.
- **No evidence of a pH/corrosion gameplay consequence.** See above. If it exists, it is invisible in every public source read.

## 3. Fault and malfunction gameplay

### 3.1 What the game says it does

Official copy (both site and Steam): "Respond to equipment failures, floods, and fires under pressure." GamesPress announcement: "dynamic emergency scenarios like equipment failures, fires, floods, and structural breakdowns." Steam's current copy adds meltdowns and the "last train out", i.e. abandoning the plant is a real ending, with a "Train Escape" achievement (added June 2024, alongside "Reversed Meltdown", "Reactor Destroyer", "Professional Analyzer", "Maintenance Expert", "Energy Master", "Critical Mass").

The accidents guide is explicit about the design intent: failure modes should be tangible, "loss of cooling, power blackouts, chemistry mistakes, and the cascade from a small fault into a plant-wide emergency", and frames it through defence in depth: "operators must recognize early symptoms and buy time for those layers to work." Fukushima is the framing case: "a reactor that is 'off' still produces decay heat. Cooling and electrical infrastructure are as critical as the core itself."

### 3.2 How faults are introduced, propagated, recovered, as far as public sources show

**Introduction:** No public source read gives a fault schedule, a difficulty-scaled spawn table, or a "fault injection" menu for the player. Faults are described as dynamic/random ("Stuff dies and combusts at random", Steam review, Mar 2026; "breakdowns, fires, floods and meltdowns", Steam). The webserver exposes deliberate triggers, which is the closest thing to a documented fault-injection system (see §7): `FUN_DECREASE_INTEGRITY` (extended May 2025 to include generators and turbines), `FUN_FIRE_DRILL` (sets off a random alarm for X minutes), plus `CORE_EMERGENCY_STOP`, `STEAM_TURBINE_TRIP`, and a SCRAM button variable. NuCon marks all `FUN_*` parameters as cheats: writing them raises unless `cheat_mode=True`. That gating is a good design, fault injection exists, but it is fenced off from normal play and from naive automation.

**Propagation:** The strongest evidence for real cascading is indirect but convergent: the maintenance/integrity model (equipment has integrity, degrades, requires repair; pumps have states including `REQUIRES_MAINTENANCE`, `INSUFFICIENT_ENERGY`, dry-running and overload states per NuCon's pump enums), the condenser vacuum overhaul (June 2025: new ejector + vacuum pump system, with condensate flow as a new diagnostic), and patch notes about audits: V 2.2.25.219 (Jul 2026) "Fixed a bug in the audits, which did not consider fires as a hazard to be assessed." That single line proves audits/inspections score hazards including fires, i.e. faults have consequences beyond the immediate emergency, in the progression system.

**Recovery is physical and slow.** The recurring pattern in patch notes and threads: AO (or the player) must travel to a scan/repair point, scan, then repair; some repairs are via the maintenance report/task list; the overhead crane moves fuel and rods; sprinklers, diesel generators, CCTV, and steam generators are all individually repairable items that have each had their own "could not be repaired" bug at some point (steam generator, June 2025 patch cycle; sprinklers shown as pending repair after being repaired, Jan 2026). The player can also die: a Nov 2026 Steam review describes remote-controlling AO, electrocuting themselves, dying, "remain[ing] as the AO", the game pausing and forcing a quit/restart. Death-by-electrocution while possessing AO is player-reported, not developer-documented; treat it as evidence that AO remote control has lethal edge cases, not as a designed mechanic.

**What is missing from public evidence:** no named fault catalogue (e.g. "LOCA", "loss of offsite power", "pump trip" as distinct scenarios with distinct procedures), no evidence of a scenario editor or a scripted campaign, no published fault-tree or "how to diagnose X from Y alarm" guide from the developer. Diagnosis appears to be: alarms sound, AO scans, the player traces pipes/valves physically ("If a system fails, you can physically trace the fault, from a short circuit in an underground tunnel to a failing pump in the containment area", Techmash summary; consistent with the game's no-black-box claim). That is a strength for immersion and a weakness for learnability.

### 3.3 Assessment

Good: faults are systemic (integrity → degradation → failure → cascade) rather than scripted cutscenes; the train-escape ending gives failure a dignified exit instead of a game-over screen; audits give faults a long tail. Weak: randomness without a visible fault model feels unfair to new players ("the slightest thing I touch is enough to set the alarms and I have to restart over because I have no idea how to fix anything", Steam review, Nov 2026); recovery is gated on AO pathfinding and save/load correctness, both of which have repeatedly broken (see §9).

## 4. Walkable plant, what walking adds, and what it costs

### 4.1 The spaces

Control Room, Containment / Reactor Building (reactor hall, fuel pool, control-rod drive room), Turbine Hall / Turbine Building, Cooling Towers, underground maintenance tunnels, chemical treatment room, diesel generator room, distribution area, parking lot / truck intake hall, outdoors (island, trees, weather/rain), rooftop via elevator. Movement includes ladders, elevators (repeatedly patched for co-op and for taking the player to the wrong floor), and the overhead crane over the reactor pool, operable by hand or dispatched from the tablet.

### 4.2 What walking is *for* (not just scenery)

1. **Controls are where they are.** Breakers, pumps, valves, and the chemical room's consoles are physical objects in rooms, not a single unified panel. The "no black box" claim is literal: you trace a fault by walking the pipe run.
2. **Fuel handling is a crane operation.** "Lift tons of steel, lower fresh fuel into the heart of the reactor and swap out the spent blocks, slowly and carefully, because down there every inch counts" (Steam). Fuel-block loading/unloading can also be sent as an Operational Tasks job from the tablet, and V 2.2.25.219 changed automatic crane tasks to always choose the newest control rod. The crane moving "according to the speed of the game" was itself a Jul 2026 fix, i.e. crane motion is simulated, not animated on a fixed timer.
3. **Maintenance requires presence.** AO has to pathfind to scan/repair points (patch notes repeatedly: "Fixed the plant mapping so that AO can correctly find the scan or repair points", Jul 2026; "Fixed a bug preventing AO from finding repair points when requested to perform a scan", Apr 2026). The player likewise must be in the right room with the right gear.
4. **Radiation / protective gear is spatial.** Prestige buys protective gear; security-suit containers exist in-world (a Jun 2024 patch fixed seeing through the wall behind them). The accidents guide and Steam copy imply contaminated areas are places you enter, not a global meter. No public source read quantifies dose rate by zone, so do not assume a detailed dose model exists.
5. **The tablet makes walking optional for *some* things and not others.** Remote control of reactor tasks, AO, fuel loading, and supplies ordering from anywhere in the plant is the game's answer to "walking everywhere would be tedious". That tension, physical presence for repairs and fuel, remote operation for monitoring and ordering, is the central walkability design decision, and it is a good one.

### 4.3 What walking costs

- **AO pathfinding is a perennial bug source.** Patch notes across Jan–Jul 2026: AO not recognising outdoors, AO shutting down at the wrong charger location, AO falling off the map when remotely controlled, AO taking control of the reactor after loading a save, AO failing to reactivate tasks after loading. A helper robot whose navigation is unreliable converts "walkable plant" from a feature into a commute.
- **Traversal time vs. emergency time.** No source quantifies travel times, but the design tension is obvious: a fire in containment while the player is at the cooling towers is a meaningful choice only if travel time is fair and the player had a reason to be there. The game gives no public evidence of having tuned this (no "you were too far away" post-mortem, no fast-travel).
- **Performance.** The plant is rendered in full 3D with dynamic lighting, simulated water (with a GPU-compatibility fallback added Jul 2026), rain, trees responding to wind. GameBrain's aggregated reviews report "wildly inconsistent framerates ranging from 29 fps to 111 fps" on a 9950X3D + RTX 5090, and "heavy CPU usage … especially during picture-in-picture views". Walkability bought with that rendering cost is a trade reactor-sim should weigh explicitly: Unity 6 + a 160 MWt SMR site is smaller, and reactor-sim should protect frame rate over visual density.

## 5. Progression and economy

### 5.1 The loop as documented

- **Daily energy goals:** meet the day's production target to earn Prestige and Experience Points; level up to unlock harder scenarios.
- **A living city:** demand "rises at dinner time, drops at night and never stops changing" (Steam). Deliver and earn prestige, upgrades, tougher challenges; fall short and "the inspectors will be knocking on your door" (Steam). V 2.2.25.219 fixed a penalty bug: players were "not being penalized correctly when power was stopped being supplied to the city without a prior shutdown request", i.e. there is a formal shutdown-request procedure, and cutting supply without it is penalised. That is a nice piece of operational realism: the grid is a counterparty, not a meter.
- **Prestige spending:** upgrades, fuel blocks, protective gear, radioactive waste cleanup (Steam / site, repeated verbatim across sources). No public source gives costs, so no cost curve can be reported.
- **Resource management:** "Balance fuel consumption, maintenance costs, and community demands" (Steam). Fuel blocks burn down and must be replaced via the crane; maintenance has a cost (AO repair cost was at one point double-charged after loading a save, Jul 2026 patch); supplies (boric acid, NaOH, water, spare parts) are ordered through the tablet and delivered by truck.
- **Audits / inspections:** a scored system that assesses hazards; fires were (until Jul 2026) not counted as a hazard in audits, which implies audits score the plant's state across categories. No public source lists the audit categories or the pass/fail consequences.
- **Difficulty:** four levels, tutorial to Advanced; levelling "into harder scenarios as your operator expertise grows" (site). No public detail on what changes per level (demand? fault rate? starting equipment?).

### 5.2 Assessment

Good: the economy is *operational*, not abstract, money/prestige is spent on fuel, gear, waste, and repairs, all of which are physical things with lead times. The demand curve plus the shutdown-request penalty makes "just scram and wait" a losing strategy, which is exactly the pressure a real operator faces. The daily goal gives a session shape (a "day" as a unit) that a pure sandbox lacks.

Weak / unproven: no visible numbers anywhere in public sources, no prestige income per MWh, no fuel-block price, no maintenance cost curve, no demand numbers. Either the economy is deliberately opaque (bad: players cannot plan) or it is documented only in-game (unverifiable from outside). The review aggregation also flags "pay-to-win elements" (~2% of reviews, Niklas Notes Feb 2026) and "DLC concerns" (~3%), the DLC in question is not named in the aggregation, but the paid AO Extra Power Pack (Aug 2026, see §6) is the obvious candidate for "pay for a better helper". Reactor-sim should keep any assistant-power progression earnable in-game, not paid.

## 6. AI assistant "AO"

### 6.1 What AO is

A physical robot in the plant (not a voice in a menu). Per official copy: "Your AI companion helps with ignition protocols, alarm systems, and crisis management. Upgrade its skills to unlock advanced functionalities." Per the site: coordinate fuel loading, control rods, and maintenance workflows; run remote tasks from the operator tablet anywhere in the facility.

From patch notes and community sources, AO's concrete jobs include:

- **Guided startup:** AO guides the first startup in the tutorial.
- **Plant analysis / scanning:** "ask AO to do a full scan and see what they find" (community advice for a damaged ion column); AO does a "preventative maintenance analysis" that generates the maintenance task list (including chemical-waste draining).
- **Repair:** AO travels to repair points and repairs equipment; repair speed, scan speed, and move speed are AO stats.
- **Pressurizer control:** AO can control the pressurizer (a Feb 2026 patch fixed "AO's control of the pressurizer").
- **Remote operation:** the player can remotely control AO from the tablet (and, per a player review, can die by electrocution while doing so and end up "remain[ing] as the AO", see §3.2 caveat).
- **Battery/charger:** AO has a battery and a charger it returns to; multiple patches concern AO shutting down at the wrong location when sent to its charger.
- **Operational Tasks app:** manage reactor functions remotely via tablet, including fuel-block loading/unloading and AO control (V 2.2.21.172 update notes).

### 6.2 Upgrades and the DLC problem

The base game promises "Upgrade AO skills to unlock advanced plant assistance" (skill tree / skill points, exact list not found in any public source read). Then, Aug 2026, a paid DLC: **Island PWR Sim, AO Extra Power Pack** (Steam app 5018580; MicroProse page published 2026-08-11). Its Steam description, in full marketing voice:

> "Introducing the AO Intelligence Upgrade: a breakthrough so revolutionary it lets you boost the assistant's work power without using more battery. Same runtime. More output. … With the extra power control, you can take AO from 'responsible operator' all the way to 'absolute efficiency (do not look into the eyes)'. At maximum power, the robot: moves faster, scans faster, repairs faster than ever. … Includes: AO intelligence upgrade; Adjustable work power (0 → 100%); Same battery, higher productivity; Zero documented incidents."

The copy is plainly satirical ("Ethics asked to speak and… well", "Frequently asked questions we recommend not asking out loud"). The design fact underneath the joke is not satirical: **assistant speed is sold as a paid add-on with a 0–100% slider**. Whether that is pay-to-win or a harmless joke DLC depends on whether base-game AO speed is adequate, which no public source quantifies. Reactor-sim should not split its crew/helper capability across a paywall; the perception cost alone (see §5.2, DLC concerns in reviews) is not worth it.

Other DLC for context: New Generator Set ($2.99, May 2024/25, sources differ on year; SteamDB lists May 2024, MicroProse press kit says published 2024-05-25) and Island Fauna ($3.99, Apr 2026, "dynamic wildlife … animals roam, react to the player, and behave differently as conditions change"). Fauna is decorative; the generator set is functional equipment. Both are small, cheap, and, unlike the AO pack, do not touch the core helper loop.

### 6.3 Assessment

Good: AO as an *embodied* agent with a battery, a location, a speed, and a task queue is far better than a magic "auto-repair" button. It creates real decisions: send AO to scan containment or keep it near the turbine? It also gives the game a second pair of hands, which is what makes a one-person plant operable at all, and it is the in-fiction justification for the tablet remote control. The satirical DLC copy shows a developer with a sense of humour about his own AI helper, which players seem to enjoy.

Weak: AO is the single buggiest subsystem in the patch record (pathfinding, charger, save/load state, taking control unprompted, remote-control falls off the map). An assistant that acts on its own after a save load ("AO would sometimes take control of the reactor after loading a saved game", Feb 2026) breaks the player's trust in a way a broken valve does not. And the upgrade path is undocumented publicly, no skill list, no costs, so "upgrade AO skills" is a promise, not a system a prospective player can evaluate.

## 7. Web API, automation controllers, and RL agents

This is the most important section for reactor-sim, because it is where Island PWR Sim accidentally became a research platform, and where reactor-sim, being deterministic and C#-native, can be deliberately better.

### 7.1 The official webserver

The game embeds an HTTP webserver, default **http://localhost:8785**, enableable in-game, with configurable custom port, listening interface (localhost or all interfaces), and auto-start on game load (added May 2025). It exposes game variables over GET (read) and POST (write). Community tooling documents:

- `WEBSERVER_LIST_VARIABLES` (machine-readable list, for automation) vs `WEBSERVER_VIEW_VARIABLES` (human-readable; "we may be changing the VIEW formatting over time", developer advice via patch notes is to always use LIST for automation). That is a developer who has learned API stability the hard way, and the advice is correct.
- `WEBSERVER_BATCH_GET` for reading multiple variables at once (added June 2025, credited to community contributor GHXX).
- A GET variable for active alarms; colour-coded maintenance report/tasks over the webserver (May 2025).
- Write-only action variables (e.g. `CORE_SCRAM_BUTTON`, `VALVE_OPEN`), readable state variables, and enum-typed variables (pump/breaker status).
- Event/cheat triggers under `FUN_*` (see §3.2), plus `CORE_EMERGENCY_STOP`, `CORE_END_EMERGENCY_STOP`, `STEAM_TURBINE_TRIP`, `RESET_AO`.
- Variable naming is flat `SYSTEM_THING_DETAIL` (e.g. `CORE_TEMP`, `CHEM_BORON_PPM`, `ROD_BANK_POS_0_ORDERED` / `ROD_BANK_POS_0_ACTUAL`, `CONDENSER_CIRCULATION_PUMP_ORDERED_SPEED`). Ordered-vs-actual pairs are everywhere, the API exposes intent and state separately, which is exactly what a controller needs.
- Documentation lives on a Blogspot blog: https://[commercial-pwr-product-url-omitted] (referenced in 2023 and 2024 patch notes; not re-fetched in full for this report, the variable list is corroborated by NuCon's tests, which verify parameter types against the live game). The condenser deep-dive is likewise a blog post (May 2025). A Blogspot blog as API documentation is fragile and unversioned; reactor-sim should ship its API reference in-repo, versioned with the build.

Community contributions to the webserver are credited by name in patch notes (Ker Ball One for coolant-flow, pump-capacity, boron, and time variables; GHXX for batch GET, valve-panel JSON, steam-generator variables). That is a healthy pattern: the API grew because players asked for variables, and the developer merged them.

### 7.2 Community PWR PID controller (C#, GHXX original, Dutchevil fork)

- **What:** a real-time C# program that autonomously operates the reactor via the webserver (.NET 7+, MIT, created 2025-05-17 as a fork; 30 commits, 0 stars at time of reading, small, but real).
- **Control law:** PID on reactivity to hold core temperature 310–360 °C, one rod bank adjusted per tick, reactivity target ~0.10; a second PID holds boron ~3,300 ppm; a third PID holds condenser return temperature ~65 °C via condenser pump speed; iodine tracked with a smoothed moving average, boron dosed to suppress generation; xenon-pit rescue rule (xenon > 60, temp < 300, reactivity < 0 → withdraw all rods gradually).
- **Why it matters:** it proves the API is sufficient for closed-loop control of the *whole* normal operating envelope, rods, boron, condenser, with three PIDs and a handful of rules. No machine learning needed. That sets the bar for reactor-sim: if a deterministic sim's API cannot be driven to steady state by three PIDs, the API (or the sim) is incomplete.

### 7.3 NuCon (Python, dodox, Gitea)

The most sophisticated community project found. NuCon is a typed Python library over the same webserver, plus:

- Enum-based, type-safe parameter access (`nucon.CORE_TEMP.value`, `.read()`, `.write()`, `.param_type`, `.is_writable`, `.is_readable`, `.is_cheat`, `.unit`); `get_all_readable()`, `get_all_writable()`, `get_game_variable_names()`; dummy mode (sensible values, writes ignored) and cheat mode (gated `FUN_*` writes).
- A dedicated Valve API: `get_valve(s)` for all **53 valves** (`Value`, `IsOpened`, `IsClosed`, `Stuck`, …), `open_valve` / `close_valve` / `off_valve`, modelling motorised actuators correctly (OPEN/CLOSE powers the motor, OFF holds position), not as boolean open/closed.
- A shipped classical operator: `scripts/reactor_control.py`, "a full classical reactor operator with grid-demand following, pressurizer control, and a live TUI".
- **Gymnasium RL environments** (`nucon/rl.py`): `NuconEnv` (fixed objective; observation ~290 readable numeric parameters, action ~30 writable parameters, 9 rod banks, 3 MSCVs, 3 turbine bypass valves, 6 coolant pump speeds, condenser pump, freight/vent switches, resistor banks) and `NuconGoalEnv` (goal-conditioned; target generator output or core temperature sampled per episode; designed for Hindsight Experience Replay). Predefined goal envs: `Nucon-goal_power-v0` (3 × 0–1200 kW, note the kW scale, a fraction of a real plant; treat as the game's internal scale), `Nucon-goal_temp-v0` (280–380 °C).
- **A learned simulator and model learning:** because "Island PWR Sim is slow and cannot be trivially parallelised", NuCon provides data collection (`collect_dataset.py`), model fitting (kNN Gaussian Process, works from a few hundred samples, returns uncertainty, or a neural net needing several thousand), and a faster-than-real-time simulator for training, with an explicit iterative loop: collect in the real game → fit model → train SAC + HER in the simulator (with an uncertainty penalty / uncertainty abort so the policy stays in-distribution) → evaluate in the real game and collect more data → refit.
- **Tests** that verify parameter types against the live game and that non-writable parameters cannot be written even with force.

The honest caveat in NuCon's own README is the key lesson: "RL algorithms require a huge number of training steps, and Island PWR Sim is slow and cannot be trivially parallelised. That's why NuCon provides a built-in simulator." Island PWR Sim' game loop is real-time and single-instance. Reactor-sim's deterministic C# core, if it can run headless and faster than real time, removes the exact bottleneck that forced NuCon to learn a surrogate model of the game. That is reactor-sim's single biggest structural advantage over Island PWR Sim, and it should be designed in from Phase 1, not bolted on: headless mode, fixed-step determinism, seedable faults, and an API that is the *same* interface the game UI uses, so a controller tested headless behaves identically in the Unity build.

## 8. Update cadence and developer communication

### 8.1 Cadence (from SteamDB patch notes, the most complete public record)

- **Long tail, small patches, high frequency.** After full release (Mar 2023) the game has been patched continuously for 3+ years. In 2026 alone, the patches surfaced in this research: V 2.2.25.205 (Jan 3), 207 (Jan 7), 208 (Jan 9), 209 (Jan 12–13), 211 (Feb 9), 213 (Feb 12), 214 (Apr 2), 215 (Apr 12), 218 (Jul 12), 219 (Jul 22). The latest patch note found is **V 2.2.25.219, 22 Jul 2026**; no patch note after that date surfaced in searches conducted 2026-11-25. That is a four-month gap at time of writing, possibly a quiet period, a larger update in progress (a Dec 2025 forum thread says "THEIR WORKING on something big which could take 6 months"), or a slowdown. Do not assert development has stopped; do assert the public patch record goes quiet after July 2026.
- **Major systems landed post-release, not at launch:** Chemical Module (V 0.2.18.141, Jan 2024), Enhanced Core and Safety Systems, nine fuel blocks / 72 rods (V 2.2.21.172), Condenser Vacuum System Overhaul (Jun 2025), Time Freeze & Volumetric Lighting (Aug 2025), Cooperative Mode Fusion 2.0 (experimental, Jan–Jul 2026). The version numbering (0.2 → 1.2 → 2.2) suggests the developer kept "Early Access style" versioning well past the Mar 2023 "release", consistent with a July 2026 Steam review: "really should come with an early access flag. Still buggy, but the developer is still very much actively working on it."
- **Save compatibility is a recurring casualty.** Jan 2024: "Older savegames may become incompatible with future releases, and savegames from the previous version are not compatible with the chemical module." Jan 2026: save slots capped at 7 (was unlimited); Steam Cloud sync only "begun implementation (in progress)". Players report save/load instability as a top complaint (GameBrain: stability 27 negative mentions).

### 8.2 How the developer communicates

- **Steam announcements for every patch**, each ending "We are waiting for you in the game's Discord!", Discord is the community hub ([product-discord-invite-omitted]).
- **A Blogspot dev blog** ([dev-blog-url-omitted]) for deep system posts: webserver variables (Nov 2023), condenser overhaul (May 2025), co-op disabled (Jan 2024). Technical, unpolished, genuinely useful, and the only place some systems are documented at all.
- **official product site operator guides** (Aug 2026): short, well-written explainers tying game systems to real PWR physics and to Chernobyl/Fukushima. These read as recent marketing-site work (possibly MicroProse-era) and are the best-written material about the game. They explain *why*, not *how to click*.
- **Patch notes credit community contributors by name** (Ker Ball One, GHXX) and quote player bug reports ("[2]" markers in some notes). Players notice: "the developer is super active and updates are coming out all the time" (Jul 2026 review); "the developer was quick to respond to my crash report and confirm that it'll be fixed in the next update" (Nov 2026 review).
- **Direct, sometimes blunt forum communication on multiplayer:** co-op disabled Jan 2024 because "bugs have accumulated over time" and adapting it "requires substantial work"; developer quote in the forum (via players): "Multiplayer is not eliminated from the plans. The first attempt didn't go well, but it proved that it is possible. When the game stops receiving major updates, I'll be able to get into multiplayer again." That is honest. It is also an admission that co-op was attempted before the single-player systems were stable, the exact mistake reactor-sim's phased plan (LAN co-op is in the original reactor-sim pitch, but after core determinism) is designed to avoid.

### 8.3 Assessment

Good: relentless small patches, public patch notes, named community credit, a Discord that players actually cite, and a developer who answers crash reports personally. That cadence is a large part of why a buggy game holds Very Positive. Weak: documentation is scattered across Steam, Blogspot, Discord, and forum threads, with no single manual; major systems ship with save-breaking changes; and the four-month patch gap (Jul–Nov 2026) is unexplained publicly.

## 9. Genuinely good / weak / missing, summary

### Genuinely good (steal these)

1. **Coupled systems with different timescales.** Rods/boron/resin/xenon/fuel/demand/maintenance all interact, and the interactions are the game. No single slider wins.
2. **Embodied operations.** Valves, crane, truck deliveries, AO travel, and repair points are in physical places. The plant is a place, not a dashboard.
3. **The tablet as a remote-ops layer.** It solves the "walking everywhere is tedious" problem without removing the physical plant: monitor/order remotely, repair/refuel in person.
4. **The public web API, with ordered/actual pairs, batch reads, and gated cheat triggers.** It turned players into controller authors and RL researchers. The community did this with no official SDK.
5. **Failure with dignity.** Meltdown is possible, the train out is an ending, audits score the aftermath. Failure is a story, not a screen.
6. **A living demand curve + a shutdown-request penalty.** The grid as a counterparty creates pressure that no fault RNG can.
7. **Developer cadence and community credit.** Small patches, named contributors, personal crash-report replies.

### Weak (learn from, do not repeat)

1. **No-hand-holding as an excuse for no documentation.** The tutorial covers startup; maintenance, overloads, overpressures, and chemistry are left to forums and YouTube (one reviewer estimates "12 hours minimum in YouTube tutorial videos"). "Learn through experimentation" only works if experiments give readable feedback.
2. **Save/load and AO reliability.** The two systems a long campaign depends on, persistence and the helper, are the two most patched and most complained about. A deterministic sim with golden replays has no excuse for this.
3. **Performance.** Inconsistent frame rates on a 9950X3D + RTX 5090 for modest graphics; heavy CPU in picture-in-picture views. Scope the rendering to the simulation, not the other way round.
4. **Faults feel random rather than diagnosable.** Alarms without a diagnostic path produce restarts, not learning. "39 alarms to tend to, explosion" is funny in a review and miserable in a play session.
5. **Co-op before stability.** Removed in 2024, rebuilt experimentally in 2026, still desyncing. Ship single-player solid first.
6. **Paid assistant power.** The AO Extra Power Pack makes the helper's speed a purchase. Even as a joke, it reads as pay-to-win in review aggregations.
7. **Save-breaking major updates and a 7-slot cap introduced years after launch.** Version your saves; migrate them; do not cap them without warning.

### Missing (gaps reactor-sim can own outright)

1. **No crew.** One operator + one robot. No shift handovers, no crew skills, no staffing trade-offs. Reactor-sim's planned crew phase has no incumbent to beat.
2. **No campus / site layer.** The island, parking lot, and train exist as scenery and an exit. There is no site management beyond the plant fence.
3. **No radiation model players can see or plan around** (no public dose numbers, no zone map; protective gear is a prestige purchase, not a dosimetry system). Reactor-sim's planned radiation phase can be the differentiator, but only if dose is a number with a budget, a meter, and consequences.
4. **No scenario editor, no scripted fault scenarios, no seeded runs.** Faults are ambient RNG plus webserver cheat triggers. A deterministic sim can offer named scenarios ("loss of offsite power", "xenon pit after load rejection") with a seed, a briefing, and a debrief, and that is directly the "whiteboard defence" / mastery idea behind viva as well.
5. **No in-game manual that matches the simulation.** The best documentation is a 2024 forum tutorial and a Blogspot post. A shipped, versioned, in-game manual with a piping diagram (the community made one: "Island PWR Sim Piping and Plumbing Diagram … for v2.2.24.191 dated 21JUN2025", a player filled the gap) is a low-cost, high-trust win.
6. **No headless / fast-time operation.** The game is real-time and single-instance, which is why RL researchers had to learn a surrogate model. A deterministic core that runs headless faster than real time is not a feature; it is the platform the community builds on.

## 10. What reactor-sim should steal, prioritised, mapped to its phases

Reactor-sim phases (per task brief): walkable buildings, crew, radiation, maintenance, campus, operations/progression, release hardening; Phase 1 (deterministic core) done. Steals are ordered by leverage, not by phase number.

### Steal first (Phase 1–2, cheap, high leverage)

1. **A public HTTP API from the first public build, with Island PWR Sim' exact good habits and none of its bad ones.** Flat `SYSTEM_THING_DETAIL` names; ordered-vs-actual pairs for every actuator; `LIST` (stable, machine-readable) vs `VIEW` (human-readable, explicitly unstable); `BATCH_GET`; a `FUN_*`-style gated trigger namespace for fault injection, default-off and clearly labelled as cheats; valve/pump state as enums (including `REQUIRES_MAINTENANCE`, dry-run, overload, stuck), not booleans. Document it in-repo, versioned with the build, not on a blog. *This is the single highest-leverage steal: it is what lets three PIDs, a TUI operator, and later an RL agent all target the same interface.*
2. **Headless, fixed-step, faster-than-real-time execution of the same core the Unity build uses.** Island PWR Sim forced its RL community to learn a surrogate model because the game is slow and single-instance. Reactor-sim's determinism is the answer, but only if headless is a first-class mode with a seed, not a debug flag. Ship `collect → train → evaluate` as a documented loop, and NuCon's iterative workflow (real-game data → kNN-GP/NN model → simulated training with an uncertainty penalty → real-game eval → refit) becomes unnecessary for reactor-sim: train against the real core, fast.
3. **Chemistry as three timescales, not one slider.** Copy the shape, fix the documentation: (a) fast lever, boron dose/filter (or the SMR equivalent) with a published stable band; (b) slow poison, xenon/iodine with a visible "time to restart" consequence after scram/load drop; (c) consumable + chore, ion-exchange/resin capacity with a *named, in-game, step-by-step* regeneration procedure, a waste stream with a volume, and a supply delivery with a lead time and a correct-valve check. Show pH and corrosion as numbers with a rate and a named failure, or do not claim them at all.
4. **In-game checklists + a piping diagram, shipped with the game.** Island PWR Sim players made their own checklists and P&ID; the developer added a Checklist app to the tablet only in Apr 2026. Reactor-sim should ship the 13-step startup procedure (already planned) as an interactive checklist, plus a live P&ID that highlights the fault path. This is the direct fix for Island PWR Sim' #1 complaint.

### Steal next (operations/progression + walkable buildings)

5. **A daily demand curve with a grid counterparty.** Hourly demand (dinner peak, night trough), a daily energy goal, and a formal shutdown-request procedure with a penalty for cutting supply unannounced. Prestige/XP as the reward currency, spent on physical things (fuel, gear, waste, repairs) with lead times, never on assistant power. Publish the numbers: income per MWh, fuel price, maintenance cost. Opaque economies cannot be planned, and players can tell.
6. **Faults as a diagnosable cascade, with audits as the long tail.** Copy: integrity → degradation → failure; fires/floods/equipment failure as categories; audits that score hazards (including fires, Island PWR Sim forgot them until Jul 2026). Fix: a named fault catalogue with distinct procedures, a fault-injection menu with seeds for practice, and an alarm system that points at evidence (which pump, which valve, which reading changed) instead of 39 simultaneous alarms. A fault the player cannot diagnose is a restart, not a lesson.
7. **Walkable plant where location is a mechanic.** Copy: controls in rooms, crane fuel handling (manual + tablet-dispatched), repairs that require presence, AO/crew that must travel to a repair point. Fix: make travel time fair and visible (show "AO arrives in 40 s"), give the tablet remote monitoring/ordering so walking is a choice, and protect frame rate, a smaller SMR site is an advantage; do not spend it on volumetric lighting.
8. **An embodied helper with a battery, a queue, and a location, but deterministic and trustworthy.** AO's design (scan, analyse, repair, pressurizer, startup guide, remote control) is right; its implementation (pathfinding, save/load state, unprompted takeovers) is the warning. Reactor-sim's crew phase should implement the helper/crew as state machines with explicit task queues, persisted in the save, and *never* acting without an order or a standing order the player set. No paid speed upgrades.

### Steal later (crew, radiation, maintenance, campus)

9. **Maintenance as a report, not a chore list.** A colour-coded maintenance report with tasks generated from analysis (AO's preventative-maintenance analysis is the model), repair costs that are charged once and survive a save/load, and equipment states (dry, overload, insufficient energy, not installed) visible at a glance. Tie it to the chemistry waste stream (drain the waste tank via a maintenance task), Island PWR Sim already links these; make the link visible.
10. **Radiation as a budget, not a vibe.** Island PWR Sim has protective gear and a train escape but no public dose model. Reactor-sim's radiation phase should do what Island PWR Sim did not: dose rate by zone on the map, a personal dose budget per shift, a Geiger/dosimeter readout, and gear that changes the number. If the number is not shown, the mechanic does not exist.
11. **Campus as a system, not scenery.** Island PWR Sim' island, parking lot, truck, and train are set dressing plus one exit. Reactor-sim's campus phase should make the site boundary matter: deliveries, waste removal, grid connection, and the train/road exit as a real evacuation decision with a cost (abandoning the plant vs. staying).
12. **Co-op only after single-player is stable, and say so publicly.** Island PWR Sim' honest forum line ("when the game stops receiving major updates, I'll be able to get into multiplayer again") is the right communication and the wrong sequencing. Reactor-sim's LAN co-op should wait until saves, faults, and the helper are deterministic; the deterministic core is precisely what makes lockstep co-op feasible, so the wait buys something.

### Do not copy

- "No hand-holding" as a design identity. Replace with: a tutorial that covers startup *and* the first fault, first maintenance cycle, and first chemistry regeneration; a manual that matches the build; community guides linked in-game.
- Save-breaking major updates. Version saves, migrate them, test the migration in CI (golden replays already planned for reactor-sim make this cheap).
- Small paid DLC that touches core capability (AO speed). Cosmetic or genuinely additional equipment DLC is fine; helper power is not.
- A Blogspot blog as the API docs. In-repo, versioned, generated from the same source as the API.

## 11. Open questions (need a play session or developer source to answer)

1. What is the actual pH variable (range, gauge, effect on corrosion rate), if any, and what named failure does corrosion cause?
2. What are the Prestige/XP numbers (income per MWh, fuel-block cost, gear cost, waste cost, audit thresholds)?
3. What is the full AO skill list and what does each level unlock in the base game (excluding the DLC)?
4. What is the fault spawn model (rate vs. difficulty, per-system weights, any "grace period" after startup)?
5. Is there any dose/radiation number at all, and what kills the player besides electrocution/meltdown?
6. Has there been a patch after V 2.2.25.219 (22 Jul 2026)? The public record found here goes quiet after that date.
7. What is the AO Extra Power Pack's price, and does base-game AO speed feel inadequate without it? (Review aggregations flag pay-to-win concerns, but do not name the DLC.)

## Sources (actually read for this report)

- https://[commercial-pwr-product-url-omitted] home page (200+ parameters, 150+ switches, 9 fuel blocks, 72 rods, AO, chemistry, prestige; "Very Positive (84% of 1,092 reviews)", $14.99, published 2023-03-13)
- https://[major-PC-storefront-url-omitted] current store page (first-person pitch, city demand, crane, walkable plant, four difficulties, time freeze, train escape)
- https://[commercial-pwr-product-url-omitted] PWR explainer, updated 2026-08-06
- https://[commercial-pwr-product-url-omitted] accidents explainer, updated 2026-08-06 (defence in depth, Chernobyl/Fukushima framing, "models / does not claim" scope)
- https://[commercial-pwr-product-url-omitted] MicroProse announcement (200+ params, AO, prestige, chemistry, faults)
- https://steamcommunity.com/app/1428420/discussions/2/4357872216490878004/, "Chemical Treatment (Tutorial)" (Mudraxus, May 2024; 3,000/3,400–3,500 ppm band, dosage/filter numbers)
- https://steamcommunity.com/app/1428420/discussions/2/632295902087801142/, "How do you add chemicals other than boron?" (ion column, AN-31 resin, NaOH regeneration, waste tank, MO1, delivery truck, NaOH pump-overload bug)
- https://git.dominik-roth.eu/dodox/NuCon/src/branch/master/README.md, NuCon (typed API, 53 valves, ~290 obs / ~30 actions, NuconEnv / NuconGoalEnv, HER, kNN-GP simulator, model-learning loop, dummy/cheat modes)
- https://github.com/Dutchevil/Community PWR PID controller, Community PWR PID controller (C#, fork of GHXX; PID 310–360 °C, reactivity ~0.10, boron ~3,300 ppm, condenser ~65 °C, xenon rescue rule; webserver default http://localhost:8785)
- https://steamdb.info/patchnotes/24343829/, V 2.2.25.219, 22 Jul 2026 (audits ignoring fires, shutdown-request penalty, crane, lighting, tablet)
- https://steamdb.info/patchnotes/18595770/, V 2.2.23.190, 24 May 2025 (webserver: FUN_DECREASE_INTEGRITY incl. generators/turbines, FUN_FIRE_DRILL, CORE_EMERGENCY_STOP, custom port/interface, autostart, LIST vs VIEW variables, boron variables)
- https://steamdb.info/patchnotes/18945884/, Condenser Vacuum System Overhaul, 20 Jun 2025 (ejector + vacuum pump, WEBSERVER_BATCH_GET)
- https://steamdb.info/patchnotes/13263211/, Chemical treatment module, 24 Jan 2024 (co-op disabled; save incompatibility with the chemical module)
- SteamDB patch notes V 2.2.25.205–215, Jan–Apr 2026 (save-slot cap at 7, Steam Cloud in progress, AO charger/pathfinding/save-load bugs, Fusion 2.0 co-op fixes)
- https://steamdb.info/app/1428420/dlc/ and https://[commercial-pwr-product-url-omitted] DLC list; AO Extra Power Pack published 2026-08-11 (adjustable 0–100% work power, faster move/scan/repair, same battery); Island Fauna $3.99, Apr 2026; New Generator Set $2.99, May 2024
- https://[commercial-pwr-product-url-omitted] review aggregation (steep learning curve, bugs/save-load/AO, performance: 29–111 fps on 9950X3D + RTX 5090)
- https://[commercial-pwr-product-url-omitted] Steam review analysis, Feb 2026 (82% Very Positive, 988 reviews at that snapshot; documentation, multiplayer removal, DLC/pay-to-win concerns)
- https://[commercial-pwr-product-url-omitted] Steam review excerpts (Jul 2026 "early access flag" review; Apr 2026 real-operator review; Nov 2026 tutorial-too-short review)
- https://[commercial-pwr-product-url-omitted] and https://techmash.co.uk/tag/nuclearpowerplant/, secondary summaries (demand curve, crane, walkable plant, no-black-box fault tracing, solo developer Iván Marcelo Pretti)
