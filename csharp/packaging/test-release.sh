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

# The signing key: by default the checkout's gitignored .release-keys/ (D3SKCOMMANDER_RELEASE_KEY_HOME moves it);
# a key inside a git checkout that git tracks or does not ignore is refused before the tool runs.
export D3SK_RELEASE="bash $tmpn/fake-release" FAKE_ENV_LOG="$tmpn/keyenv"
native() { case "${OSTYPE:-}" in msys* | cygwin*) cygpath -m "$1" ;; *) printf '%s' "$1" ;; esac; }
if git -C "$root" check-ignore -q .release-keys/release-signing-key.pem; then ok ".gitignore covers .release-keys/"
else bad ".gitignore does not cover .release-keys/"; fi
mkdir -p "$tmp/keyhome"; printf 'k\n' > "$tmp/keyhome/release-signing-key.pem"
rm -f "$FAKE_ENV_LOG"
if (cd "$root" && D3SKCOMMANDER_RELEASE_KEY_HOME="$tmp/keyhome" ./release.sh --no-check >/dev/null 2>"$tmp/err") \
   && [ "$(tr -d '\r' < "$FAKE_ENV_LOG")" = "$(native "$tmp/keyhome/release-signing-key.pem")" ]; then
  ok "the tool gets the key folder's release-signing-key.pem by default"
else bad "default key path: $(cat "$FAKE_ENV_LOG" 2>/dev/null) $(cat "$tmp/err")"; fi
git init -q "$tmp/keyrepo"; printf 'k\n' > "$tmp/keyrepo/signing.pem"
# key_case <label> <expected stderr word, empty = accepted>
key_case() {
  rm -f "$FAKE_LOG"
  if (cd "$root" && D3SKCOMMANDER_RELEASE_KEY_FILE="$tmp/keyrepo/signing.pem" ./release.sh --no-check >/dev/null 2>"$tmp/err"); then
    if [ -z "$2" ]; then ok "$1"; else bad "$1: accepted"; fi
  elif [ -n "$2" ] && grep -q "$2" "$tmp/err" && [ ! -f "$FAKE_LOG" ]; then ok "$1"
  else bad "$1: $(cat "$tmp/err")"; fi
}
key_case "a key in a checkout that git does not ignore is refused" "not ignored"
printf 'signing.pem\n' > "$tmp/keyrepo/.gitignore"
key_case "a key in a checkout that git ignores is accepted" ""
git -C "$tmp/keyrepo" add -f signing.pem
key_case "a key git tracks is refused, even when ignored" "tracked by git"

# restore-release-key.sh: the backup's note becomes the key file, readable by this user only; never overwrites.
if command -v openssl >/dev/null 2>&1; then
  openssl ecparam -name prime256v1 -genkey -noout -out "$tmp/throwaway.pem" 2>/dev/null
  printf 'not a key\n' > "$tmp/junk.txt"
  cat > "$tmp/bw" <<'SHIM'
#!/usr/bin/env bash
[ "$1 $2 $3" = "get notes the-item" ] || exit 1
cat "$FAKE_BW_NOTE"
SHIM
  chmod +x "$tmp/bw"
  restore() { BW="$tmp/bw" FAKE_BW_NOTE="$1" D3SKCOMMANDER_RELEASE_KEY_ITEM=the-item D3SKCOMMANDER_RELEASE_KEY_HOME="$2" \
    "$here/restore-release-key.sh" >"$tmp/out" 2>&1; }
  if restore "$tmp/throwaway.pem" "$tmp/restored" && cmp -s "$tmp/throwaway.pem" "$tmp/restored/release-signing-key.pem" \
     && ! grep -q PRIVATE "$tmp/out"; then ok "restore writes the note into the key folder and prints no key"
  else bad "restore: $(cat "$tmp/out")"; fi
  if restore "$tmp/throwaway.pem" "$tmp/restored"; then bad "restore over an existing key: accepted"
  else ok "restore never overwrites a key"; fi
  if restore "$tmp/junk.txt" "$tmp/junk" || [ -e "$tmp/junk/release-signing-key.pem" ]; then bad "a note that is no key: accepted"
  else ok "a note that is no EC key leaves no file"; fi
else
  echo "skip restore-release-key.sh tests: no openssl"
fi

fake_finish "all release.sh tests passed"
