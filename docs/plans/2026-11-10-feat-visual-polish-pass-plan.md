---
title: Visual Polish Pass - Screens, Labels, Checklist, Menus, Control-Room Lighting - Plan
type: feat
date: 2026-11-10
artifact_contract: ce-unified-plan/v1
product_contract_source: ce-plan-bootstrap
execution: code
---

# Visual Polish Pass - Plan

## Goal Capsule

- **Target repo:** this repository (public Reactor Sim). No machine names, user names or home paths appear in files: the Windows build machine is `$REACTOR_SIM_WIN_HOST`, its repo checkout `$REACTOR_SIM_WIN_REPO`.
- **Objective:** Owner request, verbatim: "up to date on new, visuals, any screens in game, placement and labels, to do, menus and lists, stylize and clean up. lighting as well". Every in-game screen, label, list and menu reads as one industrial control-room style, and the control room is lit sensibly.
- **Means:** Pure-logic checks first (theme, label naming, page layout, HUD models) so polish is held by tests, then the engine-side changes they drive, then before/after captures judged by eye.
- **Authority:** PR1 is merged and owns the type ramp and tokens (`Theme.cs`, `ThemeColors.cs`, `ReadoutFormat.cs`, `RuntimeUi.cs`). This plan builds on them and adds no new palette. Control-room lighting only.
- **Out of scope:** Hall lighting and hall geometry (PR2, branch `feat/playtest2-smr-pool-hall`, owns the hall). Follow-up note in U8. Do not edit `CaptureMath.cs`, `CaptureMode.cs`, `OperatorController.cs`, `PlantEquipmentBinder.cs`, the hall builder or glow code; if unavoidable, keep the edit minimal and list it in the PR for rebase.
- **Execution profile:**
  - Pure logic is UnityEngine-free, linked into `ReactorSim.Core.Tests`, and runs under `DOTNET_ROLL_FORWARD=LatestMajor bash scripts/ci.sh` on any Linux box.
  - Scene regeneration, capture runs and the Unity checks run only on the separate Windows checkout (`ssh $REACTOR_SIM_WIN_HOST`). If that machine is offline, Unity checks and visuals are reported as owner-run.
  - No `ReactorSim.Core` change is planned. If one happens: `bash scripts/sync-core-to-unity.sh` and commit the Plugins files.
  - Clean room: original work only; `bash scripts/clean_room_check.sh` must pass.
- **Stop conditions:** Stop and report if graphics-enabled capture cannot run (visuals become owner-run); if a change needs a file on the do-not-edit list beyond a minimal edit.

---

## Product Contract

### Summary

PR1 made the type system and made the menus uGUI. What remains uneven: some labels collide in name, the mission HUD still shows debug text in IMGUI, the legacy desk overlay is still compiled into every build, the checklist does not say what comes next, screens lack automated layout guards, and the room has no deliberate lighting beyond a flat per-fixture intensity.

### Requirements

- **R1 Screens.** Every CRT page and tablet page follows one grid: header, title and clock, status glyph plus word, key readings, supporting rows. Rows fit the column budget, readings carry units from `ReadoutFormat`, and no page shows raw or debug text. Legible at 1920x1080 from the seat and in ScreenFocus.
- **R2 Labels.** Control and indicator labels are unique across the whole inventory, sentence-case, free of type notes, short labels are uppercase and at most 16 characters, labels group by panel/system. No plate overlaps or floats (existing layout checks stay green).
- **R3 Checklist.** The startup checklist shows the current step, done and blocked states, and the next step, compactly.
- **R4 Menus and lists.** Main menu, pause menu, settings, alarm and mission lists use the shared `RuntimeUi` kit and Theme tokens only. No IMGUI in player builds except the development input probe; the legacy `ControlDeskUI` overlay is development-only.
- **R5 Style.** Dead UI and debug strings removed; one consistent look.
- **R6 Lighting.** Control-room key/fill balance, panel glare controlled, screens and lamps emissive but not blown out, deliberate ambient and exposure. Hall layer untouched.

### Scope Boundaries

- No gameplay, physics or Core changes. No new fonts. No hall work.
- Blender label-plate regeneration happens only if the captures show a plate fault that data changes cannot fix.

---

## Key Technical Decisions

- **KTD1 Guard polish with tests.** Each requirement that can be stated as arithmetic or string rules gets a pure test, so the polish cannot silently regress. Tests live in `ReactorSim.Core.Tests`.
- **KTD2 Labels are data.** Label fixes are edits to `design/control-room/inventory.csv`, followed by `python3 scripts/inventory-rollup.py` to regenerate `rollup.md` and `inventory-runtime.json`.
- **KTD3 One HUD model per surface.** HUD content is built by a pure model (as `ClipboardView.Hud` already does) and drawn by a thin uGUI view.
- **KTD4 Lighting constants are data in a pure class** (`RoomLighting`), asserted by tests (ratios, ranges), and applied by `ControlRoomSceneBuilder`. Screens stay emissive-dark so contrast holds in any light.
- **KTD5 Evidence loop.** Before captures are taken from master; after captures are read by eye and the plan is iterated until nothing looks muddy, blown out, overlapping or cramped.

---

## Implementation Units

### U1. Label naming hygiene
- **Goal:** R2.
- **Files:** `design/control-room/inventory.csv`, `design/control-room/rollup.md`, `ReactorSim.Unity/Assets/StreamingAssets/inventory-runtime.json` (regenerated), test `ReactorSim.Core.Tests/InventoryLabelTests.cs`.
- **Approach:** Rename the duplicated labels so each row is unique ("Heater power" demand vs output; "Cooling water flow" demand vs measured), keep control labels as demand/target and indicators as measured.
- **Test scenarios:** labels globally unique (case-insensitive); labels sentence-case, no trailing period or double space; short labels uppercase, at most 16 characters, non-empty; short labels unique within a panel; label length within plate budget.
- **Verification:** `dotnet test ReactorSim.Core.Tests --filter InventoryLabelTests`; rollup regenerates without diff noise.

### U2. CRT page layout guard
- **Goal:** R1 (CRT).
- **Files:** test `ReactorSim.Core.Tests/CrtPageLayoutTests.cs`; fixes in `ReactorSim.Unity/Assets/Scripts/View3D/CrtPages.cs` if the tests expose faults.
- **Test scenarios:** for every known page and for a cold, a running and a scrammed snapshot: first three rows are header, title+clock, status; every row at most `Columns` cells; `RowCost` at most `RowBudget`; status row always carries a glyph and a word; no row contains `NaN`, `Infinity`, `null`, `TODO`, `debug`, `U8`; every numeric key row has a unit token; titles are uppercase.
- **Verification:** tests pass; capture of each CRT page reviewed.

### U3. Tablet pages layout guard
- **Goal:** R1 (tablet), R4 (alarm list).
- **Files:** test additions in `ReactorSim.Core.Tests/TabletPagesTests.cs`; fixes in `ReactorSim.Unity/Assets/Scripts/UI/TabletPages.cs` and the tablet view if needed.
- **Test scenarios:** pages have a stable title, no raw/debug text, alarm rows start with a glyph and word, ordered by severity then name; empty states have a sentence.
- **Verification:** tests plus tablet capture.

### U4. Checklist: current, done, blocked, next
- **Goal:** R3.
- **Files:** `UI/ClipboardView.cs` (model: next step text, blocked marker), `UI/StartupChecklistHud.cs` (compact view), tests in `ClipboardViewTests.cs`.
- **Test scenarios:** compact model names current step, shows the following step as "Next: ...", reports a blocked reason when a prerequisite regressed, completes with an "all steps ticked" state, never exceeds the compact line budget.
- **Verification:** tests plus checklist capture (compact and expanded).

### U5. Mission HUD to the shared kit
- **Goal:** R4, R5.
- **Files:** `Missions/MissionHud.cs` (replace IMGUI with a uGUI panel on `RuntimeUi`), new pure model `Missions/MissionHudModel.cs` linked into the test project, test `MissionHudModelTests.cs`.
- **Test scenarios:** no "(U8)" or tier debug label; title, step N of M, completed state; advisory line trimmed to the width budget; hidden when no mission.
- **Verification:** tests; mission-mode capture.

### U6. Legacy desk overlay is development-only
- **Goal:** R4, R5.
- **Files:** `UI/ControlDeskUI.cs`, `PlantBridge.cs` wiring.
- **Approach:** Compile the overlay only in the editor and development builds; release builds never create it. Keep `InputProbe` as is.
- **Test scenarios:** theme literal scan still passes (`ThemeLiteralScanTests`); build and play-mode round trip unaffected (Unity check).
- **Verification:** `bash scripts/ci.sh`; Unity checks.

### U7. Control-room lighting
- **Goal:** R6.
- **Files:** new pure `View3D/RoomLighting.cs` (linked into tests), `Editor/ControlRoomSceneBuilder.cs` (apply: fixture intensity and range, ambient colour and intensity, specular glare, screen and lamp emission levels), test `RoomLightingTests.cs`.
- **Approach:** Warm-neutral key from ceiling fixtures, cool low fill via ambient, screens dark-emissive, lamps brighter than panel surfaces by a fixed ratio. Hall layer mask left as is.
- **Test scenarios:** fixture intensity and range within bounds; ambient lower than key; lamp emission above screen emission above panel; hall culling bit untouched.
- **Verification:** tests; Unity `LayoutCheck`; room render before and after reviewed by eye, iterate.

### U8. Docs and hall follow-up note
- **Goal:** record decisions; leave the hall note.
- **Files:** `docs/design/main-control-room.md` (label and lighting rules), `docs/PLAYTEST.md` (checklist wording if changed), `INDEX.md` entries if new files warrant.
- **Verification:** `bash scripts/clean_room_check.sh`. Follow-up note: hall lighting stays with PR2; revisit after it merges.

### U9. Visual evidence
- **Goal:** prove R1 to R6 visually.
- **Approach:** see the contract below. No code files.

---

## Visual Verification Contract

- **Build and checkout:** a dedicated Windows checkout, never the owner's or another agent's.
- **Captures:** `scripts/capture-playtest.sh --sizes 1920x1080` (poses: seat, stand, crt, tooltip, rejected, drag, checklist, pause, tablet, clipboard, menu) on master (before) and on the branch (after); `RoomRender.Run` for room views (seat, stand, top). Grade with `bash scripts/check-captures.sh <dir>` (glyph height at least 10 px, contrast at least 4.5, PNG present).
- **Per-page captures:** each CRT page and each tablet page is shown; if the stock poses cover only some, the capture run is extended through the existing poses option, not by editing `CaptureMode.cs`.
- **Delivery:** `before-*.png` and `after-*.png` copied to `~/Downloads/claude/reactor-renders/pr3/`, all read by eye; anything muddy, blown out, overlapping, or cramped is fixed and recaptured.
- **Fallback:** if rendering fails over ssh, retry through the interactive-session scheduled task the script uses; if impossible, visuals are owner-run and reported as such.

## Gate

`DOTNET_ROLL_FORWARD=LatestMajor bash scripts/ci.sh` exit 0, `bash scripts/clean_room_check.sh` exit 0, then `UNITY_EXE=... bash scripts/ci.sh` on the Windows checkout.

## Risks

- PR2 overlaps files; mitigation is the do-not-edit list and a rebase note in the PR.
- Lighting numbers cannot be judged without a render; the evidence loop (KTD5) is the control.
- Blender plate geometry cannot change without re-export; only data changes are planned.
