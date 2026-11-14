#!/usr/bin/env bash
# Game tests for a pull request, as a GitHub commit status. GitHub's own checks (build, private-data) run no game tests;
# this posts the status "game-tests" on the PR head, so auto-merge waits for it once it is a required check.
# Usage: game-check.sh [PR number]   (default: the PR of the current branch)
# 1. A scratch worktree <container>/check-<PR> at the PR head, with current origin/main merged in (nothing is pushed): the
#    tests run on what main will look like after the merge.
# 2. scripts/autotest-affected.sh on that merge, every test of the touched areas (AUTOTEST_TIER=full, like integrate.sh).
# 3. The status "game-tests" on the PR head SHA: pending while it runs, then success or failure with a short reason and the
#    log path (<container>/.state/game-check/).
# PC calls need an active turn: run it as a background Bash call and wait with t3_background_wait.
# The owner flow (CLAUDE.md): push the branch, run this, then "gh pr merge --auto --merge". When main moves, merge main into
# the branch, adapt, push, and run this again on the new head.
set -uo pipefail
cd "$(dirname "$0")/.."
source scripts/autotest-lib.sh
PR="${1:-$(gh pr view --json number -q .number 2>/dev/null)}"
[[ -n "$PR" ]] || { echo "game-check: no PR for this branch; name one"; exit 2; }
read -r SHA HEAD_REF < <(gh pr view "$PR" --json headRefOid,headRefName -q '.headRefOid + " " + .headRefName')
[[ -n "$SHA" ]] || { echo "game-check: PR $PR not found"; exit 2; }
REPO="$(gh repo view --json nameWithOwner -q .nameWithOwner)"
OUT_DIR="$STATE_DIR/game-check"; mkdir -p "$OUT_DIR"
LOG="$OUT_DIR/pr$PR-${SHA:0:9}-$(date +%Y%m%d-%H%M%S).log"

# status <state> <description>: the "game-tests" status on the PR head (description at most 140 characters).
status() {
  gh api -X POST "repos/$REPO/statuses/$SHA" -f state="$1" -f context=game-tests -f description="${2:0:140}" > /dev/null \
    || echo "game-check: could not post the status ($1)" >&2
}

git fetch -q origin main "$HEAD_REF"
WT="$(cd .. && pwd)/check-$PR"
[[ -e "$WT" ]] && git worktree remove --force "$WT" 2>/dev/null
git worktree add -q --detach "$WT" "$SHA" || { echo "game-check: no worktree at $SHA"; exit 1; }
trap 'git worktree remove --force "$WT" 2>/dev/null || true' EXIT
status pending "game tests running on the PR head merged with origin/main"
T0=$SECONDS
cd "$WT"
if ! git merge -q --no-edit origin/main > /dev/null 2>&1; then
  status failure "conflicts with main: merge origin/main into the branch"
  echo "game-check: PR $PR conflicts with origin/main; merge main into the branch first"
  exit 1
fi
echo "== game-check PR $PR at ${SHA:0:9} with origin/main $(git rev-parse --short origin/main); log $LOG"
# LIST_ONLY from the caller would only list the tests and exit 0, which would post a false success.
env -u LIST_ONLY AUTOTEST_TIER=full COMMITTED_ONLY=1 scripts/autotest-affected.sh origin/main > "$LOG" 2>&1
RC=$?
SECS=$((SECONDS - T0))
tail -15 "$LOG"
if [[ $RC -eq 0 ]]; then
  status success "affected game tests passed in ${SECS} s ($(grep -m1 '^== affected:' "$LOG" | awk '{print NF - 2}') tests incl. core)"
  echo "== game-check: PASS, status game-tests=success on ${SHA:0:9}"
else
  FAILED="$(grep -m1 '^== failed: ' "$LOG" | sed 's/^== failed: //')"
  status failure "failed: ${FAILED:-see log} (log $(basename "$LOG"))"
  echo "== game-check: FAIL, status game-tests=failure on ${SHA:0:9}"
fi
exit $RC
