#!/usr/bin/env bash
# Sync Blender FBX exports into the Unity project.
# Usage: bash Blender/scripts/sync-blender-to-unity.sh
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
SRC="$ROOT/Blender/exports"
DEST="$ROOT/ReactorSim.Unity/Assets/Blender"
mkdir -p "$DEST"
for f in ControlRoom.fbx PanelKits.fbx PlantEquipment.fbx; do
  if [ -f "$SRC/$f" ]; then cp "$SRC/$f" "$DEST/$f"; echo "Copied $f"; else echo "Missing $SRC/$f (run Blender export first)"; fi
done
if compgen -G "$SRC/Textures/*.png" > /dev/null; then
  mkdir -p "$DEST/Textures"
  cp "$SRC"/Textures/*.png "$DEST/Textures/"
  echo "Copied $(ls "$SRC"/Textures/*.png | wc -l) trim-sheet textures"
else
  echo "No baked textures (run Blender/scripts/bake_trim_sheets.py to add them)"
fi
