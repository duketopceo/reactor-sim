# whatisnuclear/simulator: deep-dive research

**Repo:** https://github.com/whatisnuclear/simulator
**Researched:** 2026-11-25, read-only via GitHub API (`gh.py`) plus live fetch of `live.whatisnuclear.com`
**Bottom line up front:** this repo is not a simulator. It is a 9 KB requirements and design document with zero lines of code. Everything genuinely good about the project lives in the README's thinking and in a tiny side project (a point-kinetics toy) that shipped outside this repo. The main lesson for reactor-sim is negative: a good design doc, a generality-first architecture, and a research-grade physics ambition produced three README commits in fifteen months and then silence.

## 1. What the repo actually is

Verified from the GitHub API on 2026-11-25:

| Fact | Value |
|---|---|
| Created | 2019-04-07 |
| Last push | 2020-07-06 |
| Stars / forks / watchers | 34 / 2 / 34 |
| Open issues | 4 (see section 5; three are 2026 noise) |
| Pull requests, ever | 0 |
| Branches | 1 (`master`) |
| License | MIT |
| Languages | none detected (`{}`) |
| Recursive file tree | exactly 2 files: `LICENSE.txt` (1,068 B) and `README.md` (8,986 B) |
| Commits, total | 3, all by Nick Touran (`partofthething`, nuclear engineer near DC, per his GitHub profile) |

Commit history, complete:

1. `5ee8698`, 2019-04-07, "Initial commit", LICENSE.txt + README.md (138 lines).
2. `1389fe0`, 2019-06-18, "Added progress in the key decisions", README +81 lines.
3. `8d232ff`, 2020-07-06, "Live PKE link", README +7 lines, adding a link to the live kinetics applet.

No code was ever committed. No tests, no CI, no deployment config, no frontend, no backend. The two forks (`sevalar/simulator`, `Khaledasad1963/simulator`) are byte-identical mirrors last pushed the same day, 2020-07-06. The sole contributor in GitHub's count is `partofthething` (3 contributions). Any framing of this as a working web simulator describes the ambition, not the artifact. That distinction matters because the repo's 34 stars are for a good idea well written down, not for a working thing.

## 2. What the README proposes

The README is the whole project, and it is worth reading in full. It is a crisp requirements document with six numbered requirements and a "Key Decisions" log.

### 2.1 Requirements R1–R6

- **R1:** dashboard reacts to user actions within 1 second. "Speed is more important than physical accuracy." One server should support at least 20 concurrent users.
- **R2:** "The system shall capture the attention of a 14-year-old for 4 minutes." The outreach mission stated as a measurable constraint.
- **R3:** simulate an AP1000-like LWR, an MSR, and an SFR. Design decisions should keep a variety of reactor types in mind (LWR, MSR, HTGR, SFR, …) to stay relevant to the widest users and developers.
- **R4:** no specialized software to install; standard web technologies, runnable on common household PCs.
- **R5:** operable on a mobile device, a normal PC with a 15-inch monitor, and a PC with a 50-inch monitor.
- **R6:** source readable and editable broadly, worldwide (open source on GitHub).

### 2.2 Proposed stack (explicitly a "proposal/straw man")

- **Backend: Django.** Sessions, API, reactor configurations, "and (maybe) physics." PyRK named for kinetics. If Django/Python is too slow, "use something else for physics, including (possibly) client-side physics."
- **Frontend: HTML5/JavaScript/AJAX.** ChartJS (with the streaming plugin), Gauge.js, Material UI named as candidates.
- **Hosting:** a whatisnuclear VPS ("we have servers and sysadmins that can get us started"), with load balancing later or physics shifted to the client if it gets popular.
- **License:** "Probably a MIT license, but maybe GPL." The repo ended up MIT.

### 2.3 Key decisions (the best part of the document)

**UI: SVG.js for the animated reactor scene.** Rationale is empirical, not fashionable. Author `partofthething` built a proof of concept: a pump animation combining JS-generated SVG (rotor) with Inkscape-drawn SVG (casing), rotor speed driven by an HTML5 range input, confirmed working in Firefox and Chromium on Linux and Android. Stress test: 100 rotors spinning at different speeds "rendered just fine on a 2013 laptop." A primary/secondary loop demo was posted at `partofthething.com/spintest-svgjs.html`. Alternatives considered and rejected with reasons: Velocity.js (could not change animation speed interactively), vanilla JS (code got too big for simple animations), GSAP (license seemed odd). Concerns recorded: "None! This library looks great." Gauges/graphs/indicators were explicitly left open to ChartJS, Gauge.js, Material UI as appropriate.

**Physics/session: Django, with Django Channels.** Rationale: many simple reactor sim projects (PyRK) are Python-accessible, collaborators know Python, and Django was "the only web framework the team has experience with." A Channels proof of concept gave "seemingly instantaneous" full-duplex interaction between a UI element, a trivial server calculation, and a UI response; Channels' `scope` concept was expected to allow multiple independent simulations at once. Concerns: "Django is not the fastest web framework out there, and performance may become an issue." Alternatives considered: "Honestly, none."

**Physics core: build their own 1-D transient coupled neutronics/thermal-hydraulics code.** Still "in investigation" at the last commit. The lean was to rebuild Sandia's 1984 code TOPAZ in Python and pass results to the frontend via Django Channels. Rationale: TOPAZ is 1-D, handles 1- or 2-phase flow, compressible or incompressible, is well documented, and has test problems for validation. "If this ran on awesome computers in 1984, it should run fine for simple problems in the web browser today." Steam tables first; because TOPAZ looks things up by pressure and internal energy, "any coolant can be added in later." Concerns, quoted verbatim: "Is it too crazy to write our own transient code for this? Possibly, but it's also pretty neat and fun. Let's see how it goes."

**License and first plant:** both decision entries are blank. First plant is only "Probably best to just do a PWR."

### 2.4 Project plan

- **Phase 1, Technology Stack Shakedown:** dummy components, a UI slider passing data to the backend and the squared value showing up on a live plot, load experiments, trivial animations. A "dummy kinetics solver" note points to the live PKE applet at `live.whatisnuclear.com`, with solver source at `partofthething/keepinpace` and "source for webapp coming soon."
- **Phase 2:** design a DB schema "that allows a variety of reactors to be simulated," plus "supersimple physics engines (0-D, 1-D T/H, etc.)" and the control panel plus reactor animation.
- **Phase 3:** build a full-on PWR simulator. Test and adjust.
- **Phase 4:** "Other reactors, anyone?"

### 2.5 Related projects named in the README

- IAEA nuclear reactor simulators for education and training: "Many of these don't meet the requirements of easy access/use/development or multi-reactor capabilities."
- a defunct legacy browser plant sim (domain omitted): "very similar in concept and operation to what we want. We just want it to be a little bit more interactive, with better graphics, more live plots, and possibly sound effects." Note: this comparator is now defunct. Web search on 2026-11-25 finds it described as defunct and playable only via the Wayback Machine (web.archive.org snapshot 2020-02-21). The README's main prior art implementation died the same way the README's own project did: no maintainer, no funding, no loop.

## 3. What actually shipped (outside this repo)

Two things shipped, neither in `whatisnuclear/simulator`.

### 3.1 keepinpace: the real kinetics solver

`partofthething/keepinpace` (created 2019-12-21, last push 2019-12-27, 1 star, 0 forks, Python) is a small point-kinetics solver. I read `solver.py` (2,791 B) and `interactive.py` (7,529 B) in full.

- `solver.py`: `KineticsSolver` integrates the point kinetics equations with 6 delayed-neutron precursor groups, using `scipy.integrate.solve_ivp` with method LSODA (comment: LSODA is "infinitely" faster than default RK45 for this problem), 5 evaluation points per second, state carried forward between runs for continuation. Delayed-neutron data comes from TerraPower's ARMI (`armi.nuclearDataIO.dlayxs`).
- `interactive.py`: an `InteractiveKinetics` wrapper that pre-computes a 20-second future horizon, then lets the future "flow into the present in real time." Past is plotted solid, predicted future dotted; changing reactivity recomputes the future from the current state. The matplotlib prototype has a reactivity slider (−5 to +1 dollars, in the code's units), a prompt-jump marker (red dot showing where the prompt-jump approximation would put power), and a bar chart of "what's driving neutron population changes" (prompt fission vs each precursor group). The docstring is explicit that matplotlib is a prototype of "the ultimate system, which will have UI elements and control in a web-browser."
- Honest limits, from its own README: "Real kinetics work requires thermal feedback and other stuff. For real work, I recommend checking out https://pyrk.github.io/." The `requirements.txt` pins ARMI at `-e https://github.com/terrapower/armi.git@develop`, i.e. a moving target on a develop branch, which is fragile dependency practice and a small sign of the project's prototype status.

### 3.2 live.whatisnuclear.com: the one-slider PKE applet, still live

Fetched 2026-11-25. The page is alive and working. It is the entire shipped product of this whole effort, and it is one slider: reactivity in cents, driving a server-side point-kinetics solve with 6 delayed-neutron groups, plotted live. The page gives the user one goal: "Try to bring the reactor up to 10,000 Watts and hold it steady there." Inline explainer text covers supercritical vs subcritical, reactivity in dollars/cents (Manhattan Project code-name origin), and prompt vs delayed neutrons ("delayed neutrons allow nuclear chain reactions to be controlled easily"). It is explicit about its limits: no thermal feedback ("does not (yet) include any of the thermal feedback mechanisms inherent in any high-power chain reactor"), and reactivity insertions are capped below a dollar because "the solver quickly runs into numerical troubles."

That applet is the proof that the project's minimal shippable slice worked. It is also the proof of how far the project got: one equation, one slider, no plant.

## 4. Zero-install web architecture: what it buys, what it costs

### What it buys

- **Reach is the whole point, and R4/R5 state it correctly.** A URL beats a download for a 14-year-old, a classroom, or a museum kiosk. No install, no admin rights, no platform build matrix. For outreach, that is not a nice-to-have; it is the distribution strategy.
- **The SVG schematic idea is genuinely good.** A vector plant diagram whose animation speed and color are driven by live state is the cheapest way to make a reactor legible: flow you can see, pumps you can watch spin faster, temperatures as color. The author's stress test (100 rotors, 2013 laptop) is the right kind of evidence, and SVG scales cleanly from a phone to a 50-inch display, which is exactly R5.
- **Per-user server sessions (Django Channels `scope`) are a coherent multi-user design** for a modest target: 20 concurrent users is honest and achievable. Sessions also give a natural home for saved state, leaderboards, or classroom use later.
- **The stack is maximally approachable.** Python plus HTML/JS is the lowest barrier for the volunteer contributors R6 wants.

### What it costs

- **Server-side physics is a tax on everything else.** Every user is server CPU; latency is network-bound; the 1-second R1 budget is spent on round trips before any physics runs. The README itself flags Django as possibly too slow, then picks it anyway because it is the only framework the team knew. The escape hatch ("shift physics to the client") is named but never designed, so the architecture has a known single point of failure with no plan.
- **Fidelity is explicitly traded away.** R1 says speed matters more than physical accuracy. That is fine for a 4-minute outreach toy and fatal if the project later wants to be trusted. There is no path in the document from "fast and approximate" to "verified and trustworthy," no validation plan beyond TOPAZ's own test problems, and no determinism story (server-side, floating point, network jitter).
- **No offline use, no 3D, no haptics, no walkable plant.** None of that is in scope for this project, but it bounds what the architecture can ever become. A browser page cannot grow into reactor-sim's phases 2–8 (walkable buildings, crew, radiation, maintenance) without becoming a different product.
- **Hosting is hand-waved.** "We have servers and sysadmins" is not a deployment plan, and no deployment ever appears in the repo. The only thing that ended up hosted is the PKE applet, which lives outside this repo with its webapp source "coming soon" (it never came).

### Implication for reactor-sim

Unity already pays the install price and buys the 3D/walkable plant in return. Do not port the server-side physics architecture. The steal is the shareable layer: a web demo (even a read-only live schematic, or a one-slider kinetics toy in the browser) as the top of the funnel, plus the SVG lesson translated into Unity UI: animate flow and state on the diagram itself instead of showing a table of numbers.

## 5. Multi-reactor design goals (LWR/MSR/SFR): help or hurt?

Hurt, in this case, and the mechanism is visible in the document.

- **Generality was required before instance one existed.** R3 demands LWR, MSR, and SFR up front, and Phase 2 starts with "design a DB schema that allows a variety of reactors to be simulated" before any physics engine exists. That is architecture-first sequencing: the abstraction is designed before there is a single working thing to abstract over.
- **The generality mechanism is real but premature.** The coolant-agnostic idea (look up properties by pressure and internal energy so any coolant can be added later) is sound engineering at the physics boundary. But it was attached to a from-scratch 1-D transient code that did not exist yet, so the "any coolant" benefit was purely theoretical.
- **The blank decisions tell the story.** The README's own decision log leaves "Choose license" and "Choose first plant to model" empty. A project that cannot decide its first plant but has already required three reactor types has its priorities inverted.
- **Contrast with what shipped.** `keepinpace` shipped because it was one reactor physics, one equation, one slider. The multi-reactor design produced zero lines; the single-physics toy produced a live page still running six years later.

For reactor-sim: keep the 160 MWt PWR SMR as the only reactor through Phase 1. If a second reactor type ever comes, it should be a data-driven configuration on top of a proven, validated core, not a schema designed up front. Steal the coolant-agnostic state-variable interface (pressure plus internal energy at the physics boundary); do not steal the DB-schema-first approach.

## 6. Why it stalled

Evidence, not speculation:

1. **Nothing was ever built in the repo.** Fifteen months, three commits, all README-only. The project never crossed from document to code. That is the primary cause; the rest are contributing factors.
2. **The hard part was put on the critical path and then made harder.** Instead of shipping the trivial PWR on a 0-D model first (Phase 2 even names "supersimple physics engines" as an option), the plan's physics core became "rebuild Sandia's 1984 TOPAZ in Python." Writing a research-grade 1-D transient thermal-hydraulics code from scratch is a multi-month undertaking for a specialist, and it was the dependency for everything downstream. The author knew it ("Is it too crazy...? Possibly") and proceeded anyway.
3. **Single-maintainer bottleneck.** Nick Touran wrote all three commits. The only other participants were commenters on issues. Zero pull requests in the repo's lifetime. There was no bus factor and no on-ramp: no code to contribute to, no build instructions, no good-first-issue, nothing to clone and run.
4. **No forcing function.** An outreach side project with no deadline, no users waiting, no shipped loop producing feedback. The only external pressure in the record is issue #3, "Any news on this?" (2019-06-18), answered the same day with more README. After the July 2020 link addition, silence.
5. **The org moved on, not away.** The `whatisnuclear` org is active: its `website` repo was pushed 2026-11-25 (the day before this research), and it created new repos in 2025 (`nuclear-economics`, `nuclear-plant-anki-deck`). The simulator was not abandoned because the org died; it was deprioritized while the org's main site and other outreach material continued. There is no evidence any other repo took over simulator work (five org repos total, none a simulator).
6. **The stack's known risk was never retired.** Django's slowness was flagged in the README as a concern, with no alternative evaluated ("Honestly, none") and the client-side-physics mitigation never attempted. Phase 1's shakedown (slider to squared value to live plot) was specified in detail and, as far as the repo shows, never executed. The 2020 commit only links the PKE applet; the webapp source for it was "coming soon" and never arrived in this repo.
7. **The 2026 issues are noise, not revival.** Issues #4, #5, #6 (opened 2026-09-26) restate Phase 1 as boilerplate tasks (Django plus Channels backend, SVG.js plus ChartJS frontend, slider-to-squared-value end-to-end proof of concept). They were filed by `npm-install-fluffypaws`, an account created 2026-09-25 with 0 followers and 3 public repos, with zero comments, zero linked PRs, and no maintainer response. The repo's `updated_at` of 2026-09-26 is entirely due to these three issues. Do not mistake them for renewed activity.

## 7. Educational design: the 4-minute problem

### What is good

- **R2 as a measurable requirement is the single best idea in the repo.** "Capture the attention of a 14-year-old for 4 minutes" is a design constraint you can test, not a vibe. Most sim projects never state their engagement bar at all.
- **One verb plus one number plus hold it steady.** The live applet's entire pedagogy is: move one slider, watch power, hold 10,000 W. That is a complete learning loop in under a minute, and it teaches the one concept (delayed neutrons make control possible) that the page also explains in plain language right next to the toy.
- **Contextual hints on wrong actions.** From `partofthething` in issue #1, praising a defunct legacy browser plant sim (domain omitted): hints like "try turning on the pump before increasing its speed" when the user does things in the wrong order. Cheap to implement, high teaching value, and it turns errors into lessons instead of dead ends.
- **Immediate visual feedback.** Rotor speed tied to a slider, animated flow, color and motion as state. The operator sees cause and effect without reading a number.
- **Progressive disclosure, named but not built.** Issue #1 suggests gating escalating complexity (Infinifactory, Satisfactory, Minecraft mods as models): the player earns understanding of one concept before the next is introduced. Issue #1 also proposes simulation vs training vs game as explicit modes, which the commenter calls "a good set of milestones."
- **Plain-language explanation co-located with the controls.** The live page explains dollars/cents, prompt vs delayed neutrons, and supercritical vs subcritical in the same view as the slider, not in a separate manual.

### What is weak or missing

- **The audience conflict was identified and never resolved.** Issue #1 (comment by `partofthething`): children of the general public need fun and less realism; engineering students, advocates, and designers need realism. "Perhaps we can make this configurable somehow" is not a decision, and no decision followed.
- **"Nuclear plants are fairly boring to operate by modern gaming standards."** The author's own words (issue #1). His hope was that the thing would be "neat-looking and interesting enough to have some wow factor, giving whoever's standing there time to explain a few things." That concedes the sim alone does not hold attention; it needs a human explainer standing next to it. For a game, that is a design failure to plan around, not a hope to rely on.
- **No onboarding, tutorial, level design, or progression was ever built.** No failure modes, no scenarios, no goals beyond the applet's hold-steady challenge, no sound (mentioned as an aspiration in the README's comparison to a defunct legacy browser plant sim (domain omitted)).
- **The 4-minute metric was never instrumented.** No analytics, no playtesting reported, no evidence anyone's attention was ever measured. R2 is a requirement with no verification method.
- **Multiplayer was discussed and dropped.** Issue #2 (Karl Alex Pauls, a games-industry voice) argues from Artemis Bridge Simulator and Space Team that chained 3–5 person work tasks create responsiveness, that 50–350 ms is the real bar for realtime multiplayer, and that commands should be queued, rate-limited, and promptly acknowledged even if the sim lags. The author agrees ("This thing won't be much different from a game. It is a game, really") and revises the target to 350 ms. Nothing multiplayer was ever built.
- **A useful pointer that went nowhere:** `jakehader` (2019-04-14) points to `wgurecky/pyReactor` in issue #1. There is no record of it being evaluated.

## 8. Genuinely good, weak, missing

**Genuinely good:**
- R1–R6 as a requirements document: crisp, testable, honest about trade-offs (R1 explicitly prefers speed over accuracy).
- The SVG.js decision: empirical (stress test, cross-browser check), with rejected alternatives and reasons recorded.
- Latency thinking: R1's budget, issue #2's correction from 1 s to 350 ms, and the acknowledge-input-immediately principle.
- The PKE applet as a minimal shippable slice: one equation, one slider, one goal, still live six years later.
- Hint-on-error, hold-steady goal, plain-language explainer next to the controls.
- The coolant-agnostic state-variable idea (pressure plus internal energy) at the physics boundary.

**Weak:**
- Django as the physics server: a known-slow choice adopted because it was the only framework the team knew, with no alternative evaluated.
- TOPAZ rebuild as the critical path: research-grade scope before a playable plant.
- Generality before instance: multi-reactor schema before reactor one.
- Blank decisions in its own decision log (license, first plant).
- "Webapp coming soon" for the PKE applet: announced in the README, never delivered in the repo.
- Dependency fragility in the one shipped code artifact (`keepinpace` pinning ARMI `develop`).

**Missing:**
- Everything executable in the repo itself.
- Any plant schematic, control panel, pump, heat exchanger, turbine, or coolant loop.
- Thermal feedback, xenon, decay heat, or any plant system beyond point kinetics.
- Failure modes, scenarios, progression, sound, multiplayer, analytics, playtesting.
- Any validation beyond "TOPAZ has test problems" (aspiration, not execution).

## 9. What reactor-sim should steal, and what to avoid

Reactor-sim's position is already stronger than this project's in the ways that matter: a deterministic C# core on .NET 8 exists, Phase 1 (trustworthy plant) is done, and the plan runs through phases 2–8. The steal list is about the top of the funnel and the first minutes; the avoid list is about the failure modes that killed this project.

### Steal

1. **An R2-style requirement, written down and instrumented.** Specify the first 4 minutes: cold iron to a held power level (e.g. raise rods, reach 10 MWt, hold within a band for 60 s), and measure completion rate. reactor-sim's Phase 1 proves the plant; the first-minutes spec proves a new player can reach it.
2. **The animated live schematic as the primary UI.** Flow speed, pump rotation, and color tied to actual state variables, not a table of numbers. This is the SVG.js lesson translated to Unity: the diagram is the interface, and motion is the feedback channel. Keep it cheap to render; the 100-rotors-on-a-2013-laptop anecdote is a reminder that schematic animation does not need GPU budget.
3. **Contextual hints on wrong-order actions.** "Start the pump before raising its speed" is the pattern. Implement as a hint system keyed to action preconditions in the deterministic core, so hints are derived from the sim state rather than hard-coded strings per button.
4. **Acknowledge input immediately, even if the sim step lags.** Issue #2's principle: the control visibly responds in tens of milliseconds; the physics catching up a step later is acceptable. Keep the core's step budget explicit so this stays true as systems are added in phases 2–8.
5. **The one-slider kinetics sandbox as a tutorial level zero or a web companion.** Before the full plant, teach delayed neutrons with one control and one goal (the live applet's hold-10,000-W pattern). It is also the cheapest shareable web demo: a URL that teaches the core concept with no install, funneling into the Unity game. This is the one place a browser build earns its keep.
6. **Coolant-agnostic state variables at the physics boundary.** Pressure plus internal energy as the lookup key, so a future coolant or reactor variant configures the core rather than rewriting it. Do this at the API level inside the existing C# core, not as a database schema.
7. **Plain-language explainer next to the controls.** One or two sentences per system, in the control-room view, explaining what the player is seeing and why it matters. The live applet's dollars/cents paragraph is the model: short, human, adjacent to the control it explains.
8. **Explicit modes, decided early.** Simulation vs training vs game (issue #1's suggestion). reactor-sim already has a game framing; commit to it, and make the tutor/explainer a mode rather than a compromise that blurs the audience.

### Avoid

1. **Generality before instance one.** Do not design multi-reactor support (or a multi-reactor schema) until the 160 MWt PWR is playable end to end and validated. R3 as a day-one requirement is what produced a schema design and no reactor.
2. **Escalating the physics core before the game is playable.** The TOPAZ rebuild is the cautionary tale: a research-grade 1-D transient code as the dependency for everything. reactor-sim's current scope (6-group kinetics, IAPWS-IF97, lumped thermal-hydraulics) is the right size. Do not grow it to 1-D transient T/H until Phase 1 is playable, validated, and asking for it.
3. **Server-side physics for a game.** Latency budget, hosting cost, no offline play, no determinism story. The local deterministic C# core is the correct architecture; the web is for demo and sharing, not for the sim loop.
4. **README-driven development.** Three commits in fifteen months, all documentation, is the failure mode to name explicitly in the roadmap: gate every phase on a playable build, not on a decision document. If a phase produces only a design doc, it has not shipped.
5. **The single-maintainer trap.** Zero PRs, no contribution path, no build instructions. If reactor-sim is open, keep the path real from day one (build docs, runnable in minutes, labeled first issues). The September 2026 drive-by issues on this repo show what a dormant repo with no maintainer attracts: noise, not help.
6. **Leaving the audience decision open.** "Configurable somehow" killed the engagement design here. Pick the audience per mode and design each mode's difficulty and realism explicitly; do not let one mode serve children and engineers at once.
7. **Announcing deliverables before they exist.** "Source for webapp coming soon" stayed a README line for six years. Do not put a feature in the roadmap's public face until a build demonstrates it.

## 10. Sources actually read

- GitHub API, `whatisnuclear/simulator`: repo metadata, recursive tree, all 3 commits with file stats, all 6 issues with bodies, comments on issues #1–#3, pull list (empty), forks, contributors, branches. All fetched 2026-11-25.
- `README.md` in full (8,986 B, decoded from base64 API response).
- GitHub API, `partofthething/keepinpace`: repo metadata, recursive tree, `solver.py`, `interactive.py`, `README.md`, `requirements.txt` in full.
- GitHub API, `whatisnuclear` org repo list; `npm-install-fluffypaws` and `karl-alex-pauls` user records.
- `https://live.whatisnuclear.com`, fetched 2026-11-25: live PKE applet page text.
- Web search, 2026-11-25: a defunct legacy browser plant sim (domain omitted) status (defunct, Wayback-only per forum comment).

### Verification gaps (not retried, not assumed)

- The SVG.js proof-of-concept links were not fully verified: `partofthething.com/spintest-svgjs.html` returned no extractable content on fetch (2026-11-25); the jsfiddle link was not opened. The PoC's existence is attested only by the README.
- TOPAZ/OSTI claims are cited from the README, not independently verified.
- a defunct legacy browser plant sim (domain omitted) being defunct rests on a forum comment and search results, not a direct fetch of the site.
- The characterization of issues #4–#6 as drive-by noise rests on the filer's account metadata (created 2026-09-25, 0 followers, 3 public repos) plus zero comments, PRs, or maintainer response; intent cannot be proven from the API.
