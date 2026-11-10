#!/usr/bin/env bash
# Prints one sha256 over the ReactorSim.Core sources (*.cs, *.csproj; bin/obj excluded).
# Single definition shared by sync-core-to-unity.sh (writes it) and ci.sh (verifies it).
# Deterministic: C-locale sorted paths, per-file content hash (CRLF-normalised) + path.
set -euo pipefail
cd "$(dirname "$0")/.."
find ReactorSim.Core -type f \( -name '*.cs' -o -name '*.csproj' \) \
    -not -path '*/bin/*' -not -path '*/obj/*' | LC_ALL=C sort |
while IFS= read -r f; do
  printf '%s  %s\n' "$(tr -d '\r' < "$f" | sha256sum | cut -d' ' -f1)" "$f"
done | sha256sum | cut -d' ' -f1
