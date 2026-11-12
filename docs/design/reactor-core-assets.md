# Living Reactor Assets

Original, fictional game-scale PWR cutaways for educational entertainment, not
engineering drawings, physical light predictions or real operating procedures.

## Player View

F8 enters live remote inspection. Left/Right switches between the fuel array,
rod drives, primary pumps and turbine cutaway. The wheel changes zoom; Esc or F8
restores the operator's position, orientation, FOV and culling mask.
The simulation keeps running. Room controls and other camera/overlay modes are
suspended; Escape returns before it can open the pause menu.

The normal gallery is unchanged. Its pool rim and window sill obscure the deep
fuel array even with a vessel cutaway. Inspection is an explicit camera abstraction,
not a claim that the operator can see through the deck or enter the pool.
Inspection reuses the core light as a neutral camera-positioned service lamp;
it does not add a fifth light or pretend that this fill is Cherenkov illumination.

## Asset Ownership

- `ReactorCore.blend`: one window-facing detailed assembly, four absorber clusters, shafts and
  CRDM housings. Rodlets share a batched lattice per LOD rather than unique meshes
  and draw submissions per rod.
- `VesselInternals.blend`: eight simplified neighboring assemblies, open core
  barrel, thermal shield, support grid and emissive core tiles.
- `RotatingEquipment.blend`: four shoulder pump motors with fins and bolted flanges,
  flow-driven flywheels and exposed impellers, integral tube bundle and a small turbine rotor display;
  not an external primary loop or a new turbine building.

Generators use metres, applied unit scale, Blender Z-up and -Y forward. FBX maps
to Unity Y-up/+Z forward; the placer turns imported cutaways to face the hall's
-Z window. Preview lights/cameras are not saved. LOD1 and LOD2 target approximately
40% and 15% of LOD0 geometry.

The procedural vessel retains its heads and envelope but opens its straight wall
towards the containment slot. Blender replaces the old pump motor bodies;
overlapping legacy PlantEquipment is never placed in the hall.

## Live Binding Contract

`PlantEquipmentBinder` delegates authored assets to `ReactorAssetMotion`;
existing rack, relief and legacy flywheel behavior remains.
The obsolete equipment-side `BIND_BAR_BankInsertion_*` animation is retired;
control-room gauge bars continue to belong to the control-room binder.

| Name | Source | Motion |
|---|---|---|
| `ROD_BANK_{A..D}_PIV` | `BankInsertion[0..3]` | Actual insertion, never target |
| `RS_ROD_Bank{A..D}_Withdrawn_PIV` | Authored stationary endpoint | Insertion 0 |
| `RS_ROD_Bank{A..D}_Inserted_PIV` | Authored stationary endpoint | Insertion 1 |
| `Flywheel_PrimaryFlow_*_PIV` | `PrimaryFlowFrac` | Visual speed up to 240 deg/s |
| `ROTOR_Turbine_PIV` | `TurbineRpm` | Rated visual speed at 3000 RPM |
| `RS_VES_CoreGlow_*` | `Power`, `FuelTempC` | Bounded blue emission and color cue |

Each moving bank and its stationary markers share a parent coordinate frame.
The 3.9 m stroke survives FBX conversion and preserves authored offsets.
Identity is parsed, never assigned by traversal order; fuel remains stationary.
Missing/NaN rod samples insert safely, and valid values clamp to 0..1.
SCRAM follows actual insertion without a separate drop animation.

Rotor axis markers survive coordinate conversion. The visual RPM mapping prevents
aliasing; it does not change physics. Moving meshes are not batching-static.
Core emission is configured explicitly in Unity and written through property
blocks, so neither imported metal nor rack materials are recolored. One bounded
core point light uses the last slot in the four-light hall budget.
Fuel-temperature tint is a gameplay cue, not normal-temperature incandescence.

## Metadata And Budgets

`assets/manifest.csv` is generated from authored properties using the design
spec's columns plus `asset` and `object_path`. Sync copies it to
`Assets/Blender/ReactorManifest.csv`. FBX properties are documentation, not assumed
runtime fields. The editor resolves sidecar identities against actual scene
paths, known binding IDs and triangulated imported meshes, and rejects extra objects.

The entire core and internals allocation is at most 40k resident triangles including
all LODs; the detailed standalone assembly is at most 6k LOD0 triangles.
Hall checks retain conservative counting and the 120k cap. Shared meshes do not
erase per-instance triangle cost. Four rod banks share one detailed demonstration
assembly; the nine-assembly display is not a real core loading plan.

## Verification

Regeneration, export, manifest, sync and preview commands are in `Blender/README.md`.
Unity batchmode entry points:

- `HallCheck.Run`: envelope, layers, collider-free hall, budgets and repeat rebuild.
- `ReactorAssetCheck.Run`: shuffled bank identity, physical endpoints, stationary fuel,
  missing state, actual simulated SCRAM, RPM, glow and manifest/mesh agreement.
- `ReactorAssetPlayCheck.Run`: real live binder, inspection input ownership, four
  in-game captures and exact camera restoration; requires graphics and no `-quit`.
- `ReactorAssetRender.Run`: neutral diagnostic and true gallery captures at low/high
  power; diagnostic fill lighting is not part of gameplay budgets.

The play check rebuilds/saves Bootstrap before playing. Captures go to ignored
`out/reactor-assets/`. Full CI also checks clean-room content, Core freshness,
build, tests, scenarios and numerical verification. The committed-scene drift
gate remains a delivery requirement and rejects a new scene while deliberately
left uncommitted for user review.

Bubbles/turbulence, independent relief jets, latch-mechanism simulation and Core
physics changes remain deferred.
