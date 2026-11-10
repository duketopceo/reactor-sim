# Control room inventory

`inventory.csv` is the single list of every control, readout, alarm tile and CRT page the control room
needs: one row per physical device. Blender placement, Unity checks, binding tests and the rollup all read it.
`rollup.md` is generated from it; never edit it by hand.

## Columns

| Column | Meaning |
|---|---|
| `id` | Stable unique id (snake_case). Becomes the `CTL_<id>` parent empty in the room. |
| `area`, `system`, `subsystem`, `function`, `device` | Category path, plant area down to the physical device. All five are required. Areas: Nuclear island, Turbine island, Electrical, Control and safety. |
| `role` | `control`, `indicator`, `alarm` (annunciator or alarm lamp tile) or `page` (CRT page). |
| `binding` | What the device is wired to. See below. |
| `range_or_positions` | Controls and gauges: `min..max`. Momentary buttons: the button value (`1`). Planned switches: positions (`open\|closed`). Alarms: the threshold as documentation. |
| `units` | Engineering units of the value (empty for lamps and pages). |
| `physical_type`, `kit` | KTD5 physical type (`rotary_knob`, `setpoint_slider`, `stepped_knob`, `pushbutton`, `guarded_toggle`, `lever`, `handwheel`, `circular_gauge`, `edgewise_meter`, `counter`, `annunciator_tile`, `status_lamp`, `crt_page`) and the kit id from `kits.json`. A type exception is recorded in `type_note`. |
| `readiness` | `bound`, `needs_mapping` or `needs_core_model`. See below. |
| `phase` | `room` (works today), `backend` (U8 or a small Core snapshot addition), `electrical` (U10), `plumbing` (U12). |
| `section`, `panel`, `slot` | Board placement. `section` is one of `reactor`, `primary`, `safety`, `steam`, `turbine`, `electrical` (R4 order); `panel` is a panel id in `layout.json`; `slot` is `<surface>:<row>:<col>` with surface `bench` or `vert`. On `vert`, rows 0-2 are the annunciator band and rows 3 and up the instrument band. See the `_doc` of `layout.json`. |
| `label` | Full plain-language name, used for hover text. Never carries a `[type note: ...]`. |
| `short_label` | Terse control-board label (`PZR LVL`, `AUX`, `CWP A`) engraved on the plate or lens. Required. It must fit at the KTD8 text height from the row's operating point: see "Label fit" below. |
| `type_note` | Why the row's physical type differs from the KTD5 default (empty when it does not). |
| `overview` | `y` when the device must also read from `STAND_Center` (annunciators, main gauges). |
| `paired_indicator` | For a control, the `id` of the row that shows its effect. |
| `description` | One plain sentence, a little personality welcome, shown in the hover tooltip with the value, setpoint and limits. Required on every row. |

## Binding syntax

| Prefix | Form | Resolves through |
|---|---|---|
| `intent:` | `intent:<Kind>[:<Target>]` | `PivotNameParser` then `ControlIntentFactory.IsSupported`. A range `a..b` makes a dial (`INT_DIAL_<Kind>_[<Target>_]a_b`), a single value makes a button (`INT_BTN_<Kind>_[<Target>_]v`). Kinds with no default target (TurbineAutoLoad, GeneratorBreaker, GridDemand, VacuumPump, CoolingWater) carry an explicit target. |
| `gauge:` | `gauge:<SelectorName>[:<index>]` | `GaugeSources.TryResolve`. An index makes a `BIND_BAR_` name, otherwise `BIND_GAUGE_`. |
| `lamp:` | `lamp:<NAME>` | A `BIND_LAMP_<NAME>` predicate in `ControlRoom3DBinder.UpdateLamps`. |
| `crt:` | `crt:<PageName>` | `CrtPages.KnownPages`. |
| `core:` | `core:<proposed name>` | Nothing yet: a name for the Core state or intent that has to exist first. |

## Readiness

- `bound`: the binding resolves today (factory supports the kind, selector exists, lamp predicate exists, page is known).
- `needs_mapping`: the sim value or intent exists but no mapping, selector, lamp or page exists yet (U8 adds them).
- `needs_core_model`: the sim has no state for it. The device is still placed, marked out of service (KTD6).

`InventoryTests` enforces this both ways: a `bound` row must resolve, and a `needs_mapping` row that now
resolves fails the test, so the column cannot go stale. When you add a mapping, flip the row to `bound`.

Per-loop rows: loop A binds to today's lumped value, loop B is `needs_core_model` until the loops split (U12).

`LoadPreset` and `SetTimeScaleDev` are deliberately not rows (see `ExcludedKinds` in `InventoryTests`).

## Layout

`layout.json` holds the room shell, the board arc, the section and panel widths, the system-to-section map, the operating points, the cameras and the check thresholds. The spec is `docs/design/main-control-room.md`. `InventoryTests` checks types, kits, slots, panel fit, section ownership and the view-angle rule against it.

## Label fit

Engraved text should subtend `checks.min_text_deg` (0.9 degrees) from the row's `STAND_<section>` point. The required
cap height is the eye-to-label distance times tan(0.9 degrees): 17 to 32 mm for the board as laid out. Plates are one
line, `max(kit face_w, grid.min_plate_width)` wide minus 8 mm, so the budget is a handful of characters: about 4 on a
K19 or K20 plate, 5 on K11, 8 on K12, 9 on K08, 11 on K02 and 18 on K01. A K05 tile shows its `short_label` on the lens
as one line of 5 characters or fewer, or two lines split at a space, each of 5 or fewer. The generator engraves each
row at its own height. `python3 -B Blender/scripts/room_layout.py` fails when any `short_label` is wider than its
plate at that height (glyph advance modelled as 0.6 times the cap height). The engraved height is capped by the plate
(19 mm on the 25 mm plate), so rows that need more than that are reported as a note, not an error.

## Rollup

```
python3 scripts/inventory-rollup.py
```

Prints the tree `area > system > subsystem > function > device` with counts per node, then totals by
readiness, role, physical_type, kit, phase, section and section/panel, and writes the same to `design/control-room/rollup.md`.
Exits non-zero on a missing column, an empty category level, a duplicate id or a bad role, readiness or phase.
It also writes `ReactorSim.Unity/Assets/StreamingAssets/inventory-runtime.json` (id, label, short label, units, range, description,
kit, role): the game has no CSV at runtime, and `ControlTooltip` loads that file once. `InventoryTests` fails when it is stale, so
commit it with `inventory.csv` and `rollup.md`.
Regenerate and commit `rollup.md` with any change to `inventory.csv`.

## Tests

```
dotnet test ReactorSim.Core.Tests -c Release --filter InventoryTests
```
