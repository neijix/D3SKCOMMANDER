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

# shellcheck disable=SC1091
. "$ROOT/csharp/packaging/release-env.sh"
d3sk_load_env

if [ "$CHECK" = 1 ]; then
  echo "== gate: ${D3SK_GATE:-csharp/check.sh}"
  ${D3SK_GATE:-csharp/check.sh}
fi

d3sk_release_cmd

"${RELEASE[@]}" build ${PASS[@]+"${PASS[@]}"}
