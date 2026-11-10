---
title: "feat: reactor head framing in the window and README media refresh (PR6)"
type: feat
status: active
date: 2026-11-10
---

# Reactor head framing and README media (PR6)

Follows PR4 (hall lighting and window) and PR5 (final visual pass). This branch contains both until they merge.

## Problem

PR4 renders show the module head low in the window from the stand and the seat. The window sill is not the cause (0.05 m, the reach checks pass). The near pool rim hides it: the sight line from the eye to the head passes about 0.3 m above the rim top, so only a few tenths of a metre of the module show over the rim (0.44 m from the stand, 0.19 m from the seat with the PR4 numbers).

## Requirements

- R1 A pure check, `HallSightline.RimVisibleHeight`, gives the module height shown over the near rim from the stand and the seat. `HallDimensions.Validate()` requires at least the dome height plus a margin (1.5 m) from both views.
- R2 Geometry change inside plausible SMR bounds, checked from the REAL seated eye (1.2 m, `HallDimensions.SeatEyeHeight`) as well as the standing eye: pool radius 6.5 to 11.0 m, pool axis 10 to 13 m from the wall (shifted 2.2 m along it), pool coaming 0.30 to 0.15 m, gallery eye 2.2 m over the water (unchanged), containment height 20.6 to 20.85 m (head still 0.55 m under water). The head sits at least 3 degrees inside the window's downward reach (was 1) from both views.
- R3 The crane bridge moves back (z 9 to 10 m); the crane-clear sight tests keep passing.
- R4 `HallCheck`, `LayoutCheck`, the budget gates and the clean-room scan stay green.
- R5 README text and the four docs/media screenshots are regenerated at 1920x1080 from this tree, and the captions describe what is shown.
- R6 Before and after renders (window-stand, window-seat, gallery, seat, stand) are reviewed by eye; iterate until the head reads clearly.

## Verification

Linux `scripts/ci.sh`; Unity batchmode `scripts/ci.sh` on the Windows box; RoomRender and in-player captures for the images.
