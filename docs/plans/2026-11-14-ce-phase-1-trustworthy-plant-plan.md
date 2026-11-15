---
title: "Phase 1: Trustworthy Plant, Controls and Audio"
type: feat
date: 2026-11-14
artifact_contract: ce-unified-plan/v1
execution: code
status: in-progress
---

# Phase 1: Trustworthy Plant, Controls and Audio

## Goal and baseline

An operator can start, run, trip and recover the existing plant while the desk, status wall, reactor hall and audio tell the same story. The separate wall and underwater gallery exist; some component detail is prepared but not displayed, relief anchors are absent from the placed hall, and the audio has not passed a listening review. The three documented XFAILs remain open. This phase completes and extends [the live-counterparts plan](2026-11-14-feat-live-plant-counterparts-plan.md), rather than replacing its work.

## Scope and dependencies

Start on the current branch; no future building or networking milestone is required. Cover existing controls, reactor view, alarms, wall, sound and the three verification defects. Full walkable field equipment, new failure models and radiation are later phases. Preserve existing scene work, control IDs and snapshot compatibility.

## Implementation units

1. **State and presentation audit.** Complete `docs/design/plant-counterparts-inventory.md` for each shown node, path, indicator and audio layer: authoritative field, unit, actual versus command, available states and missing mapping. Use `PlantBridge.GetSnapshot()`, existing predicates and desk alarm context. Mark unavailable readings explicitly; never label a demanded rate as delivered flow or thermal power as measured steam mass flow.
2. **Wall and controls.** Connect grouped `PlantWallMapModel.ComponentReadout` data to a readable, non-obstructive detail view; preserve the separate door-side map and mounted summary. Add directional cues only where the model supports direction and activity. Verify buttons, detents, rejection feedback, labels and checklist from standing and seated views.
3. **Hall, effects and audio.** Add and bind distinct primary and secondary relief anchors to actual separate signals; confirm pump/rod motion and visible state. Audit `ProceduralPlantAudio`, `ReactorSoundscape` and `VoiceAnnouncer`; add master/category controls, source positioning, priority/acknowledgement handling and no false steam on trip. Use original or licensed assets and record an actual listening pass.
4. **Simulation defects.** Reproduce `DOM-01`, `SCR-02`, `EDGE-08` from `docs/testing/simulation-verification.md`; fix each root cause with targeted regression scenarios. Add/adjust domain-error and latch semantics as appropriate, and only promote an XFAIL after its own check passes. Any numerical design choice must be documented, with Core re-synced to Unity and snapshot/save tests retained.
5. **Playability and polish.** Verify cold startup, steady load, flow loss, high pressure, SCRAM, turbine trip, feed starvation and bypass against snapshot, desk, wall, hall and audio. Fix water, lighting, texture and readability issues; maintain the established performance and light budgets.

## Acceptance and verification

- Each visible state has an audited source and missing data stays unavailable. The summary is readable at a practical standing position; details and units are legible close up.
- Primary/secondary relief do not trigger one another; a tripped turbine has no turbine-steam cue while an independently modeled bypass can remain active. Acknowledgement and alarm behavior agree across views.
- Cold startup to grid, steady operation, SCRAM and turbine-trip recovery work in an actual build. Record screenshots and a human audio review, including quiet and simultaneous-alarm conditions.
- Run `scripts/ci.sh`, relevant Unity layout/hall/binding checks and save/load/co-op regressions. Document each remaining XFAIL honestly; this phase's complete gate requires all three named defects to be fixed and their checks promoted.

## Risks and rollout

Avoid changing plant physics solely for visual effect. Stage defect fixes separately from presentation changes, and preserve existing player saves. Update `docs/PLAYTEST.md`, the binding inventory and roadmap status with evidence; phase 2 begins only when the phase gate is met.
