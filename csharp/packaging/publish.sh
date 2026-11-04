#!/usr/bin/env bash
# Self-contained build of the app for one runtime, archived under the P38 release names:
#   csharp/packaging/publish.sh win-x64     -> dist/d3skcommander-<version>-windows-x86_64.zip
#   csharp/packaging/publish.sh linux-x64   -> dist/d3skcommander-<version>-linux-x86_64.tar.gz
#   csharp/packaging/publish.sh osx-arm64   -> dist/d3skcommander-<version>-macos-aarch64.tar.gz
# Any runtime builds from any OS (cross-publish). The program is one executable
# (D3skCommander[.exe]) with the native ImGui/GLFW libraries beside it; nothing is extracted
# to a temp folder at start. The Windows setup is csharp/packaging/setup-windows.sh.
# .deb, AppImage and .dmg are still plan row 91.
set -euo pipefail
cd "$(dirname "$0")/../.."
ROOT="$(pwd)"

RID="${1:-win-x64}"
case "$RID" in
  win-x64) NAME=windows-x86_64; EXT=zip ;;
  linux-x64) NAME=linux-x86_64; EXT=tar.gz ;;
  osx-arm64) NAME=macos-aarch64; EXT=tar.gz ;;
  *) echo "ERROR: unknown runtime '$RID' (win-x64, linux-x64, osx-arm64)" >&2; exit 2 ;;
esac

if [ -f "$ROOT/.env" ]; then set -a; . "$ROOT/.env"; set +a; fi
if ! command -v dotnet >/dev/null 2>&1; then
  if [ -n "${DOTNET_ROOT:-}" ] && [ -x "$DOTNET_ROOT/dotnet" ]; then export PATH="$DOTNET_ROOT:$PATH"
  else echo "ERROR: dotnet not found — install the .NET 10 SDK or set DOTNET_ROOT" >&2; exit 1; fi
fi

VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' csharp/Directory.Build.props)
BASE="d3skcommander-$VERSION-$NAME"
OUT="$ROOT/dist/$BASE"
rm -rf "$OUT" "$ROOT/dist/$BASE.$EXT"
mkdir -p "$ROOT/dist"

dotnet publish csharp/src/D3skCommander.App/D3skCommander.App.csproj \
  -c Release -r "$RID" --self-contained true --nologo \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false \
  -p:DebugType=none -p:GenerateDocumentationFile=false \
  -o "$OUT"
cp LICENSE "$OUT/"

cd "$ROOT/dist"
if [ "$EXT" = zip ]; then
  if command -v zip >/dev/null; then zip -qr "$BASE.zip" "$BASE"
  else python3 -c 'import shutil,sys; shutil.make_archive(sys.argv[1], "zip", ".", sys.argv[1])' "$BASE"; fi
else
  tar -czf "$BASE.tar.gz" "$BASE"
fi
sha256sum "$BASE.$EXT" > "$BASE.$EXT.sha256"
ls -l "$OUT" "$BASE.$EXT"
