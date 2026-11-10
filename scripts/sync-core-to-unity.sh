#!/usr/bin/env bash
# Build ReactorSim.Core and copy the assembly into the Unity project's Plugins.
# Run after any Core change before opening/refreshing Unity.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="/c/Program Files/dotnet:$PATH"

dotnet build ReactorSim.Core -c Release -v q
DEST="ReactorSim.Unity/Assets/Plugins/ReactorSim"
mkdir -p "$DEST"
cp ReactorSim.Core/bin/Release/netstandard2.1/ReactorSim.Core.dll "$DEST/"
cp ReactorSim.Core/bin/Release/netstandard2.1/ReactorSim.Core.pdb "$DEST/" 2>/dev/null || true
echo "Synced ReactorSim.Core.dll -> $DEST"
bash scripts/core-source-hash.sh > "$DEST/core-source.sha256"
echo "Recorded Core source hash -> $DEST/core-source.sha256"
