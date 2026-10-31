#!/usr/bin/env bash
# The commit gate: format, lint (warnings are errors) and the unit tests.
# --full is reserved for slow tiers (none yet).
set -euo pipefail
cd "$(dirname "$0")"
export PATH="$HOME/.cargo/bin:$PATH"

cargo fmt --all -- --check
cargo clippy --all-targets -- -D warnings
cargo test --quiet
