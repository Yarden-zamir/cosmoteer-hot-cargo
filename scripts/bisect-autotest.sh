#!/usr/bin/env bash
# Find the first commit where an autotest fails, with git bisect in a scratch worktree.
# Usage: bisect-autotest.sh [--first-parent] <test name> <good commit> [bad commit, default HEAD] [timeout]
# --first-parent: test only the merge commits of main, so the answer is the merge that brought the failure (scripts/nightly.sh).
# BISECT_RUNNER=mac runs each step in the Mac sim (scripts/autotest-mac.sh, no PC); default pc (scripts/autotest.sh).
# Every step runs the test script from the bad commit (copied once), so the test itself stays the same.
# Retries are off: a flaky test gives wrong answers, so check the result with a second run of that commit.
set -euo pipefail
cd "$(dirname "$0")/.."
FIRST_PARENT=()
if [[ "${1:-}" == --first-parent ]]; then FIRST_PARENT=(--first-parent); shift; fi
NAME="${1:?usage: bisect-autotest.sh [--first-parent] <test> <good> [bad] [timeout]}"
GOOD="${2:?usage: bisect-autotest.sh [--first-parent] <test> <good> [bad] [timeout]}"
BAD="$(git rev-parse "${3:-HEAD}")"
TIMEOUT="${4:-300}"
RUNNER="${BISECT_RUNNER:-pc}"
WT="$(cd .. && pwd)/bisect"
[[ -e "$WT" ]] && { echo "bisect: $WT exists; remove it first (git worktree remove $WT)"; exit 1; }
TEST_COPY="$(mktemp -d)/$NAME.txt"
git show "$BAD:tests/autotest/$NAME.txt" > "$TEST_COPY"
git worktree add -q --detach "$WT" "$BAD"
trap 'git -C "$WT" bisect reset -q 2>/dev/null || true; git worktree remove --force "$WT"' EXIT
cd "$WT"
git bisect start -q "${FIRST_PARENT[@]}" "$BAD" "$GOOD"
# Exit 125 (skip) when the commit does not build, so a broken commit does not count as the culprit.
if [[ "$RUNNER" == mac ]]; then
  STEP="cp '$TEST_COPY' tests/autotest/$NAME.txt && MAC_RETRY=0 scripts/autotest-mac.sh $TIMEOUT $NAME | tail -1; rc=\${PIPESTATUS[0]}; git checkout -q -- tests/autotest/$NAME.txt 2>/dev/null || rm -f tests/autotest/$NAME.txt; exit \$rc"
else
  STEP="AUTOTEST_RETRY=0 scripts/autotest.sh '$TEST_COPY' $TIMEOUT | tail -1"
fi
git bisect run bash -c "set -o pipefail; dotnet build src/HotCargo/HotCargo.csproj -c Release -nologo -v quiet >/dev/null 2>&1 || exit 125; $STEP"
git bisect log | tail -3
