#!/usr/bin/env bash
# Publishes the release built by ./release.sh to GitHub with the d3sk-release tool (release.json).
# Owner-run: it needs gh signed in and the release signing key. The tool tags v<version>, uploads a
# draft, verifies the signature against the pinned public key and only then publishes; a key mismatch
# stops before publishing. Every option goes to the tool unchanged, for example:
#   github-release.sh --dry-run          print the steps and the exact gh commands, make no gh call
#   github-release.sh --dry-run --json   the same, machine-readable
#   github-release.sh --verbose          show each external command
#   github-release.sh --version V --out DIR
# Seam for tests: D3SK_RELEASE (default dotnet tool run d3sk-release). Runs from any folder.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"

case "${1:-}" in
  -h | --help) sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
esac

# Optional, git-ignored local environment (same file as release.sh).
if [ -f "$ROOT/.env" ]; then
  set -a
  # shellcheck disable=SC1091
  . "$ROOT/.env"
  set +a
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

exec "${RELEASE[@]}" publish --config release.json "$@"
