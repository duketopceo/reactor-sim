# Plant, control room and Blender asset plan

> Fictional, game-scale layout for **educational entertainment**. Not a real facility layout, not
> security-sensitive, not training material. Counts are **planning estimates**, not measurements.
> Rule zero: **a playable game before a complete plant.** Anything marked *later* is optional for the first playable.

Companions: [verification](../testing/simulation-verification.md) · [observability](../development/observability.md).

## 1. Conventions

### 1.1 Asset ID

`RS_<CAT>_<Family>_<Variant>[_<Index>]`, PascalCase families, `LODn` suffix on meshes, `_COL` colliders, `_PIV` empties.

| CAT | Meaning | CAT | Meaning |
|---|---|---|---|
| `VES` reactor vessel/cutaway | `ELE` electrical |
| `ROD` rod drive | `SEN` sensors (in-world) |
| `SGN` steam generator | `CTL` control-desk modules |
| `PRI` primary pump/pipe/pressurizer | `ANN` annunciators/meters/displays |
| `VAL` valves/actuators | `SWI` switches/buttons/knobs |
| `AUX` auxiliary cooling | `ROM` room kit |
| `TNK` tanks/heat exchangers | `SIG` signage |
| `TUR` turbine/generator/condenser | `PRP` maintenance props |

Example: `RS_PRI_Pump_Main_01` (mesh), `RS_PRI_Pump_Main_01_COL`, `RS_PRI_Pump_Main_01_PIV_Shaft`.

### 1.2 Subsystem binding ID

Dotted, lowercase, stable (never renamed once shipped; used by saves, journal and UI):
`plant.<system>.<component>[.<channel>]`, e.g. `plant.primary.pumpA`, `plant.core.rods.bankA`, `plant.sg.loopA.level`.
Each interactable/indicator asset carries one binding ID in export metadata. Bindings resolve to Core state
paths (today: `Plant.Banks[3].Insertion`, `Thermal.PrimaryFlowFrac`, `Core.Power`, …); anything with no Core state yet is **decorative until its model exists**.

### 1.3 Status vocabulary

* **I** interactable (bound, player-operable) · **D** display (bound read-only) · **S** scenery (no binding) · **C** cutaway (visible, non-operational, may animate from a bound value).

### Commanded, actual, indicated

Every I/D asset exposes three distinct values, never one:

| Value | Meaning | Example (rod bank) |
|---|---|---|
| **Commanded** | what the operator/controller asked for | `Banks[3].InsertionTarget` |
| **Actual** | process truth in the simulation | `Banks[3].Insertion` |
| **Indicated** | what the instrument shows (lag, noise, bias, dropout, fault) | demand/position indicators; *no sensor layer on master — indicated = actual until it exists* |

Control-desk lamps show *commanded and indicated*; only the developer overlay shows *actual*
([observability §7](../development/observability.md#7-developer-overlays)). **Operator alarms** (player-facing, the process is in trouble)
and **developer faults** (the model/tooling is in trouble: non-finite, domain violation, solver failure)
are different channels with different IDs and never share a lamp.

## 2. Asset families

Mesh/prefab reuse: one **trim sheet + one hard-surface kit** serves ≥70 % of industrial props; indicators share a single
parametric `Gauge`, `Lamp`, `Trend` prefab with per-instance binding; switches share three base prefabs with swapped caps.
Pivots/orientation: Blender **Z-up, −Y forward**, 1 unit = 1 m, scale applied (1,1,1); in Unity Y-up, +Z forward after export.
LOD: LOD0 close, LOD1 ≈ 40 % tris, LOD2 ≈ 15 %; props < 0.3 m have no LOD, culled by distance. Colliders: simple primitives/convex, never render mesh.
Materials from a shared atlas: `RS_MAT_Metal_Painted`, `_Metal_Raw`, `_Concrete`, `_Glass`, `_Emissive_Lamp`, `_Screen`, `_Decal_Sign` (≤ 8 per room).
Export metadata (custom properties): `rs_id`, `rs_bind`, `rs_status` (I/D/S/C), `rs_priority`, `rs_lod`, `rs_license`, `rs_author`, `rs_version`.

| Family (ID prefix) | Members | Status | Binding example | Animation / pivot | Collider | LOD | Pri | Est. unique meshes (plan) |
|---|---|---|---|---|---|---|---|---|
| Vessel & cutaway `VES` | vessel shell, head, core barrel, fuel-assembly array (visible, non-operational), thermal shield, internals | C | `plant.core` (glow ∝ `Core.Power`, tint ∝ `FuelTempC`) | rod/assembly emissive only; pivot at vessel base centre | box/convex | 3 | P1 (display model) | 6–10 |
| Rod drives `ROD` | drive housing, latch tube, position scale, bank-group manifold | C/D | `plant.core.rods.bank{A..D}` | tube translates with `Insertion`; pivot top of housing | box | 2 | P0 | 4–6 |
| Steam generators `SGN` | shell, tube-sheet cutaway, steam outlet, feed inlet, level gauge glass | C/D | `plant.sg.loop{A,B}` | water-level shader ∝ `SecondaryLevelFrac` | convex | 3 | P0/P1 | 5–8 |
| Primary pumps & piping `PRI` | pump casing, motor, flywheel, hot/cold leg pipe set, elbows, supports, flanges | I/C | `plant.primary.pump{A,B}` | shaft rotates ∝ flow; pivot at shaft | convex | 3 | P0 | 10–14 |
| Pressurizer `PRI` | vessel, heaters bank, spray line, relief/safety valves | C/I | `plant.pzr` | heater glow, level shader | convex | 2 | P0 | 5–7 |
| Valves & actuators `VAL` | gate, globe, check, relief, control valve + actuator (motor/pneumatic), handwheel | I/D | `plant.<sys>.valve.<n>` | stem ∝ position (lag = actuator), handwheel rotates | box | 2 | P0 (control, isolation) | 8–12 |
| Aux cooling `AUX` | residual-heat-removal skid, component-cooling skid, emergency injection skid, pumps | I/C | `plant.aux.*` | pump spin, status lamps | convex | 2 | P2 | 8–12 |
| Tanks & exchangers `TNK` | boron/makeup tank, accumulator, letdown/heat exchanger, demineraliser, storage | C/D | `plant.chem.*` | level shader ∝ inventory (needs Core state) | convex | 2 | P1 | 8–12 |
| Turbine hall `TUR` | turbine casing, generator, exciter, condenser, circulating-water pumps/pipes, cooling-tower or outfall (stylised) | C/I | `plant.turb.*` | rotor spin ∝ speed (when modelled) | convex | 3 | P2 | 12–18 |
| Electrical `ELE` | switchgear cabinets, breakers, battery bank, emergency generator, transformer | I/D | `plant.elec.*` | breaker handle, lamps | box | 2 | P2 | 10–14 |
| Sensors `SEN` | thermowell, pressure transmitter, flow element, level transmitter, neutron detector housing | S/D | `plant.<sys>.sensor.<n>` | none (label shows channel) | box | 1 | P1 | 6–8 |
| Control-desk modules `CTL` | desk segment (flat/angled), vertical panel, mimic panel, CRT/flat display bezel, supervisor console | S/C | – | – | box | 2 | P0 | 10–14 |
| Annunciators/meters/trends `ANN` | annunciator tile (windowed), analog gauge, digital readout, strip recorder, trend screen, log-scale neutron meter | D | `plant.*` | needle pivot at gauge centre | none | 1 | P0 | 6–8 |
| Switches/buttons/knobs `SWI` | guarded SCRAM switch, push-button (momentary/latching), rotary selector, rod-bank joystick/lever, slider | I | `intent.*` | guard flips (pivot hinge), knob rotates (pivot axis) | box | 1 | P0 | 8–12 |
| Room kit `ROM` | wall/floor/ceiling modules (1 m grid), doors, stairs, catwalks, railings, ladders, glazing, ducts, cable trays | S | – | door hinge pivots | box | 2 | P0 | 25–35 |
| Signage `SIG` | room IDs, hazard/zone plates, floor lines, valve tags (decals; text from data) | S/D | – | – | none | 0 | P1 | 8–12 (decals) |
| Maintenance props `PRP` | tool cart, hose, scaffolding, barrel, ladder, lockout-tag board, PPE locker | S | – | – | box | 1 | P3 | 10–15 |

**Planning estimate:** ≈130–200 unique meshes for the full set, **≈45–60 for the minimum playable set**. These are
estimates to size the work; they are not measured or committed.

## Minimum playable set

Goal: a clean startup → power hold → trip → recovery loop with two players. **Only P0** families, built as *one* of each, instanced.

| Item | Why it is enough | Est. meshes |
|---|---|---|
| Control-room shell (2 rooms from room kit) + 6 desk segments + 2 vertical panels | zones for reactor and balance-of-plant roles | 10 |
| Annunciator wall (one tile prefab) + 6 gauge/readout variants + 1 log-scale meter + 1 trend screen | all readouts from existing `Core`/`Thermal` state | 9 |
| Guarded SCRAM, 3 push-buttons, 2 rotary, 4 rod-bank levers/sliders | commands the sim already understands (`Scram`, `SetAllRods`, rod targets, steam demand, dosing) | 8 |
| One display-only reactor cutaway (vessel + rods + pump + SG + pressurizer, 1 loop) visible through a window | gives the player a physical picture | 14 |
| Corridor + stair + door + 1 catwalk | navigation loop | 6 |

Later expansions in order: turbine hall → electrical/emergency power → auxiliary cooling → maintenance spaces → outdoor cooling.

## 3. Control-room zones

The walkable main control room, its sections and panels, and where each zone below sits are specified in [main-control-room.md](main-control-room.md); the layout numbers are in `design/control-room/layout.json`.

| Zone | Content | Bound state (today) | Primary role |
|---|---|---|---|
| 1 Reactor / neutronics | rod bank levers, position/demand, startup-rate and log/linear power, ρ ledger bar, Xe/I | `Banks[*]`, `Core.Power`, `Core.*Pcm`, `Xenon135` | Reactor operator |
| 2 Primary temp/pressure/inventory | T-hot/T-cold, pressure, subcooling margin, level, heaters/spray | `Thermal.Primary*`, `PrimarySatMarginC` | Reactor operator |
| 3 SG / feedwater / steam | SG level and pressure, steam demand, feed (when modelled) | `SecondaryPressureBar`, `SecondaryLevelFrac`, `SteamDemandFrac` | Balance-of-plant |
| 4 Turbine / grid | speed, MWe, vacuum, sync (*later*) | — | Balance-of-plant |
| 5 Power auxiliaries | buses, breakers, batteries, emergency power (*later*) | — | Balance-of-plant |
| 6 Cooling / safety | emergency cooling, SCRAM, trip bistables (*later*) | `Scrammed`, … | Either; shared |
| 7 Alarm / event / trend | first-out board, event list, trend wall | alarm layer | Shared sightline |
| 8 Supervisor / missions | mission board, assistant, debrief | assistant ([ASSISTANT.md](../ASSISTANT.md)) | Supervisor/instructor |

Layout rule: **one zone ≈ one glance.** Critical state (power, rod demand vs position, trip status, first-out) is visible from the shared centre; detail lives at the owner's desk.

## 4. Fictional room adjacency

```
              [Cooling exterior]──[Turbine hall]──[Service corridor S]
                    │                  │                 │
 [Containment] ─ airlock ─ [Aux/Elec support] ─ [Service corridor N] ─ [Maintenance bay]
      (cutaway view only)         │                 │
                                  └──── [CONTROL ROOM] ──── [Supervisor / briefing]
```

| Room | Adjacent to | Function | Notes |
|---|---|---|---|
| Control room | support/electrical, supervisor, corridor N | all operator zones | centre of play; loads first |
| Supervisor/briefing | control room | missions, debrief | glass wall = sightline to the board |
| Support/electrical/auxiliaries | control room, containment airlock, corridor | switchgear, batteries, aux cooling | go-to for loss-of-power drills |
| Reactor containment | airlock only | cutaway vessel/primary, **player does not enter during operation** | seen through windows/cameras; safe abstraction |
| Turbine hall | support, cooling exterior, corridor S | turbine/generator/condenser | large volume, lowest-detail interior |
| Cooling exterior | turbine hall | circulating water, outfall (stylised) | streamed |
| Service corridors N/S | everything | navigation loops, catwalks | two loops so players can bypass a blocked door |
| Maintenance bay | corridor N | props, tag-out board, repair mini-tasks | cosmetic/abstract maintenance (no real procedures) |

* **Navigation loops:** control room → support → corridor N → corridor S → turbine hall → … → control room (≥ 1 loop so the alarm response has two routes).
* **Cooperative roles:** reactor operator (zones 1–2), BOP operator (3–5), supervisor (7–8). Cross-trained tasks (SCRAM, alarm acknowledge) reachable from both desks.
* **Sightlines/readability:** centre board visible from every desk; red/amber/white lamp semantics are consistent; text size ≥ legible at 2.5 m.
* **Alarm response:** annunciator → first-out → desk or field action (walk to a skid) → feedback lamp. Walking time is a *game* cost; no real procedure is reproduced.
* **Radiation and maintenance gameplay:** controlled-access areas outside the operating containment can carry modeled, clearly displayed radiation exposure. Maintenance and isolation are fictional game abstractions, not real-world procedures. The operating containment remains inaccessible; any later access requires a separately designed and tested plant state and route.
* **Loading/performance boundaries:** control room + support always resident; containment cutaway is a lightweight low-poly proxy; turbine hall and exterior stream additively; LOD2 and occlusion at doors/airlocks.

## 5. Blender → Unity workflow

**Format choice must follow the project's actual import path.** `ReactorSim.Unity/Packages/manifest.json` lists no glTF package, and Unity does not import glTF without one *(I could not re-verify Unity docs from the sandbox)*; Unity imports **FBX** natively.

| | glTF 2.0 (`.glb`) | FBX |
|---|---|---|
| Unity import | needs a package such as [glTFast](https://github.com/atteneder/glTFast) (Apache-2.0, seen 2026-11-07, requires Unity 6000.0+) or the Unity-hosted [com.unity.cloud.gltfast](https://github.com/Unity-Technologies/com.unity.cloud.gltfast) | native |
| Blender | first-party add-on ([docs](https://docs.blender.org/manual/en/latest/addons/scene_gltf2.html), link not re-verified) | first-party add-on |
| Custom metadata | node `extras` (→ JSON) | custom properties (limited) |
| Open spec | yes | proprietary format |
| Recommendation | **start with FBX (zero new dependencies)**; adopt glTF + glTFast if the extras metadata or open format becomes valuable. Decide with a one-asset round-trip test. |

Checklist (applies to either): units = metres; Apply all transforms; origin/pivot at the functional hinge/axis; forward −Y/up Z in
Blender; names per §1.1; no negative scale; triangulate on export; one material per surface type from the shared atlas;
collider and LOD meshes named as in §1.1.
**Metadata verification:** an editor script (to be written) must fail the import when an I/D asset lacks `rs_id`/`rs_bind`, when `rs_bind` is not in the binding registry,
when scale ≠ 1, or when a name violates the pattern — so a typo cannot silently produce a dead gauge.

## 6. Manifest generation

Keep the manifest (`assets/manifest.csv`, *to be created*) as the source of truth: columns `rs_id, family, status, rs_bind, priority, lod, collider, material, pivot, est_tris, license, author`. Generated from the tables above; CI checks that every `rs_bind` resolves.

## 7. Licensing

Original assets only; each carries `rs_license`/`rs_author`. No third-party assets are imported; code licensing and asset licensing are tracked separately.
