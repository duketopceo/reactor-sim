#!/usr/bin/env bash
# Prints a normalized, order-independent form of a Unity YAML scene so two scenes can be compared for semantic
# equality. Unity re-rolls fileIDs and reorders documents on every save, so the raw files are never byte-identical
# between two builds of the same hierarchy. Here every fileID / anchor is masked and the remaining lines are sorted,
# which keeps every serialized value (names, components, transforms, materials, script refs) but drops the churn.
#   bash scripts/scene-signature.sh Assets/Scenes/Bootstrap.unity | sha256sum
set -euo pipefail
[ $# -eq 1 ] || { echo "usage: $0 <scene.unity>" >&2; exit 2; }
tr -d '\r' < "$1" \
  | sed -E 's/fileID: -?[0-9]+/fileID: N/g; s/^(--- !u![0-9]+) &-?[0-9]+.*/\1 \&N/' \
  | LC_ALL=C sort
