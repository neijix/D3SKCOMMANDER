#!/usr/bin/env bash
# Build (release, incremental) and start d3skcommander. Extra arguments go to the app.
set -euo pipefail
cd "$(dirname "$0")"
# Launched from a desktop menu, PATH may miss dotnet: fall back to DOTNET_ROOT.
if ! command -v dotnet >/dev/null && [ -n "${DOTNET_ROOT:-}" ]; then export PATH="$DOTNET_ROOT:$PATH"; fi
exec dotnet run --project csharp/src/D3skCommander.App -c Release -- "$@"
