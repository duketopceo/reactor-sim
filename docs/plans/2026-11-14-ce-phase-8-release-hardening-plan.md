---
title: "Phase 8: Release Hardening and Expansion"
type: feat
date: 2026-11-14
artifact_contract: ce-unified-plan/v1
execution: code
status: planned
---

# Phase 8: Release Hardening and Expansion

## Goal and baseline

Turn the agreed playable scope into a reproducible, supportable release candidate. Previous phases define functional slices, but a plan or a passing isolated test is not release evidence. Additional reactor types, larger crews and modding remain optional future proposals.

## Dependencies and release scope

Requires an explicit accepted scope from phases 1–7 and a selected multiplayer deployment model. Freeze supported platforms, save versions, performance targets, accessibility/localization commitments and feature claims before release-candidate testing. Unsupported features must not appear as working in menus or documentation.

## Implementation units

1. **Test matrix and defect triage.** Define representative hardware, supported operating systems, graphics/audio devices and solo/co-op scenarios; include startup, trips, radiation, maintenance, cross-building navigation and long sessions. Track severity, owner, reproduction, fix and retest for every release blocker.
2. **Networking and persistence.** Validate fresh install, version mismatch, host/client lifecycle, network interruption, authority/permissions, late join, save migration, restore and recovery. If internet play is in scope, verify relay/authentication/privacy, abuse limits, costs and service outages; otherwise label release LAN-only.
3. **Experience and content.** Run documented visual/readability and actual listening passes in quiet, normal and emergency states; balance volume categories, alarm priority and spatial audio. Complete controller/input/accessibility, localization and onboarding reviews. Confirm asset provenance and licenses.
4. **Performance and packaging.** Profile player builds across the campus and multiplayer, compare against published thresholds, and fix regressions in load time, frame pacing, memory and bandwidth. Test packaging, upgrades, uninstall, failure reporting and safe diagnostic collection; avoid collecting private data by default.
5. **Release candidate and expansion review.** Build from a reproducible commit with deterministic scene/asset generation and a rollback path. Verify release notes, supported features and documentation against actual builds. Record later reactor, player-count, fuel and modding ideas separately with their own design and acceptance gates.

## Acceptance and verification

- `scripts/ci.sh`, Unity checks, scenario suite, migration and networking tests pass from the release commit; remaining known defects have explicit owner, severity and release decision. Do not silently waive expected failures.
- Human sign-off records startup-to-recovery, field/campus, radiation, audio, accessibility and co-op sessions on representative hardware. Claims match tested behavior and all included content has documented rights.
- Clean install, update, save restore, offline/failed-service behavior and crash recovery succeed. Release artifacts can be reproduced and rolled back.

## Handoff

Publish the supported-scope matrix, tested build identifier, open issues and support instructions. New expansion work begins only after the release gate or an explicit scope change.
