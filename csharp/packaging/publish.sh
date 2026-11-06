#!/usr/bin/env bash
# Self-contained build of the app for one runtime, archived under the P38 release names:
#   csharp/packaging/publish.sh win-x64     -> dist/d3skcommander-<version>-windows-x86_64.zip
#   csharp/packaging/publish.sh linux-x64   -> dist/d3skcommander-<version>-linux-x86_64.tar.gz
#   csharp/packaging/publish.sh osx-arm64   -> dist/d3skcommander-<version>-macos-aarch64.tar.gz
# Any runtime builds from any OS (cross-publish). The program is one executable
# (D3skCommander[.exe]) with the native Avalonia (Skia, HarfBuzz) libraries beside it; nothing is extracted
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
# DebugType=none only covers our own code: SkiaSharp and HarfBuzzSharp ship native symbol files
# (~100 MB of .pdb on Windows) that users never need.
find "$OUT" -name '*.pdb' -delete
cp LICENSE "$OUT/"

cd "$ROOT/dist"
if [ "$EXT" = zip ]; then
  # Git Bash has no zip: Windows 10+ ships a bsdtar that writes one (-a picks it from .zip).
  WINTAR=""
  if [ -n "${SYSTEMROOT:-}" ] && command -v cygpath >/dev/null; then WINTAR="$(cygpath -u "$SYSTEMROOT")/System32/tar.exe"; fi
  if command -v zip >/dev/null; then zip -qr "$BASE.zip" "$BASE"
  elif [ -n "$WINTAR" ] && [ -x "$WINTAR" ]; then "$WINTAR" -a -c -f "$BASE.zip" "$BASE"
  else python3 -c 'import shutil,sys; shutil.make_archive(sys.argv[1], "zip", ".", sys.argv[1])' "$BASE"; fi
else
  tar -czf "$BASE.tar.gz" "$BASE"
fi
sha256sum "$BASE.$EXT" > "$BASE.$EXT.sha256"
ls -l "$OUT" "$BASE.$EXT"
