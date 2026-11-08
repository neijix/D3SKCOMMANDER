#!/usr/bin/env bash
# Proves the build-token chain of sdk-env.sh with fakes only: no network, no vault, no real token.
# Each case runs the chain in a subshell with a fake HOME, a fake bw-auto and a fake gh.
#   test-sdk-env.sh [--full]   (--full is accepted; there is no real-tool branch here)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
failures=0
ok() { echo "ok   $*"; }
bad() { echo "FAIL $*" >&2; failures=$((failures + 1)); }

# Fakes. The tokens are throwaway strings, never real ones.
mkdir -p "$tmp/bin" "$tmp/code/BitwardenAuto" "$tmp/home"
cat > "$tmp/code/BitwardenAuto/bw-auto" <<'SHIM'
#!/usr/bin/env bash
[ -z "${FAKE_BW_DOWN:-}" ] || exit 1
case "$1" in
  get-pass) [ "$2" = d3skhand-github-packages-read-token ] && printf '%s\n' "tok-from-bitwarden" || exit 1 ;;
  get) printf 'Name     : x\nUsername : bwuser\nPassword : tok-from-bitwarden\n' ;;
esac
SHIM
cat > "$tmp/bin/gh" <<'SHIM'
#!/usr/bin/env bash
case "$1 $2" in
  "auth status") [ -z "${FAKE_GH_SCOPE:-}" ] || echo "  - Token scopes: 'read:packages', 'repo'" ;;
  "auth token") echo "tok-from-gh" ;;
  "api user") echo "ghuser" ;;
esac
SHIM
chmod +x "$tmp/code/BitwardenAuto/bw-auto" "$tmp/bin/gh"

# run_chain [VAR=value ...]: prints "<status>|<credential>" (the credential only so the test can compare).
run_chain() {
  (
    cd "$tmp"
    unset NuGetPackageSourceCredentials_github D3SKCOMMANDER_PACKAGES_USER FAKE_BW_DOWN FAKE_GH_SCOPE
    export HOME="$tmp/home" AI_CODE_ROOT="$tmp/code" PATH="$tmp/bin:$PATH"
    for kv in "$@"; do export "${kv?}"; done
    # shellcheck disable=SC1091
    . "$here/sdk-env.sh"
    st=0
    d3sk_packages_credentials 2>"$tmp/err" || st=$?
    echo "$st|${NuGetPackageSourceCredentials_github:-}"
  )
}
expect() { # name, expected "status|credential"
  local got
  got="$(run_chain "${@:3}")"
  if [ "$got" = "$2" ]; then ok "$1"; else bad "$1 (got '$got', want '$2')"; fi
}

state="$tmp/home/.d3skcommander-release"
expect "env var wins and is kept" "0|Username=u;Password=envtok" \
  "NuGetPackageSourceCredentials_github=Username=u;Password=envtok" FAKE_GH_SCOPE=1
expect "Bitwarden item next" "0|Username=bwuser;Password=tok-from-bitwarden"
expect "login override" "0|Username=me;Password=tok-from-bitwarden" D3SKCOMMANDER_PACKAGES_USER=me

mkdir -p "$state"; printf 'tok-from-file\nsecond line\n' > "$state/github-packages-read-token"; chmod 600 "$state/github-packages-read-token"
expect "token file when Bitwarden is down (first line, 0600)" "0|Username=me;Password=tok-from-file" FAKE_BW_DOWN=1 D3SKCOMMANDER_PACKAGES_USER=me
chmod 644 "$state/github-packages-read-token"
case "${OSTYPE:-}" in
  msys* | cygwin*) echo "skip token file mode check (Windows has no modes)" ;;
  *) expect "token file readable by others is refused" "1|" FAKE_BW_DOWN=1 D3SKCOMMANDER_PACKAGES_USER=me ;;
esac
rm -f "$state/github-packages-read-token"
expect "gh token with read:packages last" "0|Username=ghuser;Password=tok-from-gh" FAKE_BW_DOWN=1 FAKE_GH_SCOPE=1
expect "gh without read:packages is not used" "1|" FAKE_BW_DOWN=1

# The failure names the four ways and never prints a token.
run_chain FAKE_BW_DOWN=1 >/dev/null
msg="$(cat "$tmp/err")"
for w in NuGetPackageSourceCredentials_github d3skhand-github-packages-read-token github-packages-read-token "read:packages"; do
  case "$msg" in *"$w"*) ok "failure names $w" ;; *) bad "failure names $w" ;; esac
done
run_chain >/dev/null
case "$(cat "$tmp/err")" in *tok-from*) bad "a token reached stderr" ;; *) ok "no token on stderr" ;; esac

# No D3sk.* package referenced: the lookup is skipped (nothing needs it today).
mkdir -p "$tmp/none"
if (cd "$tmp" && . "$here/sdk-env.sh" && ! d3sk_uses_private_packages "$tmp/none"); then ok "no D3sk.* reference: skipped"; else bad "no D3sk.* reference: skipped"; fi
mkdir -p "$tmp/some"; echo '<PackageVersion Include="D3sk.Update" Version="1.0.0" />' > "$tmp/some/Directory.Packages.props"
if (cd "$tmp" && . "$here/sdk-env.sh" && d3sk_uses_private_packages "$tmp/some"); then ok "D3sk.* reference found"; else bad "D3sk.* reference found"; fi

[ "$failures" = 0 ] || { echo "test-sdk-env: $failures failure(s)" >&2; exit 1; }
echo "test-sdk-env: all passed"
