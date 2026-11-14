---
title: "Phase 6: Connected Plant Campus"
type: feat
date: 2026-11-14
artifact_contract: ce-unified-plan/v1
execution: code
status: planned
---

# Phase 6: Connected Plant Campus

## Goal and baseline

Expand the phase 2 route into a coherent multi-building facility with recognizable systems and useful reasons to visit each space. Phase 2 supplies the first connected control-room, reactor-access and turbine-hall slice; existing campus sketches and asset lists are planning references, not completed rooms.

## Dependencies and boundaries

Requires phase 2 navigation and at least the phase 5 pump-maintenance slice; phase 3 is required for shared-world acceptance. Follow the restricted operating-containment boundary in `docs/design/plant-and-assets.md`. Add one connected area at a time rather than shipping empty shells as complete gameplay.

## Implementation units

1. **Campus topology.** Finalize a fictional route graph connecting control room, reactor access, turbine hall, electrical/emergency power, auxiliary chemistry/cooling, workshop, fuel handling/storage and exterior cooling/switchyard. Assign stable room, door, equipment and circuit IDs. Resolve map/camera labels and alternate route/locked-door behavior.
2. **First service building.** Add electrical and emergency-power or auxiliary space selected by the first complete cross-building scenario. Author assets, safe observation boundaries, doors, lighting, audio zones and accessible field instrumentation. Bind representations only to supported state and the existing maintenance task system.
3. **Campus expansion.** Add workshop/parts, fuel service, cooling exterior and switchyard in independent slices. Each area has at least one meaningful simulated state, task or documented future placeholder, not invented values. Plan fuel handling before claiming live refueling.
4. **Cameras and environment.** Add fixed plant camera views with clear IDs, location and unavailable states; ensure no camera implies a missing area. Add weather/day-night only when their gameplay and performance cost are measured. Synchronize any authoritative doors or environment effects in co-op.
5. **Streaming and budgets.** Keep the control room responsive; define scene loading, occlusion, LOD, navigation, audio and networking budgets per area. Prevent falls, inaccessible doors and lost task state when crossing streamed boundaries.

## Acceptance and verification

- In a built player, traverse each released area and an alternate return route, identify the same equipment on the map and physical sign, and complete a cross-building desk/field task.
- Test running containment restriction, access conditions, streaming reload, save/load, late join and disconnect; no loaded-out area loses authoritative plant or task state.
- Record solo and two-player CPU/GPU/memory/network profiles against agreed phase 2 baselines, visual captures and navigation/collision checks; run CI and Unity checks.

## Handoff

Maintain an area/asset/state inventory and mark unfinished rooms honestly. Phase 7 can add campus-wide work orders only for rooms and equipment whose slice has passed its gate.
