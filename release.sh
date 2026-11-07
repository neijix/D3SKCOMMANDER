#!/usr/bin/env bash
# Builds every release package locally into dist/ (gitignored). Nothing is built on GitHub and
# nothing is published:
#   ./release.sh              gate (csharp/check.sh), the three archives, the Windows setup, SHA256SUMS
#   ./release.sh --no-check   skip the gate
#   ./release.sh --no-setup   skip the Windows setup
# Output in dist/: d3skcommander-<version>-{windows-x86_64.zip,linux-x86_64.tar.gz,
# macos-aarch64.tar.gz,windows-x86_64-setup.exe} and SHA256SUMS. The version comes from
# csharp/Directory.Build.props. Every runtime cross-publishes from any OS (bash: Git Bash on
# Windows). The setup needs NSIS 3 (makensis, or NSISDIR — see csharp/packaging/setup-windows.sh);
# without it the setup is skipped with a warning and the archives are still built.
set -euo pipefail
cd "$(dirname "$0")"
ROOT="$(pwd)"

CHECK=1
SETUP=1
for a in "$@"; do
  case "$a" in
    --no-check) CHECK=0 ;;
    --no-setup) SETUP=0 ;;
    -h|--help) sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "ERROR: unknown option '$a' (--no-check, --no-setup, --help)" >&2; exit 2 ;;
  esac
done

VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' csharp/Directory.Build.props)
echo "== d3skcommander $VERSION"

if [ "$CHECK" = 1 ]; then
  echo "== gate: csharp/check.sh"
  csharp/check.sh
fi

for rid in linux-x64 win-x64 osx-arm64; do
  echo "== archive: $rid"
  csharp/packaging/publish.sh "$rid"
done

has_nsis() {
  command -v makensis >/dev/null 2>&1 || [ -n "${NSISDIR:-}" ]
}

SKIPPED=""
if [ "$SETUP" = 1 ] && has_nsis; then
  echo "== Windows setup"
  csharp/packaging/setup-windows.sh
elif [ "$SETUP" = 1 ]; then
  SKIPPED="Windows setup skipped: makensis not found (install NSIS 3, e.g. 'sudo apt install nsis', or set NSISDIR)"
fi

cd "$ROOT/dist"
files=()
for f in "d3skcommander-$VERSION-"*.zip "d3skcommander-$VERSION-"*.tar.gz "d3skcommander-$VERSION-"*-setup.exe; do
  [ -f "$f" ] && files+=("$f")
done
# A setup left over from an earlier run of this version is not part of this build.
if [ "$SETUP" = 0 ] || [ -n "$SKIPPED" ]; then
  kept=()
  for f in "${files[@]}"; do [[ "$f" == *-setup.exe ]] || kept+=("$f"); done
  files=("${kept[@]}")
fi
sha256sum "${files[@]}" > SHA256SUMS

echo "== done: $ROOT/dist"
ls -l "${files[@]}" SHA256SUMS
if [ -n "$SKIPPED" ]; then
  echo "WARNING: $SKIPPED" >&2
fi
