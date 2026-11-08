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

# shellcheck disable=SC1091
. "$ROOT/csharp/packaging/release-env.sh"
d3sk_load_env
d3sk_release_cmd
exec "${RELEASE[@]}" publish --config release.json "$@"
