---
title: Live Plant Counterparts and Readable Status Wall
type: feat
date: 2026-11-14
artifact_contract: ce-unified-plan/v1
execution: code
---

# Live Plant Counterparts and Readable Status Wall

## Goal

Make the reactor hall, plant overview and operator instruments tell the same story as the running simulation. The player should be able to see which equipment is operating, where modeled energy and water are going, and what needs attention, then read the important values up close.

## Starting point

The wall sits left of the door and contains the core, rod banks, major circuits, turbine/generator, condenser and a steam-dump bypass. Rod positions and fuel glow follow the same state as the hall; primary circulation, delivered feedwater, cooling, turbine steam heat, dump steam heat, shaft speed and grid output affect their routes. Snapshot fields preserve delivered feed and dump heat across save/load. A mounted summary has been added beneath the map, but grouped component detail is not yet displayed. The wall remains schematic: several routes are static, equipment detail is limited, and the full range of operating states has not been visually signed off. The existing `DOM-01`, `SCR-02`, and `EDGE-08` verification XFAILs remain separate known defects. Phase 1 is coordinated in [the broader CE plan](2026-11-14-ce-phase-1-trustworthy-plant-plan.md).

## Requirements and work

1. **Audit every visible counterpart.** Create a binding inventory for wall components, hall equipment, indicators and sounds. Record each object's authoritative source, units, actual-versus-command semantics, current behavior and gaps. Use one shared mapping where wall and hall depict the same state; preserve the snapshot's D/C/B/A bank order.
2. **Make the wall readable and useful.** Provide a large safety/mode summary and legible grouped readings for core and rod banks; primary pressure, temperature, margin, level and flow; steam generator and feed; turbine, condenser, cooling and grid; chemistry and clock. Show real values with units and a clear unavailable state. Show first-out and procedure step only if an authoritative source is available; otherwise explicitly mark them unavailable. Do not invent a first-out from an unordered alarm list.
3. **Complete modeled motion and paths.** Add clearly labeled flow direction and visible state changes to modeled equipment and paths: pumps, steam generator/feed, turbine and generator, condenser, cooling and bypass. Use delivered or measured values where available, and label command/setpoint indications as such. Steam heat is an energy rate, not a measured mass flow. Routes without a modeled value must remain explicitly unavailable rather than implying normal operation. Add snapshot outputs only for state the simulation actually computes, including backward-compatible serialization and restore tests.
4. **Connect hall feedback.** Extend hall equipment, lighting, water effects and existing audio using the same sources and alarm predicates as the wall and desk. Keep effects legible at startup and after trips; avoid making decorative particles appear to be measured boiling or flow. Verify sound in an actual listening pass and respect master volume.
5. **Verify real scenarios and sightlines.** Exercise cold shutdown, startup, normal operation, pump/flow loss, high pressure, SCRAM, turbine trip, feed starvation and steam dump. Compare snapshot, wall, hall, alarm tiles and relevant screens for each. Capture the standing and close views, check that values are readable, and test absent, invalid and restored state. Keep scene rebuilds deterministic and preserve existing performance/light budgets.

## Acceptance

- Each represented dynamic component has an auditable source and responds to changes in that source; unmodeled readings are visibly unavailable.
- The wall's safety summary is readable at a practical standing position, and a close view makes grouped metrics and units readable without obscuring room controls.
- Pump loss, SCRAM, turbine trip and steam dump produce matching wall/hall/desk behavior; a tripped turbine does not imply steam is passing through it.
- All rod banks agree between the wall and full-size reactor through insertion, withdrawal and SCRAM.
- Automated model, snapshot, scene-binding and regression tests pass; visual and audio playtest results are recorded with any remaining limitations.

## Boundaries

This phase does not invent reactor physics, build the full walkable balance-of-plant facilities, or imply that unmodeled equipment has independent sensors. Preserve the existing read-only wall and control-room interactions. Later phases can address full turbine/cooling spaces, cameras and the operator character.
