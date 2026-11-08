# Sourced by test-release.sh and test-github-release.sh (not run on its own; named outside check.sh's
# test-*.sh glob). Sets here, root, tmp (removed on exit), tmpn (native path), failures, ok, bad, a fake
# d3sk-release (D3SK_RELEASE; logs its args to FAKE_LOG, exits FAKE_EXIT) and fake_finish.
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
failures=0
ok() { echo "ok   $*"; }
bad() { echo "FAIL $*" >&2; failures=$((failures + 1)); }

# The fakes live in a folder without spaces; a native Windows path for cmd.exe.
tmpn="$tmp"
case "${OSTYPE:-}" in msys* | cygwin*) tmpn="$(cygpath -m "$tmp")" ;; esac
cat > "$tmp/fake-release" <<'SHIM'
#!/usr/bin/env bash
printf '%s\n' "$@" > "${FAKE_LOG:?}"
exit "${FAKE_EXIT:-0}"
SHIM
export FAKE_LOG="$tmpn/args"
export D3SK_RELEASE="bash $tmpn/fake-release"

# fake_finish <message>: exit 1 on any failure, else print the message.
fake_finish() {
  [ "$failures" = 0 ] || { echo "$failures failure(s)" >&2; exit 1; }
  echo "$1"
}
