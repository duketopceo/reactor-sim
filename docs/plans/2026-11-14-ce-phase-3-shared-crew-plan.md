---
title: "Phase 3: Shared Plant and Visible Crew"
type: feat
date: 2026-11-14
artifact_contract: ce-unified-plan/v1
execution: code
status: planned
---

# Phase 3: Shared Plant and Visible Crew

## Goal and baseline

Two operators on separate machines can see one another, operate the same plant and recover from disconnects. `CoopSessionManager` currently exchanges snapshots and intents over TCP; the Fusion path is a stub. Existing transport code is a foundation, not evidence that avatars, doors, late join or a production internet service work.

## Dependencies and decisions

Requires phase 1's coherent plant and phase 2's player/room IDs. Deliver a trusted-LAN two-player slice first. Internet transport, authentication provider, host migration, final roles and crew size are separate decisions; no internet release claim until explicitly implemented and verified. Solo play remains first-class.

## Implementation units

1. **Authoritative session contract.** Define versioned join handshake, host identity, protocol/schema version, intent ownership and validation, timeouts, message size/rate limits and error states. Reconcile existing plant-only snapshots with persistent world/player state according to `AGENTS.md`; specify migration and deterministic restoration. Document the trust boundary for LAN.
2. **Two-client lifecycle.** Implement discoverable/direct join UI, clear connection status, disconnect/reconnect and full late-join state. Host remains the sole simulation authority; reject unsupported versions and unauthorized intents and recover gracefully from a lost host. Add tests for packet delay, duplicate or malformed messages, interrupted saves and rejoin.
3. **Visible operators and world.** Synchronize player identity, transform, pose and interaction state with interpolation; replicate phase 2 doors, use feedback and required equipment states. Resolve simultaneous control use deterministically and surface accepted/rejected actions. Do not conflate cosmetic interpolation with authoritative plant state.
4. **Crew workflow.** Provide optional reactor, balance-of-plant and field roles with host-configurable permissions, shared alarm acknowledgement and procedure state. Preserve shared emergency actions; default solo operation remains unrestricted. Define role reassignment on disconnect and a text/status fallback for communication.
5. **Internet decision.** Compare supported transport/relay options, hosting cost, NAT behavior, identity, abuse/privacy controls and maintenance; choose only after the LAN gate. Prototype separately and do not imply the existing stub is operational.

## Acceptance and verification

- Two real machines join, see both players and door/control use, complete a cold startup and coordinate a trip/recovery with matching wall/desk state and alarm acknowledgement.
- A late join and reconnect restore plant, roles, door positions and location; conflicts and malformed input have deterministic visible outcomes. Save/load round trips preserve authoritative state.
- Run automated host/client, latency/disconnect and serialization tests plus recorded two-machine playtests and baseline CI. Confirm solo behavior, accessibility and measured bandwidth/performance.

## Handoff

Publish protocol/versioning, state ownership and known limits for radiation and maintenance. Expanding to more than two players requires a separate load test and product decision.
