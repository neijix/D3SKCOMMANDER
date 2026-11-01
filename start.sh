#!/usr/bin/env bash
# Build (release, incremental) and start d3skcommander. Extra arguments go to the app.
set -euo pipefail
cd "$(dirname "$0")"
# Launched from a desktop menu, PATH may miss rustup's default bin folder.
command -v cargo >/dev/null || export PATH="${CARGO_HOME:-$HOME/.cargo}/bin:$PATH"
cargo build --release
exec ./target/release/d3skcommander "$@"
