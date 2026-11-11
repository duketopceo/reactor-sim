---
title: Reactor Hall Lighting and Window Visibility (PR4) - Plan
type: feat
date: 2026-11-10
artifact_contract: ce-unified-plan/v1
product_contract_source: ce-plan-bootstrap
execution: code
---

# Reactor Hall Lighting and Window Visibility (PR4) - Plan

> **Superseded in part by [the PR6 plan](2026-11-10-feat-window-framing-readme-plan.md).** The window and pool geometry below (pool radius, pool axis distance, gallery eye height, coaming, containment height, crane bridge position) was retuned in PR6; the current numbers live in `HallDimensions.cs`. The diagnosis and the lighting work stay as written.

## Goal Capsule

- **Target repo:** this repository (public Reactor Sim). No machine names or home paths in the plan: the Windows build machine is `$REACTOR_SIM_WIN_HOST`.
- **Objective:** From the standing and the seated viewpoint in the control room, the reactor module and the pool read through the observation window without walking to the rail, with material and lighting contrast instead of one flat blue-grey, an unobstructed module, and a glow cue that does not depend on how many pixel lights the renderer allows.
- **Authority:** `docs/plans/2026-11-09-2010-feat-playtest2-controls-readability-hall-plan.md` (PR2) stays authoritative for the hall's scale (true-scale integral SMR in a deep pool), KTD9 (frame budget) and R18-R22. This plan only retunes what the window frames, the lighting and the materials. It uses the PR2 plan's named fallback (reopen window geometry) because tuning light alone cannot fix a sight-line problem.
- **Scope:** hall and window only. Not touched: control-room UI, menus, HUD, CRT and tablet pages, control-room lighting (owned by the PR3 branch `feat/visual-polish-pass`). Shared files kept minimal: `ReactorSim.Core.Tests/ReactorSim.Core.Tests.csproj` gets two `Compile Include` lines; `RoomRender.cs` gets a size env var and a hall gate fix.
- **Stop conditions:** stop and report if the window change breaks `LayoutCheck`; if the hall exceeds KTD9 relative to the PR2 hall on a quiet pair of runs; if the module still cannot be seen from the standing pose on a real capture after the geometry fix.

## Diagnosis (evidence before design)

1. **The sight line cannot reach the pool.** Anchors (from the room render log): stand eye (1.65 m high) is 4.5 m from the window wall; the seat is 6.1 m back (seated eye 1.2 m; the "window-seat" render uses standing eye 1.65 m). The PR2 window has a 0.35 m sill and the gallery eye is 9 m above the water with the pool axis 11 m from the wall. Largest downward angle through the opening: stand atan(1.30/4.5) = 16 deg, seat atan(1.30/6.1) = 12 deg. Angle to the pool centre's surface: atan(9/15.5) = 30 deg from the stand. The standing view therefore shows only the far wall, the ceiling and the crane.
2. **The render harness hides the hall.** `HallVisibility` tests the window box against `Camera.main` (the operator camera, facing elsewhere). `RoomRender` renders with its own temporary camera, so the frustum gate has switched the hall root off: the window renders the clear colour (the "black void"). Real play is not affected by this part. The player capture poses (`gallery`, `hall`, `glow*`) are the truth for play.
3. **Flat colour.** Every hall material is a blue-grey (0.26-0.72 grey blues), lights are cool white, water layers share one tint, so nothing separates.
4. **Crane.** Bridge at z = 2.2 m over the pool axis; its girders and the lifting block (hanging to 3.5 m over the deck) lie across the view of the module.
5. **Glow lights.** 3 hall lights + 3 rack lights = 6 real lights on the hall layer; Unity forward rendering lights each object with 4 pixel lights, so the rack cue changes with light sorting.

## Key Technical Decisions

- **KTD1. Lower the gallery, keep the pool.** `GalleryEyeAboveWater` 9.0 -> 2.2 m (first try 3.2 m: renders showed the module head still cut by the sill) and `PoolAxisFromWall` 11.0 -> 10.0 m. The 22 m depth, the true-scale module and the pool radius are unchanged. Required downward angle to the module centre becomes atan(3.05/14.5) = 11.9 deg to the module head from the stand and atan(3.05/16.1) = 10.7 deg from the seat, inside the new opening's reach (below).
- **KTD2. Taller, lower window.** Sill 0.35 -> 0.05 m, height 1.9 -> 2.3 m (clamped so the header keeps 0.3 m of wall). Reach: stand 19.5 deg, seat 14.7 deg. A pure helper (`HallSightline`) states and tests the claim, and a capture proves it.
- **KTD3. Pure maths lives in `Scripts/View3D`, linked into the test project.** `HallSightline` (angles, opening reach, segment-vs-box clearance) and `HallExposure` (linear luminance, exposure ceiling, contrast ratio). The Editor builder and `HallCheck` read the same constants.
- **KTD4. Glow cue is emissive-first.** Cells use an HDR emission scaled by `GlowMapping.RackEmission`; haze cones carry opacity from `GlowMapping.Haze`; ONE real rack light (`RackGlow_Light_0`) is scaled by `GlowMapping.LightScale`. Hall real lights become 2 warm high-bays + 1 rack light = 3, under `MaxRealtimeLights = 4` (the default pixel-light count). `GlowMapping` constants and behaviour are unchanged.
- **KTD5. Park the crane.** Bridge moves behind the pool (z beyond the far pool edge), trolley parked at one end, block raised under the girders. `HallSightline.SegmentHitsBox` proves no eye-to-module sight line crosses the girder box.
- **KTD6. Warm high-bay versus blue pool.** Warm point lights (colour ~ (1.0, 0.82, 0.58)) over the deck, blue only from water and rack glow. Concrete warm grey with a small emissive floor so far walls never go black, painted crane in safety yellow, brushed-stainless vessel (metallic 0.55, smoothness 0.6, not mirror), off-white containment paint, teal shallow water over deep-blue deeper water (two volume materials, one extra draw).
- **KTD7. Budget.** KTD9 from PR2 is unchanged and not reopened: gate stays; added draw calls +1; real lights go down.

## Implementation Units

### U1. Pure sight-line and exposure maths
- **Files:** create `ReactorSim.Unity/Assets/Scripts/View3D/HallSightline.cs`, `HallExposure.cs`; link in `ReactorSim.Core.Tests/ReactorSim.Core.Tests.csproj`; tests `ReactorSim.Core.Tests/HallSightlineTests.cs`, `HallExposureTests.cs`; extend `HallDimensionsTests.cs`.
- **Test scenarios:** opening reach from stand and seat covers the angle to the module centre with at least 1 deg margin under the new constants and FAILS for the PR2 constants (a regression guard written as a pure check on literals); segment from eye to module centre through the window clears the parked crane box and hits a box placed over the pool axis (negative control); luminance of a lit albedo stays under the blown-highlight ceiling at the brightest light level and above the black floor at the far wall; water/steel/concrete luminance contrast ratios at least 1.5 between neighbours; Validate() rejects a window whose sill is above the stand eye.

### U2. Geometry, window and render harness
- **Files:** `HallDimensions.cs` (eye height, axis distance, anchor distances, window constants, `MaxRealtimeLights` 4, validation), `ControlRoomSceneBuilder.cs` (`BuildReactorWindow` only: sill, height, glass alpha), `RoomRender.cs` (force the hall root on for its own camera), `PolarCrane.cs` call site rail height (relative to the deck, not the room floor), `HallBuilder.cs`.
- **Test scenarios:** `HallCheck` reads the built window glass/frame and asserts reach; `LayoutCheck` unchanged and passing; rebuild twice gives identical counts.

### U3. Lighting and materials
- **Files:** `HallBuilder.cs` (materials, lights, two-layer water), `SmrModuleMesh.cs` only if material slots need it, `PolarCrane.cs` (park, paint material), `CableTray.cs` (height), `HallCheck.cs`.
- **Test scenarios:** `HallCheck`: real lights <= 4; every hall renderer shares no more than the expected material count; crane bridge bounds clear of the stand/seat sight lines; exposure helper bounds on the chosen colours and intensities.

### U4. Robust glow
- **Files:** `FuelRackMesh.cs` (one shared light, HDR emission), `HallBuilder.cs`, `PlantEquipmentBinder.cs` (already loops all lights; keep), `HallCheck.cs` (rack light count rule), `GlowMappingTests.cs` unchanged.
- **Test scenarios:** `check-captures.sh --only glowoff,glowidle,glowmid,glowfull` passes (rack crop mean rises with power) with the new lighting.

### U5. Docs and visual verification
- **Files:** `docs/PLAYTEST.md` (window, gallery distances, capture commands, ROOM_RENDER_SIZE), `docs/TESTING.md` if it lists the render size.
- **Visual contract (`ROOM_RENDER=1`, `ROOM_RENDER_SIZE=1920x1080`, plus player poses):** before (master) and after renders of window-stand, window-seat, gallery (player pose), hall (rail), glowoff and glowfull at 1920x1080. Accept when, looking at the standing view: the water surface, the module head/pump shoulders and the rack glow are identifiable; no girder crosses the module; surfaces are distinguishable by hue and value (no single flat blue-grey); no region is clipped white; glow full is plainly brighter than glow off.

## Verification Contract

| Check | Command | Where |
|---|---|---|
| Full gate | `DOTNET_ROLL_FORWARD=LatestMajor bash scripts/ci.sh` (exit read directly) | any Linux box |
| Pure tests | `dotnet test ReactorSim.Core.Tests -c Release` | any Linux box |
| Clean room | `bash scripts/clean_room_check.sh` | any Linux box |
| Unity checks | `UNITY_EXE=... bash scripts/ci.sh` (HallCheck, LayoutCheck) | Windows build machine, own checkout |
| Renders | `ROOM_RENDER=1 ROOM_RENDER_SIZE=1920x1080` and `scripts/capture-playtest.sh --sizes 1920x1080 --poses ...` | Windows build machine, interactive session |
| Budget | `scripts/check-captures.sh <dir> --baseline <PR2 hall dir> --budget-only` quiet pair, noise reported | Windows build machine |

## Risks

- Seated reach is thin (10.7 deg needed, 14.7 deg available); if captures show the module cut by the sill the fallback is a lower `GalleryEyeAboveWater` (1.8 m) with the same pure check.
- Shallow viewing angle means the deep body of the module is hidden by water; the head, pump shoulders and steam stubs must carry the read. The owner chose pool depth; it is not reopened here.
- `ReactorSim.Core.Tests.csproj` may conflict textually with PR3; resolve by keeping both sets of lines.
