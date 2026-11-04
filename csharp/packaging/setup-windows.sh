#!/usr/bin/env bash
# Windows NSIS setup from the self-contained win-x64 publish:
#   csharp/packaging/setup-windows.sh
#   -> dist/d3skcommander-<version>-windows-x86_64-setup.exe
# Uses an existing dist/d3skcommander-<version>-windows-x86_64 folder, or builds it
# with publish.sh when that folder is missing. Needs makensis (NSIS 3): on PATH, or
# NSISDIR pointing at the directory that contains Include/MUI2.nsh (the Debian layout
# is <prefix>/usr/share/nsis with makensis in <prefix>/usr/bin).
set -euo pipefail
cd "$(dirname "$0")/../.."
ROOT="$(pwd)"

RID_DIR_NAME=windows-x86_64

find_nsis() {
  local bin share
  if command -v makensis >/dev/null 2>&1; then
    bin=$(command -v makensis)
  else
    bin=""
  fi
  if [ -n "${NSISDIR:-}" ] && [ -f "$NSISDIR/Include/MUI2.nsh" ]; then
    share=$NSISDIR
  elif [ -n "$bin" ] && [ -f "$(dirname "$bin")/../share/nsis/Include/MUI2.nsh" ]; then
    share=$(cd "$(dirname "$bin")/../share/nsis" && pwd)
  elif [ -f /usr/share/nsis/Include/MUI2.nsh ]; then
    share=/usr/share/nsis
  else
    share=""
  fi
  if [ -z "$bin" ] || [ -z "$share" ]; then
    echo "ERROR: makensis not found. Install NSIS 3, or unpack the nsis and nsis-common packages and set NSISDIR to their usr/share/nsis (with usr/bin on PATH)." >&2
    exit 1
  fi
  NSISDIR=$(cd "$share" && pwd)
  MAKENSIS=$bin
  export NSISDIR
}

version_quad() {
  case "$1" in
    *.*.*.*) printf '%s\n' "$1" ;;
    *.*.*) printf '%s.0\n' "$1" ;;
    *) echo "ERROR: version '$1' is not major.minor.patch" >&2; exit 1 ;;
  esac
}

ensure_publish() {
  local dir="$1"
  if [ ! -f "$dir/D3skCommander.exe" ]; then
    "$ROOT/csharp/packaging/publish.sh" win-x64
  fi
  if [ ! -f "$dir/D3skCommander.exe" ]; then
    echo "ERROR: $dir/D3skCommander.exe is missing after publish" >&2
    exit 1
  fi
}

write_file_list() {
  local src="$1" out="$2" rel dir base win
  {
    echo '!macro InstallPublishedFiles'
    while IFS= read -r rel; do
      if printf '%s' "$rel" | grep -q '[[:space:]$`"!\\]'; then
        echo "ERROR: cannot pack file name: $rel" >&2
        exit 1
      fi
      dir=$(dirname "$rel")
      base=$(basename "$rel")
      if [ "$dir" = "." ]; then
        echo '  SetOutPath "$INSTDIR"'
      else
        win=${dir//\//\\}
        echo "  SetOutPath \"\$INSTDIR\\${win}\""
      fi
      echo "  File \"/oname=${base}\" \"${src}/${rel}\""
    done < <(find "$src" -type f -printf '%P\n' | sort)
    echo '!macroend'
    echo '!macro UninstallPublishedFiles'
    while IFS= read -r rel; do
      win=${rel//\//\\}
      echo "  Delete \"\$INSTDIR\\${win}\""
    done < <(find "$src" -type f -printf '%P\n' | sort)
    while IFS= read -r rel; do
      [ -n "$rel" ] || continue
      win=${rel//\//\\}
      echo "  RMDir \"\$INSTDIR\\${win}\""
    done < <(find "$src" -mindepth 1 -type d -printf '%P\n' | sort -r)
    echo '!macroend'
  } > "$out"
}

compile_setup() {
  local version="$1" quad="$2" list="$3" outfile="$4" kb="$5"
  "$MAKENSIS" -NOCD \
    -DVERSION="$version" \
    -DVERSIONQUAD="$quad" \
    -DOUTFILE="$outfile" \
    -DICON="$ROOT/packaging/icons/icon.ico" \
    -DLICENSE="$ROOT/LICENSE" \
    -DFILELIST="$list" \
    -DESTIMATEDSIZE="$kb" \
    "$ROOT/csharp/packaging/nsis/setup.nsi"
}

main() {
  local version quad payload list outfile kb
  find_nsis
  version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' csharp/Directory.Build.props)
  quad=$(version_quad "$version")
  payload="$ROOT/dist/d3skcommander-${version}-${RID_DIR_NAME}"
  ensure_publish "$payload"
  list="$ROOT/dist/.nsis-files.nsh"
  outfile="$ROOT/dist/d3skcommander-${version}-${RID_DIR_NAME}-setup.exe"
  write_file_list "$payload" "$list"
  kb=$(du -sk "$payload" | awk '{print $1}')
  compile_setup "$version" "$quad" "$list" "$outfile" "$kb"
  sha256sum "$outfile" > "$outfile.sha256"
  ls -l "$outfile"
}

main "$@"
