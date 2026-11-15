---
title: "Phase 7: Operations, Progression and Assistant"
type: feat
date: 2026-11-14
artifact_contract: ce-unified-plan/v1
execution: code
status: planned
---

# Phase 7: Operations, Progression and Assistant

## Goal and baseline

Give a solo operator or crew a meaningful multi-shift loop: generate power, respond to a fault, perform field work, recover and understand the outcome. Existing startup procedures, missions, dispatcher/messages and assistant provide a foundation but do not constitute the finished loop or economy.

## Dependencies and boundaries

Requires phase 5's authoritative work-order loop; campus-wide tasks additionally require phase 6 areas. Use phase 3 for co-op permissions and state. Progression, economy balance, assistant autonomy and worker embodiment remain product decisions; preserve an enjoyable solo sandbox with no mandatory grind.

## Implementation units

1. **Shift and scenarios.** Define objective templates for startup, load following, shutdown, fault diagnosis and recovery, with authoritative triggers and outcomes. Connect existing procedures, dispatch and scenario runner; show a clear current objective and a history of completed/failed tasks without duplicating plant truth.
2. **Resources and progression.** Specify optional fuel, consumables, spare parts, maintenance costs and demand rewards as transparent game economy data. Add progression only after a playable un-gated loop; validate bounds, failure recovery, save migrations and multiplayer ownership before enabling rewards.
3. **Assistant behavior.** Provide explainable recommendations with evidence, step-by-step startup and an explicit player-authorized action mode. Reuse existing plant intents and accepted control values; show current action, reject reasons, cancellation and manual override. Assistant must not bypass locks, roles or emergency rules.
4. **Persistence and UI.** Add or extend save slots, shift summaries, incident/replay history, accessible onboarding, settings and scenario selection. Version authoritative and player-specific state and preserve prior saves; distinguish partial objectives from completed work.
5. **Field/campus link.** Connect work orders and dispatch to released equipment IDs and areas only. A visible worker, remote tasking or automated maintenance requires separate navigation, permissions and collision design; do not represent a remote task as complete until authoritative state confirms it.

## Acceptance and verification

- A single player and a two-person crew can run a shift, meet or miss demand, diagnose a fault, repair it and recover; outcomes and resource changes are understandable and reproducible.
- Save/reload, reconnect and role change preserve objectives, resources and assistant permission/action state without duplicate payouts or silent commands. Manual override cancels or supersedes an assistant action predictably.
- Exercise assistant safety, objective state transitions, economic bounds, input denial and migration with automated tests and scenario runs; record a human onboarding and multi-shift playtest.

## Handoff

Document optional progression defaults, accessibility and assistant permission UX for release; no claim of a fully autonomous worker until its actual behavior is tested.
