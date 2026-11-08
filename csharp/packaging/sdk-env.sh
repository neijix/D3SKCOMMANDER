#!/usr/bin/env bash
# Sourced (never run) by csharp/check.sh and release.sh: the read:packages credential of the private
# D3sk.* NuGet source (root nuget.config, source "github"), for this process only. The token is
# never printed and never written to a file.
#
# d3sk_packages_credentials sets NuGetPackageSourceCredentials_github from the first of:
#   1. the variable NuGetPackageSourceCredentials_github, already set;
#   2. the Bitwarden item d3skhand-github-packages-read-token (folder DEV_KEYS), read with
#      $AI_CODE_ROOT/BitwardenAuto/bw-auto get-pass (the same token as D3SKHAND: one owner, one scope);
#   3. the file ~/.d3skcommander-release/github-packages-read-token (first line, mode 0600, else refused);
#   4. the token of gh, when it carries read:packages.
# The GitHub login comes from D3SKCOMMANDER_PACKAGES_USER, else the Bitwarden item's username, else
# `gh api user`. It returns 1, with a message naming the four ways, when none answers.

# d3sk_uses_private_packages <folder>: succeeds when a project or props file there references D3sk.*.
d3sk_uses_private_packages() {
  grep -rqs --include='*.csproj' --include='Directory.Packages.props' 'Include="D3sk\.' "$1"
}

d3sk_packages_credentials() {
  [ -z "${NuGetPackageSourceCredentials_github:-}" ] || return 0
  local entry=d3skhand-github-packages-read-token
  local bw="${AI_CODE_ROOT:-$HOME/Code}/BitwardenAuto/bw-auto"
  local file="$HOME/.d3skcommander-release/github-packages-read-token"
  local token="" login="${D3SKCOMMANDER_PACKAGES_USER:-}" note=""

  if [ -f "$bw" ] && token="$(bash "$bw" get-pass "$entry" 2>/dev/null)" && [ -n "$token" ]; then
    [ -n "$login" ] || login="$(bash "$bw" get "$entry" 2>/dev/null | sed -n 's/^Username : //p' | head -n 1)" || true
  else
    token=""
  fi

  if [ -z "$token" ] && [ -f "$file" ]; then
    # Windows: Git Bash cannot report or set the mode; the profile folder's NTFS ACL is the protection.
    if ! case "${OSTYPE:-}" in msys* | cygwin*) true ;; *) false ;; esac && [ "$(ls -ln "$file" | cut -c5-10)" != "------" ]; then
      note="$file is readable by others and was refused (chmod 600 it)"
    else
      token="$(head -n 1 "$file" | tr -d '[:space:]')"
    fi
  fi

  if [ -z "$token" ] && command -v gh >/dev/null 2>&1 \
     && gh auth status -h github.com 2>&1 | grep -Eq "(read|write):packages"; then
    token="$(gh auth token -h github.com 2>/dev/null)" || token=""
  fi

  if [ -n "$token" ] && [ -z "$login" ] && command -v gh >/dev/null 2>&1; then
    login="$(gh api user --jq .login 2>/dev/null || true)"
  fi

  if [ -n "$token" ] && [ -n "$login" ]; then
    export NuGetPackageSourceCredentials_github="Username=${login};Password=${token}"
    return 0
  fi

  {
    if [ -n "$token" ]; then
      echo "build token: a token was found but no GitHub login: set D3SKCOMMANDER_PACKAGES_USER=<login>"
    else
      echo "build token: the private D3sk.* packages need a GitHub Packages read token (read:packages). Any one of:"
      echo "  1. export NuGetPackageSourceCredentials_github=\"Username=<login>;Password=<token>\""
      echo "  2. the Bitwarden item $entry in the DEV_KEYS folder, read with bw-auto get-pass"
      echo "  3. the first line of $file (chmod 600)"
      echo "  4. gh auth refresh -h github.com -s read:packages"
      [ -z "$note" ] || echo "  note: $note"
    fi
  } >&2
  return 1
}
