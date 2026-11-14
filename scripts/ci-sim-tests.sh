#!/usr/bin/env bash
# One CI shard of the game tests in the headless sim (.github/workflows/check.yml, job game-tests-shard).
# Usage: ci-sim-tests.sh <shard> <shards> [timeout-per-test]   (shard counts from 1)
# The tests: every tests/autotest/*.txt and co-op session except those with the line "# runner: pc" (they need the PC; this
# prints them, so none is skipped silently). Each co-op session gets a shard of its own (it runs two real-time processes);
# the single-player tests go round-robin, sorted by name, over the other shards.
# Writes the shard's results to $GITHUB_STEP_SUMMARY when it is set. Exit code 0 only when every test of the shard passed.
set -uo pipefail
cd "$(dirname "$0")/.."
source scripts/autotest-lib.sh
SHARD="${1:?usage: ci-sim-tests.sh <shard> <shards> [timeout]}" SHARDS="${2:?usage: ci-sim-tests.sh <shard> <shards> [timeout]}"
TIMEOUT="${3:-400}"
SESSIONS=() SINGLES=() PC=()
for n in $(coop_sessions); do
  if grep -qx '# runner: pc' "tests/autotest-mp/$n.txt"; then PC+=("mp:$n"); else SESSIONS+=("mp:$n"); fi
done
for f in tests/autotest/*.txt; do
  t="$(basename "$f" .txt)"
  if grep -qx '# runner: pc' "$f"; then PC+=("$t"); else SINGLES+=("$t"); fi
done
(( SHARDS > ${#SESSIONS[@]} )) || { echo "ci-sim-tests: need more shards than the ${#SESSIONS[@]} co-op sessions"; exit 2; }
MINE=()
if (( SHARD <= ${#SESSIONS[@]} )); then MINE=("${SESSIONS[$((SHARD - 1))]}")
else
  rest=$((SHARDS - ${#SESSIONS[@]})) k=$((SHARD - ${#SESSIONS[@]} - 1))
  for i in "${!SINGLES[@]}"; do (( i % rest == k )) && MINE+=("${SINGLES[$i]}"); done
fi
echo "== shard $SHARD of $SHARDS: ${MINE[*]:-none}"
echo "== not in CI (# runner: pc, run on the PC): ${PC[*]:-none}"
[[ ${#MINE[@]} -gt 0 ]] || exit 0
OUT="$(mktemp)"
scripts/autotest-sim.sh "$TIMEOUT" "${MINE[@]}" 2>&1 | tee "$OUT"
RC=${PIPESTATUS[0]}
if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
  { echo "### Game tests, shard $SHARD of $SHARDS"; echo; grep -E '^(PASS|FAIL|FLAKY) ' "$OUT" | sed 's/^/- /'
    grep -E '^== (failed|flaky)|^== result: FAIL' "$OUT" | sed 's/^/- /'; } >> "$GITHUB_STEP_SUMMARY"
fi
exit "$RC"
