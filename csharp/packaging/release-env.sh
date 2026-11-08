# Sourced by release.sh and github-release.sh (not run on its own). Needs ROOT (the repo root).
# d3sk_load_env: the optional, git-ignored .env (same file as csharp/check.sh).
# d3sk_release_cmd: sets RELEASE to the d3sk-release command: D3SK_RELEASE when set (test seam), else
# dotnet tool run d3sk-release after finding dotnet and restoring the private tool.

d3sk_load_env() {
  if [ -f "$ROOT/.env" ]; then
    set -a
    # shellcheck disable=SC1091
    . "$ROOT/.env"
    set +a
  fi
}

d3sk_release_cmd() {
  if [ -z "${D3SK_RELEASE:-}" ]; then
    if ! command -v dotnet >/dev/null 2>&1; then
      if [ -n "${DOTNET_ROOT:-}" ] && [ -x "$DOTNET_ROOT/dotnet" ]; then
        export PATH="$DOTNET_ROOT:$PATH"
      else
        echo "ERROR: dotnet not found: install the .NET 10 SDK or set DOTNET_ROOT" >&2
        exit 1
      fi
    fi
    # The tool is a private D3sk.* package: its restore needs the read:packages credential (never printed).
    # shellcheck disable=SC1091
    . "$ROOT/csharp/packaging/sdk-env.sh"
    d3sk_packages_credentials || exit 1
    dotnet tool restore >&2
    RELEASE=(dotnet tool run d3sk-release)
  else
    # shellcheck disable=SC2206
    RELEASE=($D3SK_RELEASE)
  fi
}
