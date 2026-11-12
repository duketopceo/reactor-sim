# Agent brief — accurate "living" reactor assets in Blender

**Date:** 2026-11-11 · **Status:** prep for external agent run (not committed work)
**Goal:** replace the primitive plant blockout with accurate, detailed, *animated* reactor
assets — fuel assemblies, control rods, CRDM drives, vessel internals, rotating equipment —
built in Blender and bound to live sim state.

---

## 1. Environment (do this first — nothing builds without it)

**Resolved 2026-11-11:** agents run on the Windows box. Blender 5.2.2 LTS is installed at
`C:\Program Files\Blender Foundation\Blender 5.2\blender.exe` and the headless verify already
passed (`PLANTEQUIP_SAVED`, 46 objects). Invoke it by full path or add it to PATH:

```
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --python-exit-code 1 --python Blender/scripts/make_plant_equipment.py
```

(The `HIPEW initialization failed` warning on launch is harmless — no GPU compute needed for FBX export.)

Fallbacks if a Linux run is wanted later: `flatpak install flathub org.blender.Blender` on
omarchy-max (aarch64 build exists; headless works the same via `flatpak run org.blender.Blender`),
or the blender.org aarch64 tarball.

## 2. Read these first (in order)

| File | Why |
|---|---|
| `AGENTS.md` (repo root) | Clean-room rule — hard constraint, see §4 |
| `docs/design/plant-and-assets.md` | **The asset spec**: ID convention, pivots, LOD, materials, metadata, export checklist |
| `Blender/README.md` | Pipeline: generate → export FBX → sync into Unity |
| `Blender/scripts/lib_common.py`, `lib_kits.py`, `lib_materials.py` | Existing helpers — reuse, don't reinvent |
| `Blender/scripts/make_plant_equipment.py` | Current P01–P08 blockout (89 lines of primitives — the thing being replaced/upgraded) |
| `C:\Users\kimba\nucleares-re\RESEARCH.md` + `C:\Users\kimba\nucleares-re\sections\reactor-core-fuel.md` | Behavioral dossier: rod banks, fuel bays, drive mechanics, visual feedback (Cherenkov glow, bubbles, turbulence). Synced from omarchy-max 2026-11-11. **Prose only — see §4** |
| `ReactorSim.Unity/Assets/Scripts/View3D/PlantEquipmentBinder.cs` | The Unity binding contract — object names it searches for |

## 3. The binding contract (do not break)

`PlantEquipmentBinder` finds moving parts **by transform name prefix**:

- `BIND_BAR_BankInsertion_{0..3}_*` — CRDM rods, translated by `Banks[i].Insertion`
- `Flywheel_PrimaryFlow*` / `Flywheel` — rotates with `PrimaryFlowFrac`
- `VFX_PORV_SteamJet*` / `VFX_SGRelief*` — steam jet anchors (relief state)
- `RackGlow_Cells_*`, `RackGlow_Haze_*`, `RackGlow_Light_*` — spent-fuel rack glow ∝ power

New assets must keep these names **or** the binder must be updated in the same change.
Design-doc metadata (`rs_id`, `rs_bind`, `rs_status` custom props) is the documented target;
today's assets predate it — pick one convention and apply it consistently.

Sim state available to drive "living" motion (`PlantSnapshot`): `Power`, `FuelTempC`,
`PrimaryFlowFrac`, `BankInsertion[4]`, `ReliefOpen`, `SecondaryReliefOpen`,
`SecondaryLevelFrac`, pressures/temps. Anything without Core state is decorative (`S`).

## 4. Hard constraints

- **Clean room.** Original assets only. Reference material = the dossier prose above +
  public engineering knowledge (PWR/SMR cutaways, fuel assembly photos, CRDM diagrams).
  **NEVER open `C:\Users\kimba\nucleares-re\decompiled\`** — repo AGENTS.md forbids it for implementation work.
- **Conventions** (`plant-and-assets.md` §1–2, §5):
  - Names: `RS_<CAT>_<Family>_<Variant>[_<Index>]` (CAT table in doc; `VES` vessel/cutaway,
    `ROD` rod drive, `SGN` steam generator, `PRI` primary, `TUR` turbine, `VAL` valves…).
    Existing P01–P08 names are legacy — new work uses the spec.
  - Blender Z-up, −Y forward, **1 unit = 1 m**, apply all transforms, no negative scale.
  - Export: FBX, `-Z forward / Y up`, `apply_unit_scale`, triangulate — see `export_unity.py`.
  - Pivots at functional axes (rod travel axis, shaft axis, hinge). LOD0/1/2 per doc.
  - Materials from the shared atlas (`lib_materials.py` + baked trim sheets); ≤8 per room.
- **Game-scale, not engineering drawings.** The design doc is explicit: fictional
  game-scale layout, playable game before complete plant. "Accurate" = convincing
  PWR detail (grid spacers, rodlets, drive latches, flanges, welds), within LOD budgets
  (~45–60 unique meshes minimum playable set; full set 130–200).
- **Determinism:** assets are visual only — nothing in `ReactorSim.Core` changes.
- **No partial implementations** (repo rule): every asset must export, import into Unity,
  and bind — no orphan meshes.

## 5. Suggested phasing (each phase = one PR-able unit)

| Phase | Content | Why first |
|---|---|---|
| A | **Fuel assembly + control rod + CRDM drive** — the hero assets seen through the pool window. Rod mesh with travel pivot, bank manifold, latch tube. Keep `BIND_BAR_BankInsertion_*` names. | Most visible; proves the whole loop |
| B | **Vessel internals** — core barrel, assembly array (visible, non-operational `C` status), thermal shield, cutaway shell. Glow ∝ `Core.Power`, tint ∝ `FuelTempC` per spec. | The "reactor" silhouette |
| C | **Rotating equipment** — pump casing/impeller, flywheel (keep `Flywheel_PrimaryFlow`), SG tube-sheet cutaway, turbine rotor stub. | "Working" motion cues |
| D | **Integration** — export FBX → `sync-blender-to-unity.sh` → Unity batchmode import check → binder verification in Play Mode. | Nothing counts until it renders in-scene |

"Living" details worth stealing from the dossier (§reactor-core-fuel): Cherenkov-blue
core glow scaling with power, bubble columns over hot bays, level-driven turbulence,
rod banks dropping on SCRAM. Map them onto existing `GlowMapping`/binder patterns —
extend, don't fork.

## 6. Verification loop (every asset)

```
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --python Blender/scripts/<script>.py          # build .blend
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b <file>.blend --python <turntable_render>.py   # render preview
# visual critique -> iterate
"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --python Blender/scripts/export_unity.py -- <Name>
bash Blender/scripts/sync-blender-to-unity.sh
# Unity: batchmode import check + Play Mode binder check
bash scripts/ci.sh                                       # before any commit
```

A render you haven't looked at is not done. Keep turntable renders in `Blender/previews/`
(gitignored or small) for review.

---

## 7. Agent prompt (paste-ready)

```
You are building accurate 3D reactor assets for ReactorSim, a deterministic PWR plant
simulator (Unity 6 front-end, procedural Blender pipeline). Work in
C:\Users\kimba\reactor-sim (Windows; use forward slashes or quoted paths in shell commands).

Blender is at "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" — invoke by full
path (or add its dir to PATH). Headless verify already passes; re-run it yourself first:
  "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" -b --python-exit-code 1 --python Blender/scripts/make_plant_equipment.py

READ FIRST, in order:
1. AGENTS.md (repo root) — clean-room rules are hard constraints
2. docs/design/plant-and-assets.md — the asset spec (naming, pivots, LOD, export)
3. Blender/README.md and Blender/scripts/{lib_common,lib_kits,lib_materials}.py
4. Blender/scripts/make_plant_equipment.py — current blockout you are upgrading
5. C:\Users\kimba\nucleares-re\sections\reactor-core-fuel.md (+ RESEARCH.md in the
   same dir) — behavioral reference for the "living reactor" (rod banks, fuel
   handling, glow/bubbles/turbulence cues). NEVER open anything under
   C:\Users\kimba\nucleares-re\decompiled/.
6. ReactorSim.Unity/Assets/Scripts/View3D/PlantEquipmentBinder.cs — Unity finds
   moving parts by name prefix; preserve or deliberately migrate these names.

TASK (Phase A — fuel assembly, control rod, CRDM drive):
- Model a PWR-style fuel assembly: grid spacers, ~100+ rodlets (instanced/linked
  duplicates, not 100 unique meshes), top/bottom nozzle. Then a single control rod
  (absorber section + drive shaft + tip) and a CRDM drive mechanism (housing, latch
  tube, position scale) sized to sit over the assembly.
- Accuracy bar: convincing close-up detail at 0.5–3 m viewing distance — real
  proportions (assembly ~4 m, rod travel visible), flange bolts, weld beads,
  spacer grid dimples. Game-scale, not CAD.
- Conventions: RS_<CAT>_<Family>_<Variant> names, Z-up/-Y fwd, 1 unit = 1 m, apply
  transforms, pivots on functional axes (rod travel axis = CRDM stroke axis).
- Keep or emit the binding names the Unity binder expects
  (BIND_BAR_BankInsertion_*, Flywheel_PrimaryFlow, VFX_*_Anchor) and set the
  rs_id/rs_bind/rs_status custom properties from plant-and-assets.md §2.
- Deliverables: Blender/ReactorCore.blend (or extend PlantEquipment.blend —
  decide and justify), Blender/scripts/make_reactor_core.py (regenerable,
  matching existing script style), exports/ReactorCore.fbx via export_unity.py
  (add the name to its BLENDS list), turntable preview renders, and a short
  docs note on what binds to what.

VERIFY before claiming done: script runs headless clean, .blend saves, FBX
exports, sync script copies it, renders look right at game camera distances.
Do not commit; leave changes in the working tree for review.
```

---

## 8. Model choice: gpt-6.1-sol vs gpt-6-astra

I can't verify specs for either — they're newer than my reliable knowledge, and your
`config.toml` shows `gpt-6.1-terra` as the current default with both listed only in
`model_availability_nux`. Honest answer:

- **Default pick: gpt-6.1-sol.** Newer generation (6.1 vs 6), and this task is
  long-horizon agentic work — codegen + spatial reasoning + self-review loops.
- **But run a bake-off.** The task's hard part isn't coding, it's *visual-spatial
  judgment* (does the fuel assembly look right? does the rod travel read?). Give both
  models the identical Phase-A prompt above, capped, and compare: (a) does the script
  run first try, (b) tri-count discipline, (c) render quality at game distance.
  Whichever wins takes phases B–D.
- If cost matters: sol at medium reasoning for the build loop; reserve high effort
  for the critique/iterate pass.
