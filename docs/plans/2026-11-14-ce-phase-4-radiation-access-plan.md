---
title: "Phase 4: Radiation and Controlled Access"
type: feat
date: 2026-11-14
artifact_contract: ce-unified-plan/v1
execution: code
status: planned
---

# Phase 4: Radiation and Controlled Access

## Goal and baseline

Field operators can survey a controlled area, understand exposure and plan their time. The current simulation has no complete player radiation/dose or contamination system; ambient blue glow and detector ticks are atmosphere, not measurements. This is a game model with explicit approximations, not an operational radiation-protection procedure.

## Dependencies and decisions

Requires phase 2's navigable access route and equipment IDs; co-op acceptance requires phase 3's authoritative shared player state. Keep the operating containment inaccessible. Choose player consequences, tuning, units, shielding fidelity and optional effects through design and playtesting; do not present unmodeled data as safe.

## Implementation units

1. **Model and source register.** Specify game-scale sources, enabled conditions, area geometry, dose rate, accumulated dose, time integration, distance and shielding. Separate physical reference values from explicit gameplay tuning; cite authoritative sources for claims and centralize physics constants in `Physics.cs`. Invalid or absent source state yields unavailable, not zero exposure.
2. **Deterministic state.** Define versioned, host-authoritative area/source and player exposure state under the project's serialization boundary. Integrate with simulation time, pause/warp, late join and save/load; old saves require an explicit migration/default policy. Guard finite values and bound pathological inputs.
3. **Operator feedback.** Add local area monitors, portable survey meter and personal dosimeter with units, alarm thresholds, access indicators and historical exposure. Tie audio rates to the modeled instrument only after one exists. Add clear status for unavailable sensors and controls for audio/accessibility.
4. **Controlled access and task.** Implement one signed service area outside containment with a modeled source and a short task; show time, access restrictions, dose feedback and safe exit. Protective gear has explicit, tested gameplay effects rather than a blanket immunity claim.
5. **Contamination extension.** Separately design contamination state, affected surfaces/items, spread boundaries, survey and cleanup; implement only after the dose vertical slice. Model remaining unknowns explicitly and avoid treating contamination and dose as the same quantity.

## Acceptance and verification

- One player surveys a state-changing controlled area, sees matching fixed and portable readings, enters for a task, exits and retains accumulated dose across save/reload. Disabled, unavailable and invalid sources are distinguishable.
- Dose responds monotonically to modeled time/source and any defined shielding; pause/warp, zero distance and extreme values remain finite and deterministic. New-save and old-save behavior is covered.
- In co-op, host and two clients agree on source/area state and each player's separate exposure through late join and reconnect. Test denial and access feedback. Run Core/Unity/serialization tests and document game approximations and listening/visual playtests.

## Handoff

Expose a stable area access/exposure interface to maintenance and campus plans. Do not market the system as a real radiation instrument or training procedure.
