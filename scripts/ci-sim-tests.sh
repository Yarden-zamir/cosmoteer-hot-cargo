#!/usr/bin/env bash
# One CI shard of the game tests in the headless sim (.github/workflows/check.yml, job game-tests-shard).
# Usage: ci-sim-tests.sh <shard> <shards> [timeout-per-test]   (shard counts from 1)
# The tests: every tests/autotest/*.txt and co-op session except those with the line "# runner: pc" (they need the PC; this
# prints them, so none is skipped silently). The co-op sessions go one per shard in turn (a session takes 2 sim slots for
# its whole real-time run). The single-player tests, sorted by name, then go round-robin over all shards, starting after
# the last shard with a session, so that the shards without a session get the first extra test.
# Limit: the split counts tests, not their run times. If one shard often runs much longer than the others, revisit it.
# CI_TESTS, when set, limits the tests to these names (scripts/ci-test-set.sh, the set of the pull requests); unset runs all.
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
if [[ -n "${CI_TESTS+set}" ]]; then
  keep() { [[ " $CI_TESTS " == *" $1 "* ]]; }
  S=() T=()
  for t in "${SESSIONS[@]}"; do keep "$t" && S+=("$t"); done
  for t in "${SINGLES[@]}"; do keep "$t" && T+=("$t"); done
  SESSIONS=("${S[@]+"${S[@]}"}") SINGLES=("${T[@]+"${T[@]}"}")
fi
MINE=()
for i in "${!SESSIONS[@]}"; do (( i % SHARDS == SHARD - 1 )) && MINE+=("${SESSIONS[$i]}"); done
start=$(( ${#SESSIONS[@]} % SHARDS ))
for i in "${!SINGLES[@]}"; do (( (start + i) % SHARDS == SHARD - 1 )) && MINE+=("${SINGLES[$i]}"); done
echo "== runner: $(nproc) cores, $(awk '/^MemTotal:/ { printf "%.1f", $2 / 1048576 }' /proc/meminfo) GB, ${HOT_CARGO_SIM_JOBS:-default} sims at once"
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
