#!/usr/bin/env bash
# Builds every release package locally into dist/ (gitignored) with the d3sk-release tool
# (release.json). Nothing is built on GitHub and nothing is published:
#   ./release.sh              gate (csharp/check.sh), then: three archives (Windows zip, Linux and
#                             macOS tar.gz), Windows setup, .dmg, SHA256SUMS and SHA256SUMS.sig
#   ./release.sh --no-check   skip the gate
#   ./release.sh --no-setup   skip the Windows setup only
#   ./release.sh --no-dmg     skip the .dmg only
#   ./release.sh --version V  override the version (default: <Version> in csharp/Directory.Build.props)
#   ./release.sh --out DIR    output folder (default dist/)
#   ./release.sh --dry-run    list the steps and artifacts, write nothing, skip the gate
#   ./release.sh --verbose    show each external command; --json: machine-readable output
# A missing tool (NSIS, WSL, rcodesign) fails the build naming its install command.
# Seams for tests: D3SK_GATE (default csharp/check.sh), D3SK_RELEASE (default dotnet tool run d3sk-release).
set -euo pipefail
cd "$(dirname "$0")"
ROOT="$(pwd)"

CHECK=1
PASS=()
while [ $# -gt 0 ]; do
  case "$1" in
    --no-check) CHECK=0 ;;
    --dry-run) CHECK=0; PASS+=("$1") ;;
    --no-setup|--no-dmg|--verbose|--json) PASS+=("$1") ;;
    --version|--out|--config)
      [ $# -ge 2 ] || { echo "ERROR: $1 needs a value" >&2; exit 2; }
      PASS+=("$1" "$2"); shift ;;
    --version=*|--out=*|--config=*) PASS+=("$1") ;;
    -h|--help) sed -n '2,14p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "ERROR: unknown option '$1' (--no-check, --no-setup, --no-dmg, --version, --out, --dry-run, --verbose, --json, --help)" >&2; exit 2 ;;
  esac
  shift
done

# Optional, git-ignored local environment (same file as csharp/check.sh).
if [ -f "$ROOT/.env" ]; then
  set -a
  # shellcheck disable=SC1091
  . "$ROOT/.env"
  set +a
fi

if [ "$CHECK" = 1 ]; then
  echo "== gate: ${D3SK_GATE:-csharp/check.sh}"
  ${D3SK_GATE:-csharp/check.sh}
fi

if [ -z "${D3SK_RELEASE:-}" ]; then
  if ! command -v dotnet >/dev/null 2>&1; then
    if [ -n "${DOTNET_ROOT:-}" ] && [ -x "$DOTNET_ROOT/dotnet" ]; then
      export PATH="$DOTNET_ROOT:$PATH"
    else
      echo "ERROR: dotnet not found: install the .NET 10 SDK or set DOTNET_ROOT" >&2
      exit 1
    fi
  fi
  # The tool is a private D3sk.* package: its restore needs the read:packages credential (never printed).
  # shellcheck disable=SC1091
  . "$ROOT/csharp/packaging/sdk-env.sh"
  d3sk_packages_credentials || exit 1
  dotnet tool restore >&2
  RELEASE=(dotnet tool run d3sk-release)
else
  # shellcheck disable=SC2206
  RELEASE=($D3SK_RELEASE)
fi

"${RELEASE[@]}" build ${PASS[@]+"${PASS[@]}"}
