#!/usr/bin/env bash
# The C# gate: format check, build (warnings are errors), .axaml size, tests not marked slow.
# --full adds the slow tier (tests carrying the [Slow] trait Tier=Slow).
set -euo pipefail
cd "$(dirname "$0")"
ROOT="$(cd .. && pwd)"

FULL=0
for a in "$@"; do [ "$a" = "--full" ] && FULL=1; done

# Optional, git-ignored local environment: DOTNET_ROOT, DOTNET_CLI_HOME, NUGET_PACKAGES, PATH prefix.
if [ -f "$ROOT/.env" ]; then
  set -a
  # shellcheck disable=SC1091
  . "$ROOT/.env"
  set +a
fi

if ! command -v dotnet >/dev/null 2>&1; then
  if [ -n "${DOTNET_ROOT:-}" ] && [ -x "$DOTNET_ROOT/dotnet" ]; then
    export PATH="$DOTNET_ROOT:$PATH"
  else
    echo "ERROR: dotnet not found — install the .NET 10 SDK or set DOTNET_ROOT" >&2
    exit 1
  fi
fi

SLN=D3skCommander.sln

dotnet format "$SLN" --verify-no-changes
dotnet build "$SLN" -warnaserror --nologo -v q

# No .axaml view over 600 lines.
too_long=$(find src -name '*.axaml' -not -path '*/obj/*' -not -path '*/bin/*' -print0 \
  | xargs -0 -r wc -l | awk '$2 != "total" && $1 > 600 {print $2 " (" $1 " lines)"}')
if [ -n "$too_long" ]; then
  echo "ERROR: .axaml files over 600 lines:" >&2
  echo "$too_long" >&2
  exit 1
fi

if [ "$FULL" = 1 ]; then
  dotnet test "$SLN" --no-build --nologo
else
  dotnet test "$SLN" --no-build --nologo --filter "Tier!=Slow"
fi
