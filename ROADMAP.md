# Reactor Sim roadmap

## Product goal

Build an original, walkable nuclear-plant operations game: operate the reactor and grid from a tactile control room, walk through connected plant buildings, diagnose and repair equipment, manage radiation exposure, and work alone or with a crew. Simulation, visual equipment, alarms and sound should agree about the same plant state. Each milestone must be playable before the next one expands it.

This roadmap describes intended work, not a release promise. Progress is measured by the acceptance gates below rather than dates or completion percentages. Final crew size, severity of health effects, online transport and economy balance remain open until designed and playtested.

## Current baseline — November 2026

- A deterministic PWR-style core models point kinetics, delayed neutrons, reactivity, rods, chemistry, xenon/iodine, decay heat, primary/secondary heat transfer and pressurizer behavior. The balance-of-plant model includes turbine/generator, condenser, feedwater, cooling and grid trips.
- A walkable control room, underwater reactor-hall view, controls, screens, alarms, startup checklist, assistant, sandbox and missions exist. The separate plant wall shows several live readings and state-driven visual cues. Some plant elements and readings still lack visual or usability sign-off.
- Host-authoritative TCP networking can synchronize plant snapshots and player intents for LAN co-op. It is not yet a finished online lobby or a verified, full-body shared-world experience; the Fusion integration is a stub.
- Procedural plant sounds and synthesized voice lines exist. Mixing, actual listening tests, source coverage, spatial accuracy and cross-state consistency still need work.
- Walkable turbine and support buildings, radiation exposure/contamination gameplay, maintenance/repair loops and a finished player character are not present as complete gameplay systems.
- CI records three known verification XFAILs (`DOM-01`, `SCR-02`, `EDGE-08`). Snapshot compatibility, scenario verification, deterministic saves and Unity scene checks remain ongoing gates.

## Phase 1 — Make the current plant readable and trustworthy

**Player outcome:** one operator can start, run, trip and recover the existing plant, and understand what the equipment is doing.

- Finish the live counterpart and wall work: clear status summary, readable system metrics and units, source inventory, directional cues only for modeled paths, and explicit unavailable states. Connect room/hall equipment, alarms and sounds to their authoritative snapshot values.
- Finish control-room labels, physical interaction feedback, checklists, textures, lighting, water, accessibility and performance. Validate screen and reactor views from normal player positions.
- Audit and mix core, pump, steam, turbine, generator, cooling, water, control, alarm and voice audio. Add category volume controls, spatial placement and event priorities; audition builds in quiet, startup, full-load and emergency states.
- Address the three documented physics XFAILs with regression scenarios. Verify save/load, startup, transient and trip behavior without changing golden results silently.

**Gate:** cold startup to grid, steady operation, SCRAM and turbine trip are playable; wall/desk/hall/audio agree, numeric values are legible, and tests plus a documented visual/listening pass succeed.

## Phase 2 — Player and first walkable plant route

**Player outcome:** leave the control room and reach real equipment with a sense of scale and place.

- Create a simple first-person operator body, hands, shadows and interaction poses; improve locomotion, collision, doors, stairs and accessibility. Camera and interactions remain usable while seated or standing.
- Build a connected support corridor, reactor access area and turbine hall with doors, lighting, wayfinding and accessible equipment. Build a navigable route back to the control room, with game-scale clearances.
- Populate spaces with identifiable pumps, pipe runs, steam generator and turbine/generator/condenser equipment; provide observation boundaries where needed.
- Add streaming/occlusion, collision and light budgets, sound zones and location-aware interfaces. Equipment initially remains read-only unless a state-backed interaction is available.

**Gate:** a player can walk control room → reactor access → turbine hall → control room without getting stuck; observed running equipment matches desk readings and performs within agreed frame-time budgets.

## Phase 3 — Shared plant, visible crew

**Player outcome:** two people can reliably operate the same plant and see what the other person is doing.

- Harden host-authoritative LAN sessions: discovery or clear direct join, status/error handling, input validation, authority, late join, reconnect, save/load and conflict handling. Verify two real clients against one host, including trips and recovery.
- Synchronize player avatars, position/orientation, basic hand/control interactions, doors and relevant equipment state. Make alarm acknowledgement and procedure progress agree across clients.
- Introduce optional reactor operator, balance-of-plant operator and field technician roles with clear responsibilities and host-configurable access. Preserve solo operation and shared emergency actions.
- Decide and prototype internet transport/lobby/relay only after the LAN slice is reliable. Authentication, privacy, abuse controls and operating costs are release requirements. Expand beyond two operators only after measuring and testing capacity.

**Gate:** two players connect on separate machines, see each other, share a cold start and respond to a trip; reconnect/late join restores coherent plant and world state. Internet access is not claimed until independently verified.

## Phase 4 — Radiation and controlled access

**Player outcome:** a field operator can plan a task around radiation conditions and understand why access changes.

- Design a game-scale radiation model for sources, zones, shielding, distance/time, dose rate and accumulated dose. Separate design approximations from measured plant values and use authoritative, documented sources for any claimed physical constants.
- Add area radiation monitors, portable detector and personal dosimeter, alarms, access signs, time-in-area guidance and persistent player exposure state. Make readings understandable and consistent in solo and co-op.
- Add contamination as a separate system only when modeled: affected equipment/areas, survey, controlled movement and decontamination. Define protective-equipment effects, thresholds and consequences through playtesting.
- Connect abnormal source terms to supported equipment/failure state; unknown or unmodeled conditions remain unavailable instead of silently safe.

**Gate:** a player can survey a changing radiation zone, see consistent monitor/dosimeter readings, perform a timed task, exit and retain exposure across save/load and multiplayer; limits and feedback are documented and tested.

## Phase 5 — Field operations and maintenance

**Player outcome:** an alarm leads to a physical diagnosis, repair and verified return to service.

- Give named equipment persistent condition, faults and maintenance history. Begin with a pump, valve and electrical component; model only failures with clear state and observable symptoms.
- Add work orders, inspection, isolation/lockout, repair tools, spare parts and post-repair checks. Tie interaction permissions and consequences to the same authoritative simulation and shared world.
- Add field equipment controls and instruments, safety interlocks and coordinated desk/field workflows. Expand water, steam, electrical and auxiliary systems as needed for the first complete repair chain.
- Introduce fire, leaks/flooding and electrical faults one at a time with clearly modeled causes, alarms, propagation boundaries, response tools and recovery.

**Gate:** a scripted pump fault produces matching desk, field, sound and alarm cues; one or two operators isolate it, repair it, test it and restore service, with state preserved through reload.

## Phase 6 — Complete the plant campus

**Player outcome:** the facility feels like a coherent working site.

- Expand into electrical/switchgear and emergency-power rooms, auxiliary chemistry and cooling, maintenance/workshop, fuel handling/storage, access control and exterior cooling/switchyard areas.
- Make paths, labels, equipment IDs, process connections, access restrictions and camera feeds consistent with the simulated system graph and status wall. Add elevators or service routes only where navigation and synchronization are verified.
- Add weather, day/night and environmental ambience where they materially affect the experience or operations; keep plant access and performance robust.
- Build additions in small connected slices, each with at least one meaningful readout, task or scenario and an explicit budget for assets, lighting, memory and networking.

**Gate:** players can navigate the facility with consistent maps and equipment identities, complete a cross-building operating task, and maintain target performance in single-player and co-op.

## Phase 7 — Operations, progression and assistant

**Player outcome:** the plant has a reason to keep running beyond a single startup.

- Expand scenarios and shift objectives around startup, load following, shutdown, maintenance, component faults and safe recovery. Add demand, dispatch, outage reporting and clear consequences for service interruptions.
- Design a transparent economy for fuel, consumables, spares and maintenance; add optional progression and upgrades only after the operational loop is fun without grinding.
- Improve the assistant with explainable advice, step-by-step startup, state-backed control actions, clear permission boundaries and cancel/override. Consider a visible worker or remote tasks after navigation and maintenance systems exist.
- Add save slots, replayable incident history, settings/accessibility, onboarding, documentation and scenario-selection tools.

**Gate:** a player or crew can complete a multi-shift loop involving generation, a fault, field repair and safe recovery, save and resume it, and understand the outcome.

## Phase 8 — Release hardening and expansion

- Run sustained solo and multiplayer playtests, including reconnect, save migration, accessibility, localization, low-end performance and crash recovery. Fix the highest-impact defects before adding more content.
- Complete soundscape and visual polish, content licensing, install/update packaging, onboarding and support diagnostics. Audit public-facing claims against tested behavior.
- Consider additional reactor configurations, facilities, crew sizes, advanced chemistry/fuel handling and modding only after their data model, performance and test coverage are designed.

**Gate:** a reproducible release candidate passes automated checks and scenario suites, the complete playtest matrix, networking and save compatibility checks, and documented audio/visual acceptance.

## Cross-cutting rules

- Simulation remains engine-independent and host-authoritative. New persistent plant and player state must be versioned, saved, synchronized and tested.
- Use original assets, independently developed code and appropriately licensed material. Use real-world reference sources for engineering claims; distinguish a game approximation from an instrument reading.
- Keep simulation state, live visuals, audio, alarms and player interactions coherent. Unmodeled states must be identified as unavailable rather than invented.
- Ship vertical slices: a system is done only when its state, controls, feedback, save/load, relevant co-op behavior and tests work together.
