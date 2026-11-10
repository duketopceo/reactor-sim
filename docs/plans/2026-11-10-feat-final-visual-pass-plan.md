---
title: "feat: final visual pass (PR5)"
type: feat
status: active
date: 2026-11-10
---

# Final visual pass (PR5)

Follows PR1 (controls), PR2 (SMR hall), PR3 (visual polish). PR4 (hall lighting and window) is a sibling; this plan keeps out of its files.

## Requirements

- R1 Circular dial numerals never sit where the needle rests: the minimum and maximum stand past the sweep ends (`GaugeScale.NumeralSpot`), the middle keeps the needle angle.
- R2 Tooltip and drag-readout live values go through `ReadoutFormat.Adaptive` (0 decimals from 1000, 1 from 10, 2 below, zeros trimmed).
- R3 A monitor face that no `CRT_` page owns (kit mesh `Screen`) gets a `CrtScreenRenderer` on the standby page; the standby page reads NO SIGNAL inside the row budget.
- R4 Edgewise bar meters print minimum and maximum (unit on the maximum); the middle is left to the tick lines.
- R5 Room contrast: wall, ceiling and floor tints step further apart (`RoomLighting`). Blender material and wall-detail work needs Blender on the Windows box and is a recorded follow-up when that box is offline.
- R6 README screenshots refresh after PR4 merges.

## Test scenarios

- `GaugeScaleTests`: end numerals past the sweep ends and mirrored; middle at the needle angle.
- `ReadoutFormatTests`: `Adaptive` precision and trimming.
- `CrtPageLayoutTests`: standby page content, row budget, width.
- `RoomLightingTests`: wall and floor steps.
- `ControlTooltipTests`: live value now `1480 ppm`.

## Out of scope

Hall geometry, hall lighting and the window block (PR4).
