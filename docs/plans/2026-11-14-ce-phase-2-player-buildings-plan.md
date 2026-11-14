---
title: "Phase 2: Player and First Connected Buildings"
type: feat
date: 2026-11-14
artifact_contract: ce-unified-plan/v1
execution: code
status: planned
---

# Phase 2: Player and First Connected Buildings

## Goal and baseline

Give the operator a body and a continuous, navigable route from the control room through reactor access and a turbine hall and back. Today the control room is walkable and the hall is viewed through a window; `HallAssetPlacer` has miniature/cutaway equipment, not a complete walkable turbine building.

## Scope and dependencies

Build on phase 1's accepted controls and performance baseline. Create a single-player vertical slice first, with stable IDs and doors designed for phase 3 synchronization. Running containment remains inaccessible; reactor access is an observation/service area. Radiation, repairs, an economy and outdoor campus are not implied by this phase.

## Implementation units

1. **Space and route contract.** Update `docs/design/plant-and-assets.md` with authored coordinates, room graph, floor heights, access restrictions, service routes and equipment IDs. Resolve adjacency against the existing control-room/window layout. Define accessibility, doorway clearances, collision and a representative target-machine performance baseline before building.
2. **Player embodiment.** Add a simple original operator body, hands, shadow, seat/stand transitions and control-use poses compatible with `OperatorController`, camera clipping and the existing input gate. Provide settings for motion sensitivity and reduced camera motion. Avoid requiring a full animation system to ship the first slice.
3. **Connected environment.** Author Blender assets and deterministic Unity builders for control-room door, support corridor, reactor observation/access and turbine hall, with doors, stairs, railings, signs, lighting and a return path. Align player navigation and existing hall/window placement; no geometry may block control-room operations.
4. **Recognizable equipment.** Place correctly named turbine, generator, condenser, primary pump and pipe silhouettes. Bind only available motion/status to the shared snapshot; identify unavailable sensors and distinguish schematic from full-scale equipment. Add local ambience linked to actual operating state.
5. **Runtime budgets.** Partition and stream spaces where beneficial; add occlusion and LOD, manage light/audio sources and collider counts, and maintain navigation and scene rebuild determinism. Record measured metrics rather than guessing a final cap.

## Acceptance and verification

- Walk control room → reactor access → turbine hall → control room and the reverse without clipping, blocked doors or getting stuck, including from seated start and after save/load.
- Confirm visible pump/turbine states agree with the desk in cold, running and tripped snapshots. Interaction, camera, safety boundaries and accessibility settings do not disrupt existing controls.
- Rebuild the scene twice with stable object identity; verify navigation, colliders, light budgets and visual captures. Profile representative hardware and compare with the phase 1 baseline. Run repository CI and relevant Unity checks.

## Handoff

Document scene/asset ownership and every persistent door/equipment ID for phase 3. Later phases add authoritative multiplayer, controlled exposure and maintenance; their absence must be clear in the playable slice.
