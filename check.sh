#!/usr/bin/env bash
# The commit gate: the C# gate (format, build with warnings as errors, fast tests) and the
# code-health ratchet. --full adds the slow tiers (forwarded to csharp/check.sh).
set -euo pipefail
cd "$(dirname "$0")"

csharp/check.sh "$@"

# Size/complexity/copy ratchet (optional: needs the code-health skill and uv).
CH="$HOME/.agents/skills/code-health/scripts/code_health.py"
[ -f "$CH" ] || CH="$HOME/.claude/skills/code-health/scripts/code_health.py"
if [ -f "$CH" ] && command -v uv >/dev/null; then uv run -q "$CH" check; else echo "code-health: skill not installed, skipped"; fi
