# Tooling

## In this repo

- `scripts/ci.sh` - the verification contract (clean-room scan, build, tests, scenarios, `--verify`)
- `scripts/run.sh` - build and run the console harness
- `scripts/sync-core-to-unity.sh` - build the Core and copy it into the Unity project
- `scripts/clean_room_check.sh` - fails if a tracked file path or content (including compressed and UTF-16 data) contains a banned term or a per-user profile path; `--self-test` runs its negative controls
- `tools/mcp-bridge-config.json` - example MCP client configuration for the bridge

## Prerequisites

| Tool | Use |
|---|---|
| .NET SDK 8 | builds; the Core library targets netstandard2.1 for Unity |
| Unity 6 (6000.6.7f1) | optional: the control room front-end and its batchmode checks |
| Blender 5.x | optional: regenerate the procedural assets (`Blender/README.md`) |

## Open-source physics references

- **PyRK** (BSD-3): 0-D point kinetics and lumped thermal-hydraulics; the kinetics shape follows it.
- **IAPWS-IF97**: steam saturation curve fitted in `Physics.cs`.
- **Keepin data**: six delayed-neutron group constants.
- **OpenMC** (MIT): optional offline Monte Carlo runs to calibrate simplified coefficients (rod worth, boron
  worth).
- **CoolProp** (MIT): possible drop-in replacement for `Physics.Saturation*` if full property tables are wanted.
- **`paulromano/awesome-nuclear`**: master list of open-source nuclear codes.
