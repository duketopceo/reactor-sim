---
title: Whole-Plant Status Wall and Control-Room Visual Finish - Plan
type: feat
date: 2026-11-13
artifact_contract: ce-unified-plan/v1
execution: code
---

# Whole-Plant Status Wall and Control-Room Visual Finish

## Goal Capsule

- **Target repo:** the active `reactor-sim` repository. This plan builds on the current `feat/living-reactor-assets` checkout and the in-progress control-instrument work.
- **Objective:** finish the existing control-room visual pass and add a separate, wall-mounted 3D plant map that shows the reactor, fuel, primary and secondary piping, balance of plant, live readings, and one-glance equipment/alarm status.
- **Player outcome:** from the operator seat, a player can see where heat and steam are moving, what each major system is doing, and which component needs attention without opening a CRT or tablet page.
- **Product authority:** live `PlantSnapshot`, existing alarm sources, and `docs/design/plant-and-assets.md` own model values, alarms, naming and visual conventions. The diagram must not invent data or alter simulation physics.
- **Delivery:** one reviewable implementation with pure status/layout logic, Blender-authored board geometry, Unity runtime binding, and visual evidence. Keep unrelated working-tree changes intact.

## Current Asset Inventory

This describes assets present in the active checkout, not a claim that every visual has passed final in-game review. The current branch also has local, uncommitted scene, hall, interaction, lighting/audio, test, and asset-manifest work; preserve those edits while implementing this plan.

| Area | Already in the repo | Remaining to finish |
|---|---|---|
| Control-room geometry | `Blender/ControlRoom.blend` and `Blender/exports/ControlRoom.fbx` provide the room and control-room layout. | Review room scale, window/view alignment, and panel readability in the final scene capture. |
| Panel and instrument assets | `Blender/PanelKits.blend` and `PanelKits.fbx`; runtime control, gauge, lamp, CRT, and button bindings; inventory-backed labels and panel placement. | Finish the current instrument-detail and interaction pass: distinct button/guard/knob shapes, clear pressed/armed states, readable labels, useful rejection feedback, and seat/standing-view verification. |
| Alarm and status surfaces | Annunciator tiles and atlas, live alarm predicates/feedback, alarm list/board models, CRT pages, a CRT named `CRT_MimicStatusBoard`, and an existing small decorative primary-loop wall mimic. | Complete the physical alarm-board readability and consistency pass. The existing small mimic and CRT are not the requested full-plant map. |
| Reactor and equipment | Procedural sources and FBX exports for `ReactorCore`, `VesselInternals`, `PlantEquipment`, and `RotatingEquipment`; the Unity scene places the reactor/hall assets and binds supported motion/state. | Validate the final sightline and lighting from the control room; add the new wall-map composition and any missing short connecting pipe runs or system labels. |
| Materials and textures | Blender trim/material pipeline, eight shared master material families, `AnnunciatorAtlas_24.png`, and exported base-color, metallic/smoothness, and screen-emission maps in `Blender/exports/Textures/` and Unity's Blender asset folder. | Review contrast on actual wall/panel surfaces and extend shared materials only when the wall display needs a missing treatment. |
| Lighting | Control-room and reactor-hall lighting logic/builders exist; the current working tree includes additional hall/light and window changes. | Finish glare, contrast, indicator-emission, and reactor-window checks against current captures; do not increase established light budgets to make the board readable. |
| Balance-of-plant spaces | State and some equipment visuals cover pumps, steam generators, turbine, condenser and grid signals; the Unity scene has equipment assets. | Full walkable turbine/condenser hall, cooling towers/outfall, electrical and auxiliary rooms, maintenance spaces, and a visible operator character remain later work. These are outside this wall-map milestone except for the board's schematic representation. |

The existence of an exported model is not the same as visual sign-off. The board feature may reuse current meshes at schematic scale, but it must identify missing geometry and state explicitly rather than silently showing a complete-looking but inaccurate plant.

## Product Contract

### Summary

Finish the existing alarm boards, buttons, panels, labels, and status feedback, then add a distinct whole-plant wall display. The new display is a low-poly, isometric/cutaway 3D system map on the control-room wall, with simplified equipment silhouettes, connected pipe paths, flow cues, component status lamps, and live metric callouts. Existing desk instruments, CRTs, tablet pages, and alarm acknowledgement remain in place and authoritative.

### Requirements

**Existing room finish**

- R1. Preserve current panel layout, interaction contracts, control IDs, inventory bindings, CRT pages, and alarm sources while completing the in-progress control-instrument work.
- R2. Physical alarm tiles, alarm boards/lists, lamp colors, audio/voice annunciation, and latched/acknowledged behavior tell the same story for each active condition.
- R3. Buttons, guards, selectors, dials and levers communicate rest, armed, pressed, latched, disabled, and rejected states with visible feedback consistent with the value actually accepted by the simulation.
- R4. Room, panel, reactor-window, and display lighting preserve legibility from seated and standing viewpoints without exceeding existing triangle, light, draw-call, or frame-time limits.

**Separate whole-plant map**

- R5. Add a dedicated, physically separate wall-mounted overview display. Do not repurpose the CRT mimic page or the small decorative primary-loop wall panel as the full map.
- R6. The map is a read-only overview with a 3D/isometric plant composition: reactor vessel and visible fuel region at the center; control-rod banks; primary pumps and piping; pressurizer; steam generators; secondary steam/feed paths; turbine/generator; condenser and cooling return. Use existing assets where practical and add only the geometry needed to make connections legible.
- R7. Pipe runs show system connectivity and direction of flow. Flow animation or segmented illumination scales only from real modeled flow/demand state; no animation implies a value the sim does not expose.
- R8. Major components show adjacent, clearly labeled status lamps for normal, attention, alarm/trip, and offline states. Missing/unmodeled data uses a neutral “N/M” or unavailable treatment rather than green/normal.
- R9. A compact metric region groups live values by system and includes units. At minimum it covers:
  - **Core:** power, period, criticality/SCRAM, reactivity total and key components, rod-bank actual insertion, fuel temperature, xenon/iodine.
  - **Primary:** temperature, pressure, saturation margin, inventory/level, flow, pressurizer heater/spray, relief.
  - **Steam generators/secondary:** pressure, temperature, level, feedwater flow/availability, steam demand/dump, relief state.
  - **Turbine, condenser, grid:** turbine RPM and trip state, turbine/generator power, generator sync, grid output/frequency, condenser pressure, hotwell inventory, vacuum-pump and cooling-water state.
  - **Plant summary:** operating mode, alarm count/first-out, active procedure step, and simulation clock.
- R10. Readings and status are derived from `PlantSnapshot`, the current alarm model, and the existing shared lamp predicates. Values update on the same snapshot cadence as the rest of the UI; no independent hard-coded thresholds are introduced.
- R11. The diagram remains a glanceable summary: high-priority safety state and first-out alarm are visible immediately; detailed trends, setpoints, and controls stay on their existing panels, CRTs, or tablet.
- R12. The map communicates topology without implying real-plant engineering drawings or exact layout. Keep it fictional and game-scale, consistent with the clean-room and safety boundaries.

### Acceptance Examples

- **Pump/flow change:** when primary flow falls, the pump and primary-flow indicator update and the connected pipe cue dims or changes state; the numeric flow uses the snapshot value and units.
- **High-pressure alarm:** a primary pressure warning changes the corresponding loop/component lamp and summary alert consistently with the desk annunciator, and the exact pressure remains readable on the board.
- **SCRAM:** rod-bank actual insertion animates from live insertion state, core power/status follows the snapshot, and the first-out/trip summary agrees with the alarm surface.
- **Unknown state:** if a field is absent or a value is invalid, that component is marked unavailable and never presented as healthy.
- **Viewpoint:** the system map and its safety summary can be read from the normal operator seat; closer inspection may use an existing screen-focus interaction without blocking room controls.

## Scope Boundaries

**In scope**

- Final visual/feedback verification for alarm tiles, alarm boards, control buttons, panel labels and status lamps already present in the room.
- A new board asset/generator, status view model, Unity integration, map geometry/pipe connections, live metrics, status lighting, and required tests/captures.
- Reuse of current reactor, vessel, pump, steam-generator, turbine and condenser representations where their scale and topology work on the board.

**Deferred**

- Full walkable turbine hall, condenser hall, outdoor cooling towers/outfall, electrical room, auxiliary cooling rooms, maintenance props, and operator character.
- New plant physics or states, plant camera network, interactive control from the wall map, multiplayer changes, and detailed historical trends.
- Full-scale engineering-accurate piping or a certification/training layout.

## Technical Decisions

- **KTD1. Separate surface, shared sources.** The wall map gets its own named scene root and display controller. It consumes the same snapshot/alarm state as current UI and does not duplicate the CRT or alarm-board model.
- **KTD2. A 3D schematic, not a second playable plant.** Use simplified board-scale silhouettes and connected pipes, with selected existing meshes for the vessel/core and recognizable equipment. Preserve topology and scale relationships for readability; do not clone the full runtime hall into the wall.
- **KTD3. One status mapping.** Reuse `LampPredicates`, `AlarmBoard`/alarm tracking, and existing color tokens where possible. Any new predicate is defined once in pure logic and covered by tests.
- **KTD4. Read-only and truthful.** The board submits no intents. Only existing snapshot fields appear as live values. Unmodeled auxiliary equipment, per-loop values, or missing sensors remain visibly unavailable until modeled.
- **KTD5. Blender remains the asset authority.** Author the wall board and any bespoke pipe/equipment detail with the existing Blender scripts/export/sync path; Unity owns runtime text, live lights and state binding.
- **KTD6. No extra lighting budget.** Prefer emissive materials/property blocks and the existing allowed lights. Board lamps and flow cues must remain legible under current lighting and exposure.

## Implementation Units

### U1. Close the current room finish

- **Goal:** complete R1-R4 against the current working-tree implementation.
- **Files:** existing control-instrument plan files, `ControlRoom3DBinder`, `LampPredicates`, `AlarmFeedback`, inventory/labels, room/hall builders, and their tests as required.
- **Approach:** use the existing alarm predicates and interaction bindings; complete button/guard/indicator silhouettes and visible state feedback; compare alarm boards, physical annunciators, voice/audio, and status labels for consistency.
- **Verification:** current layout, interaction round-trip, alarm, lamp and capture checks pass. Capture seated/standing views and inspect panel glare, alarm text and reactor-window contrast.

### U2. Define the plant-map data model

- **Goal:** R8-R11.
- **Files:** new UnityEngine-free `PlantWallMapModel` (or equivalent) and tests under `ReactorSim.Core.Tests`; binding adapter under `ReactorSim.Unity/Assets/Scripts/View3D/`.
- **Approach:** define named component/system nodes, snapshot selector, formatting/unit, status, flow direction/fraction, and unavailable reason. Map fields to the exact `PlantSnapshot` values and existing alarm/lamp sources. Keep thresholds in the shared status source.
- **Verification:** table-driven tests cover cold, normal, startup, high alarm, trip/SCRAM, cleared/acknowledged alarms, missing values, invalid numbers, and unit/label formatting.

### U3. Build the wall-map asset

- **Goal:** R5-R7 and R12.
- **Files:** new Blender generator and source asset (proposed `Blender/scripts/make_plant_overview.py` and `Blender/PlantOverview.blend`), exported FBX, sync/export lists, and asset manifest entries.
- **Approach:** compose a readable isometric cutaway around the vessel/fuel region. Connect primary and secondary paths with distinct pipe styles, then place the pump, pressurizer, steam generators, turbine-generator and condenser in causal order. Add reserved lamp/label sockets and consistent component IDs. Use existing meshes or simplified instances; no static values or baked live alarm colors.
- **Verification:** Blender headless generation/export succeeds, every component ID resolves, no lights/cameras leak into the FBX, geometry and material count stay within the room budget, and a turntable/board preview is reviewed.

### U4. Integrate live indicators and readings

- **Goal:** R7-R11.
- **Files:** `ControlRoomSceneBuilder`, a new `PlantWallMapView`/binder, shared runtime UI/theme helpers, and scene checks.
- **Approach:** place the separate board on an approved wall location with a clear seat sightline; attach emissive status lamps and runtime metric text to authored sockets. Use compact system groupings and clear units. Keep inactive or unmodeled equipment distinguishable.
- **Verification:** simulate representative state transitions and confirm the board, desk alarm tiles, CRTs and tablet agree. Check for duplicate roots after scene rebuild and for no false green states.

### U5. Visual, readability and regression gate

- **Goal:** all success criteria.
- **Files:** new map/layout/asset checks, capture scripts or scene evidence, `docs/PLAYTEST.md`, and `INDEX.md` if needed.
- **Approach:** capture normal operation, startup, high pressure, primary-flow loss, SCRAM and turbine/grid trip from the seated view, standing view and close inspection. Keep the map legible while the room remains navigable.
- **Verification:** relevant `scripts/ci.sh` checks pass with Unity available; room and hall budgets remain within existing caps; review captures by eye. Record any platform-limited Unity check as owner-run.

## Success Criteria

- Existing alarm boards, buttons, labels, panels, and status indicators have a consistent visual language and tested interaction/state feedback.
- A separate 3D wall map shows core/fuel, rod banks, primary and secondary pipes, steam generators, turbine/generator, condenser, and cooling return in one view.
- Live component lamps and numeric metrics agree with the exact same snapshot/alarm state used by desk controls and screens.
- The board is glanceable from the operator seat, provides detail without duplicating control functions, and marks unavailable state honestly.
- Rebuilding the room is deterministic, all authored map IDs and bindings resolve, current performance/light budgets hold, and clean-room checks remain green.

## References

- `ROADMAP.md` — current visual polish, operator character, plant visibility, cameras, and balance-of-plant priorities.
- `docs/design/plant-and-assets.md` — asset families, plant zones, state bindings and room adjacency.
- `docs/design/reactor-core-assets.md` — existing reactor/core assets and live binding contract.
- `docs/design/main-control-room.md` and `design/control-room/layout.json` — existing panel zones, sightlines and layout.
- `docs/plans/2026-11-12-0000-feat-control-instruments-upgrade-plan.md` — in-progress control, alarm and instrument finish.
- `ReactorSim.Core/Snapshot.cs` — live values available to the map.
- `ReactorSim.Unity/Assets/Scripts/View3D/LampPredicates.cs`, `ReactorSim.Unity/Assets/Scripts/UI/TabletPages.cs`, and `ReactorSim.Unity/Assets/Scripts/View3D/CrtPages.cs` — status and display sources to reuse.
