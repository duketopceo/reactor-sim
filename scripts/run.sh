#!/usr/bin/env bash
# Builds and runs the console harness (ReactorSim.Runner); every argument is passed through, for example
#   bash scripts/run.sh --scenario all      bash scripts/run.sh --verify      bash scripts/run.sh --serve 7770
# With no argument it runs the scripted baseline transient with telemetry.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="/c/Program Files/dotnet:$PATH"
dotnet run --project ReactorSim.Runner "$@"
