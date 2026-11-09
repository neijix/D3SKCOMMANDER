#!/usr/bin/env bash
# Puts the release signing key back from its Bitwarden backup into this checkout's gitignored .release-keys/
# (the folder ./release.sh reads by default), readable by this user only. Owner's machine only; optional for anyone
# else, who sets D3SKCOMMANDER_RELEASE_KEY_ITEM to their own secure note or D3SKCOMMANDER_RELEASE_KEY_FILE to their own key.
#   restore-release-key.sh
# Needs an unlocked Bitwarden CLI (BW_SESSION exported, e.g. eval "$(bw-auto unlock --export)"). Never overwrites a
# key, never prints it; a note that is not an EC private key leaves no file behind.
# Seams: BW (default bw), OPENSSL (default openssl), D3SKCOMMANDER_RELEASE_KEY_HOME (the folder, default .release-keys).
set -euo pipefail
set +x
here="$(cd "$(dirname "$0")" && pwd)"
repo_root="$(cd "$here/../.." && pwd)"
bw_cmd="${BW:-bw}"
openssl_cmd="${OPENSSL:-openssl}"
item="${D3SKCOMMANDER_RELEASE_KEY_ITEM:-d3skcommander-release-signing-key}"
dir="${D3SKCOMMANDER_RELEASE_KEY_HOME:-$repo_root/.release-keys}"
key="$dir/release-signing-key.pem"

fail() { echo "restore-release-key: $*" >&2; exit 1; }
[ ! -e "$key" ] || fail "$key already exists: nothing restored"
mkdir -p "$dir"
tmp_key="$dir/.restoring-$$.pem"
trap 'rm -f "$tmp_key"' EXIT
(
  umask 077
  "$bw_cmd" get notes "$item" > "$tmp_key"
) || fail "Bitwarden has no readable note '$item' (unlocked? item name?)"
case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*)
    me="$(/c/Windows/System32/whoami.exe | tr -d '\r')"
    MSYS2_ARG_CONV_EXCL='*' icacls "$(cygpath -w "$tmp_key")" /inheritance:r /grant:r "$me:F" >/dev/null \
      || fail "cannot restrict the ACL of $tmp_key" ;;
  *) chmod 600 "$tmp_key" ;;
esac
"$openssl_cmd" ec -in "$tmp_key" -noout >/dev/null 2>&1 || fail "the note '$item' is not an EC private key"
mv "$tmp_key" "$key"
trap - EXIT
echo "restore-release-key: restored $key (readable by you only)"
