---
title: "Phase 5: Field Operations and Maintenance"
type: feat
date: 2026-11-14
artifact_contract: ce-unified-plan/v1
execution: code
status: planned
---

# Phase 5: Field Operations and Maintenance

## Goal and baseline

An alarm leads to a visible equipment fault, diagnosis, isolation, repair and verified return to service. Current plant trips and alarms are modeled, but they are not a complete physical maintenance loop. Repairs and isolation interactions are fictional game abstractions, not real-world operating instructions.

## Dependencies and boundaries

Requires the phase 2 connected route and phase 4 controlled-access vertical slice; shared work requires phase 3 session authority and interaction conflict handling. Start with one pump; valves, electrical faults, fire and flooding are separate slices. Never change equipment state solely as a cosmetic effect.

## Implementation units

1. **Equipment and faults.** Create stable equipment IDs tied to the map, wall, instruments and physical objects. Define persistent condition, fault cause, severity, detection, alarm, response and recovery. Start with one scripted pump fault that changes a real modeled input/output; keep unknown equipment status unavailable.
2. **Work order and state machine.** Model request → inspect → isolate → repair → test → return as explicit authoritative states, prerequisites and failure/rejection reasons. Provide a fictional tag-out affordance and prevent invalid or conflicting actions; emergency operations retain appropriate shared access. Persist pending tasks, history and parts use.
3. **Field interaction.** Add the relevant pump location, panel, accessible control, diagnostic reading, tool and replacement component. Use existing interaction and accessibility patterns; provide clear feedback for out-of-range, unavailable or rejected work. Keep a solo path and optional desk/field split.
4. **Additional failures.** Extend to a valve and an electrical component only after the pump gate. Design fire, leaks and flooding individually with bounded causes, observable symptoms, safety effects, response and recovery; connect radiation effects only when the phase 4 source model supports them.
5. **Co-op and saving.** Validate host-owned interactions, concurrent repair claims, actor disconnect, late join, inventory transfer and safe rejoin. Version and migrate saves; resumed faults and ongoing tasks must not duplicate or vanish.

## Acceptance and verification

- In a reproducible fault scenario, desk alarm, wall, field equipment and sound agree. One player can diagnose and complete the pump loop; two can split desk/field duties and reach the same result.
- Incorrect prerequisites produce actionable feedback, no false repair progress and no duplicate item consumption. An interrupted task, late join and save/reload retain the correct state.
- Run deterministic state-machine, fault, inventory, save migration and co-op conflict tests, scenario suite, Unity interaction checks and a recorded human playtest. Existing plant trips and controls remain intact.

## Handoff

Publish equipment IDs, task state and fault hooks for the campus and progression phases. Add more equipment only with its own source, observable symptoms and complete recovery path.
