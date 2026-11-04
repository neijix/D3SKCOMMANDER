#!/usr/bin/env bash
# The commit gate: format, lint (warnings are errors) and the unit tests.
# --full adds the slow tiers (forwarded to csharp/check.sh).
set -euo pipefail
cd "$(dirname "$0")"
export PATH="$HOME/.cargo/bin:$PATH"

cargo fmt --all -- --check
cargo clippy --all-targets -- -D warnings
cargo test --quiet

csharp/check.sh "$@"

# Size/complexity/copy ratchet (optional: needs the code-health skill and uv).
CH="$HOME/.agents/skills/code-health/scripts/code_health.py"
[ -f "$CH" ] || CH="$HOME/.claude/skills/code-health/scripts/code_health.py"
if [ -f "$CH" ] && command -v uv >/dev/null; then uv run -q "$CH" check; else echo "code-health: skill not installed, skipped"; fi
