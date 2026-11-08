#!/usr/bin/env bash
# The C# gate: format check, build (warnings are errors), view size, tests not marked slow.
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

# The read:packages credential of the private D3sk.* source, only once a D3sk.* package is referenced.
# shellcheck disable=SC1091
. "$ROOT/csharp/packaging/sdk-env.sh"
if d3sk_uses_private_packages "$ROOT/csharp"; then d3sk_packages_credentials || exit 1; fi

dotnet format "$SLN" --verify-no-changes
dotnet build "$SLN" -warnaserror --nologo -v q

# No Avalonia view over 600 lines (*.axaml, *.axaml.cs, *View.cs).
too_long=$(find src \( -name '*.axaml' -o -name '*.axaml.cs' -o -name '*View.cs' \) -not -path '*/obj/*' -not -path '*/bin/*' -print0 \
  | xargs -0 -r wc -l | awk '$2 != "total" && $1 > 600 {print $2 " (" $1 " lines)"}')
if [ -n "$too_long" ]; then
  echo "ERROR: view files over 600 lines:" >&2
  echo "$too_long" >&2
  exit 1
fi

# Hang guard: a test silent for D3SK_TEST_HANG_TIMEOUT (default 5 min; the slowest test takes
# well under one) aborts the run, names the test that was running and fails the gate.
# Dump type none: no dump tool needed on any OS, and the run ends at once.
HANG=(--blame-hang-timeout "${D3SK_TEST_HANG_TIMEOUT:-5m}" --blame-hang-dump-type none)
# Blame results (Sequence_*.xml) go out of the source tree.
RESULTS="${TMPDIR:-${TEMP:-/tmp}}/d3sk-test-results"

# The abort kills the VSTest host but not the xUnit v3 test program it started: left alive, that
# one locks bin/ and holds the caller's output pipe open (`./check.sh | tail` never returns).
kill_test_programs() {
  case "${OSTYPE:-}" in
    msys* | cygwin*)
      local bin
      bin="$(cygpath -w "$PWD/tests")"
      powershell.exe -NoProfile -NonInteractive -Command \
        "Get-CimInstance Win32_Process | Where-Object { \$_.ExecutablePath -like '$bin\\*\\bin\\*' } | ForEach-Object { Stop-Process -Id \$_.ProcessId -Force -ErrorAction SilentlyContinue }" || true
      ;;
    *) pkill -f "$PWD/tests/[^ ]*/bin/" || true ;;
  esac
}

FILTER=()
[ "$FULL" = 1 ] || FILTER=(--filter "Tier!=Slow")
status=0
dotnet test "$SLN" --no-build --nologo ${FILTER[@]+"${FILTER[@]}"} "${HANG[@]}" --results-directory "$RESULTS" || status=$?
if [ "$status" != 0 ]; then
  kill_test_programs
  exit "$status"
fi
