#!/usr/bin/env bash
# Proves the github-release.sh wrapper with a fake d3sk-release (D3SK_RELEASE): it runs
# `publish --config release.json`, passes its options through unchanged, and stops with the tool's
# exit code. Fakes only: no gh, no network, no key.
#   test-github-release.sh [--full]   (--full is accepted; there is no extra tier here)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
failures=0
ok() { echo "ok   $*"; }
bad() { echo "FAIL $*" >&2; failures=$((failures + 1)); }

tmpn="$tmp"
case "${OSTYPE:-}" in msys* | cygwin*) tmpn="$(cygpath -m "$tmp")" ;; esac
cat > "$tmp/fake-release" <<'SHIM'
#!/usr/bin/env bash
printf '%s\n' "$@" > "${FAKE_LOG:?}"
exit "${FAKE_EXIT:-0}"
SHIM
export FAKE_LOG="$tmpn/args"
export D3SK_RELEASE="bash $tmpn/fake-release"

# run <label> <expected args after "publish --config release.json"> -- <github-release.sh args>
run() {
  local label="$1" want="$2"; shift 3
  rm -f "$FAKE_LOG"
  if ! (cd "$tmp" && "$here/github-release.sh" "$@" >/dev/null 2>"$tmp/err"); then bad "$label: wrapper failed: $(cat "$tmp/err")"; return; fi
  local got; got="$(tr '\n' ' ' < "$FAKE_LOG" | tr -d '\r' | sed 's/ $//')"
  local exp="publish --config release.json${want:+ $want}"
  [ "$got" = "$exp" ] || { bad "$label: tool got '$got', wanted '$exp'"; return; }
  ok "$label"
}

run "no options" "" --
run "--dry-run passes through" "--dry-run" -- --dry-run
run "--dry-run --json passes through" "--dry-run --json" -- --dry-run --json
run "--verbose passes through" "--verbose" -- --verbose
run "--version passes through" "--version 9.9.9" -- --version 9.9.9
run "--out passes through" "--out $tmpn/o" -- --out "$tmpn/o"

# A non-zero exit from the tool stops the wrapper with that code.
rc=0
FAKE_EXIT=7 "$here/github-release.sh" >/dev/null 2>&1 || rc=$?
[ "$rc" = 7 ] && ok "tool exit code 7 propagates" || bad "tool exit code 7 gave $rc"

[ "$failures" = 0 ] || { echo "$failures failure(s)" >&2; exit 1; }
echo "all github-release.sh tests passed"
