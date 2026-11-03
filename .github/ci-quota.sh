#!/usr/bin/env bash
# Daily CI quota, run by the first job of each workflow: GitHub-hosted macOS runners are the
# scarce ones, so their jobs are capped per UTC day across all workflows, and each workflow
# has a cap on its own runs.
#
# Env: GITHUB_REPOSITORY, GH_TOKEN (actions: read), WORKFLOW (file name, e.g. release.yml),
#      CURRENT_RUN_ID (left out of the counts), MAX_RUNS (default 4), MAX_MACOS (default 2),
#      FORCE=true skips both caps. Repo variables CI_MAX_RUNS_PER_DAY / CI_MAX_MACOS_PER_DAY
#      feed MAX_RUNS / MAX_MACOS: `gh variable set CI_MAX_MACOS_PER_DAY --body 3`.
# Writes `macos=true|false` to $GITHUB_OUTPUT (stdout when unset). Exits 1 when this workflow
# already used its runs today.
set -euo pipefail

: "${GITHUB_REPOSITORY:?}" "${WORKFLOW:?}"
max_runs=${MAX_RUNS:-4}
max_macos=${MAX_MACOS:-2}
current=${CURRENT_RUN_ID:-0}
day=${QUOTA_DAY:-$(date -u +%F)}  # QUOTA_DAY: count from another day (tests)
out=${GITHUB_OUTPUT:-/dev/stdout}

# Every run created today, any workflow: "<run id> <workflow file>".
runs=$(gh api --paginate "repos/$GITHUB_REPOSITORY/actions/runs?created=$day&per_page=100" \
  --jq '.workflow_runs[] | "\(.id) \(.path | split("/") | last)"')

workflow_runs=0
macos_jobs=0
while read -r id file; do
  [[ -z "$id" || "$id" == "$current" ]] && continue
  # Jobs that did (or will) take a runner: not skipped. The quota job itself never counts.
  jobs=$(gh api "repos/$GITHUB_REPOSITORY/actions/runs/$id/jobs?per_page=100" \
    --jq '[.jobs[] | select(.name != "quota" and .conclusion != "skipped")]')
  macos_jobs=$((macos_jobs + $(jq '[.[] | select(any(.labels[]; test("macos")))] | length' <<<"$jobs")))
  if [[ "$file" == "$WORKFLOW" ]] && (($(jq length <<<"$jobs") > 0)); then
    workflow_runs=$((workflow_runs + 1))
  fi
done <<<"$runs"

echo "today ($day UTC): $WORKFLOW ran $workflow_runs/$max_runs times; macOS jobs: $macos_jobs/$max_macos"

if [[ "${FORCE:-false}" == "true" ]]; then
  echo "force: quota skipped"
  echo "macos=true" >>"$out"
  exit 0
fi
if ((workflow_runs >= max_runs)); then
  echo "::error::$WORKFLOW already ran $workflow_runs times today (cap $max_runs, CI_MAX_RUNS_PER_DAY). Run it again tomorrow, or with -f force=true."
  exit 1
fi
if ((macos_jobs >= max_macos)); then
  echo "::warning::macOS daily cap reached ($macos_jobs/$max_macos, CI_MAX_MACOS_PER_DAY): no macOS job in this run."
  echo "macos=false" >>"$out"
else
  echo "macos=true" >>"$out"
fi
