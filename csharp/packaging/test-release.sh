#!/usr/bin/env bash
# Proves the release.sh wrapper: option pass-through to the d3sk-release tool through fakes (D3SK_RELEASE,
# D3SK_GATE), release.command and release.bat against the same fakes, and the P9 asset names from the real
# pinned tool (a dry run: writes nothing, needs no key, NSIS or WSL).
#   test-release.sh [--full]   (--full is accepted; there is no extra tier here)
set -euo pipefail
# shellcheck disable=SC1091
. "$(dirname "$0")/fake-release-lib.sh"

cat > "$tmp/fake-gate" <<'SHIM'
#!/usr/bin/env bash
echo called > "${FAKE_GATE_MARK:?}"
SHIM
export FAKE_GATE_MARK="$tmpn/gate" D3SK_GATE="bash $tmpn/fake-gate"

# run <label> <expected args, space separated, after "build"> <gate: 0|1> -- <release.sh args>
run() {
  local label="$1" want="$2" gate="$3"; shift 4
  rm -f "$FAKE_LOG" "$FAKE_GATE_MARK"
  if ! (cd "$root" && ./release.sh "$@" >/dev/null 2>"$tmp/err"); then bad "$label: release.sh failed: $(cat "$tmp/err")"; return; fi
  local got; got="$(tr '\n' ' ' < "$FAKE_LOG" | sed 's/ $//')"
  [ "$got" = "build${want:+ $want}" ] || { bad "$label: tool got '$got', wanted 'build${want:+ $want}'"; return; }
  if [ "$gate" = 1 ] && [ ! -f "$FAKE_GATE_MARK" ]; then bad "$label: the gate was not called"; return; fi
  if [ "$gate" = 0 ] && [ -f "$FAKE_GATE_MARK" ]; then bad "$label: the gate was called"; return; fi
  ok "$label"
}

run "no options run the gate" "" 1 --
run "--no-check skips the gate and is not passed on" "" 0 -- --no-check
run "--no-setup passes through" "--no-setup" 1 -- --no-setup
run "--no-dmg passes through" "--no-dmg" 1 -- --no-dmg
run "--version passes through" "--version 9.9.9" 1 -- --version 9.9.9
run "--out passes through" "--out $tmpn/o" 1 -- --out "$tmpn/o"
run "--dry-run passes through and skips the gate" "--dry-run" 0 -- --dry-run
run "--verbose passes through" "--verbose" 1 -- --verbose
run "--json passes through" "--json" 1 -- --json

help="$(cd "$root" && ./release.sh --help)"
for w in --no-check --no-setup --no-dmg --dry-run NSIS; do
  case "$help" in *"$w"*) ok "--help mentions $w" ;; *) bad "--help does not mention $w" ;; esac
done
if (cd "$root" && ./release.sh --bogus >/dev/null 2>&1); then bad "unknown option accepted"; else ok "unknown option refused"; fi

# release.command and release.bat (Windows), unattended: no pause, no folder opened.
rm -f "$FAKE_LOG"
if (cd "$root" && D3SK_UNATTENDED=1 ./release.command --no-check --no-dmg >/dev/null 2>&1) \
   && [ "$(tr '\n' ' ' < "$FAKE_LOG")" = "build --no-dmg " ]; then ok "release.command"; else bad "release.command"; fi
case "${OSTYPE:-}" in
  msys* | cygwin*)
    rm -f "$FAKE_LOG"
    if (cd "$root" && D3SK_UNATTENDED=1 cmd.exe //c ".\release.bat" --no-check --no-setup >/dev/null 2>&1) \
       && [ "$(tr '\n' ' ' < "$FAKE_LOG" | tr -d '\r')" = "build --no-setup " ]; then ok "release.bat"; else bad "release.bat"; fi ;;
esac

# The P9 names from the real pinned tool: nothing is written, no key or tool is needed.
unset D3SK_RELEASE D3SK_GATE
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/csharp/Directory.Build.props")"
if real="$(cd "$root" && ./release.sh --no-check --dry-run --json 2>"$tmp/err")"; then
  for name in "d3skcommander-$version-windows-x86_64.zip" "d3skcommander-$version-linux-x86_64.tar.gz" \
              "d3skcommander-$version-macos-aarch64.tar.gz"; do
    case "$real" in *"\"message\":\"$name\""*) ok "dry run lists $name" ;; *) bad "dry run does not list $name" ;; esac
  done
  case "$real" in *PRIVATE*) bad "dry run output holds key material" ;; *) ok "dry run output holds no key material" ;; esac
else
  bad "real dry run failed: $(cat "$tmp/err")"
fi

fake_finish "all release.sh tests passed"
