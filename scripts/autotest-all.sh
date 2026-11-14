#!/usr/bin/env bash
# Run autotests in parallel on the free test slots and print a summary.
# Usage: autotest-all.sh [--detach] [--telemetry] [timeout-per-test] [test ...]
# --detach: run in the background, safe from a killed shell (scripts/detach.sh). Prints the log path; wait with
#   scripts/suite-wait.sh <log>.
# --telemetry: after the tests, copy the telemetry of every slot to ../.telemetry (scripts/pull-telemetry.sh) and write a report
#   of the runs that these tests made (tools/telemetry). The report path is printed at the end.
# - No test names: every tests/autotest/*.txt and every co-op session (mp:<session>, see coop_sessions in autotest-lib.sh).
# - Test names: only those, for example "tech-sell mp:coop-economy" (autotest-affected.sh passes its list this way).
#   "mp3:<session>" runs a session with 3 players (the host and 2 clients, 3 slots).
# Each test retries once (see autotest.sh); a pass on the retry counts as FLAKY, not as a failure.
# The suite builds and stages the mod once (scripts/stage.sh) and every test deploys that stage (HOT_CARGO_STAGE),
# so all tests run the same build. Changes made in the worktree during the suite are not in it.
# At the end it prints the seconds per phase over all runs (see autotest.sh), so the slowest phase is visible.
# Exit code 0 only if every test passed. Full logs: <container>/.state/logs/<worktree>/ (summary per test in run/).
set -uo pipefail
cd "$(dirname "$0")/.."
if [[ "${1:-}" == "--detach" ]]; then
  shift
  LOG="$(scripts/detach.sh "autotest-all-$(basename "$PWD")" scripts/autotest-all.sh "$@")"
  echo "== suite runs detached. Log: $LOG"
  echo "== wait for it: scripts/suite-wait.sh $LOG"
  exit 0
fi
TELEMETRY=no
if [[ "${1:-}" == "--telemetry" ]]; then TELEMETRY=yes; shift; fi
STARTED_UTC="$(date -u +%Y-%m-%dT%H:%M:%S)"
TIMEOUT="${1:-400}"
shift || true
source scripts/autotest-lib.sh
SUITE_START=$SECONDS
RUN_DIR="$LOG_DIR/run-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$RUN_DIR"
# A clean tree that stays on the same commit gets a pass marker, which scripts/release.sh requires (full runs only).
START_SHA="$(git rev-parse HEAD)"
CLEAN_AT_START="$([[ -z "$(git status --porcelain)" && $# -eq 0 ]] && echo yes || echo no)"

TESTS=()
if [[ $# -gt 0 ]]; then TESTS=("$@"); else
  for t in tests/autotest/*.txt; do TESTS+=("$(basename "$t" .txt)"); done
  for n in $(coop_sessions); do TESTS+=("mp:$n"); done
fi

# Build and stage once before the parallel runs. The PC caches the build by hash, so only the first deploy uploads it.
export HOT_CARGO_STAGE="$RUN_DIR/stage"
scripts/stage.sh "$HOT_CARGO_STAGE" > "$RUN_DIR/build.log" 2>&1 || { cat "$RUN_DIR/build.log"; echo "== build failed"; exit 1; }

run_test() {
  local t="$1" out="$RUN_DIR/$1.out"
  if [[ "$t" == mp:* || "$t" == mp3:* ]]; then
    local n="${t#*:}" players=2
    [[ "$t" == mp3:* ]] && players=3
    # Twice the test timeout: session waits count game seconds (requirement 1007), and a loaded PC runs the sim slower.
    PLAYERS=$players scripts/autotest-mp.sh "tests/autotest-mp/$n.txt" "$((TIMEOUT * 2 + 200))" > "$out" 2>&1
  else
    scripts/autotest.sh "tests/autotest/$t.txt" "$TIMEOUT" > "$out" 2>&1
  fi
  local rc=$?
  # Exit code 3: the PC refuses calls (this agent's turn is not active). Later tests would only hold slots for nothing.
  if [[ $rc -eq 3 ]]; then touch "$RUN_DIR/link-refused"; fi
  if [[ $rc -ne 0 ]]; then echo "FAIL $t"; elif grep -q "AUTOTEST FLAKY" "$out"; then echo "FLAKY $t"; else echo "PASS $t"; fi
}

# At most SLOTS tests wait for slots at the same time; take_slots hands out the slots.
{
  for t in "${TESTS[@]}"; do
    while [[ $(jobs -rp | wc -l) -ge $SLOTS ]]; do sleep 2; done
    if [[ -f "$RUN_DIR/link-refused" ]]; then echo "FAIL $t"; continue; fi
    run_test "$t" &
  done
  wait
} | tee "$RUN_DIR/summary.txt"

# Seconds per phase over every run of this suite (a retry is a run of its own).
phase_times() {
  local lines vals n p
  lines="$(cat "$RUN_DIR"/*.out 2>/dev/null | grep '^== timing ' || true)"
  [[ -n "$lines" ]] || return 0
  echo "== phase seconds over $(grep -c . <<<"$lines") runs, suite wall $((SECONDS - SUITE_START)) s:"
  for p in wait deploy start script teardown total; do
    vals="$(tr ' ' '\n' <<<"$lines" | grep "^$p=" | cut -d= -f2 | sort -n)"
    [[ -n "$vals" ]] || continue
    n="$(grep -c . <<<"$vals")"
    printf '   %-9s sum %6d   median %4d   max %4d\n' "$p" "$(paste -sd+ - <<<"$vals" | bc)" "$(sed -n "$(((n + 1) / 2))p" <<<"$vals")" "$(tail -1 <<<"$vals")"
  done
  echo "   build uploads: $(grep -c 'build=uploaded' <<<"$lines") of $(grep -c 'build=' <<<"$lines") deploys"
}
phase_times | tee "$RUN_DIR/phases.txt"

if [[ "$TELEMETRY" == yes ]]; then
  scripts/pull-telemetry.sh >/dev/null && (cd tools/telemetry && uv run hc-telemetry --since "$STARTED_UTC") || echo "== telemetry report failed"
fi
if [[ -f "$RUN_DIR/link-refused" ]]; then
  echo "== stopped: the PC refused calls, because this agent's turn was not active. Tests after that did not run (FAIL)."
  echo "== keep the turn open while a suite runs: --detach, then scripts/suite-wait.sh <log> with t3_background_wait."
fi
FAILED="$(grep '^FAIL ' "$RUN_DIR/summary.txt" | cut -d' ' -f2- | tr '\n' ' ')"
FLAKY="$(grep '^FLAKY ' "$RUN_DIR/summary.txt" | cut -d' ' -f2- | tr '\n' ' ')"
[[ -n "$FLAKY" ]] && echo "== flaky (passed on retry): $FLAKY"
if [[ -z "$FAILED" ]]; then
  echo "== all $(grep -c . "$RUN_DIR/summary.txt") autotests passed ($RUN_DIR)"
  if [[ "$CLEAN_AT_START" == yes && "$(git rev-parse HEAD)" == "$START_SHA" && -z "$(git status --porcelain)" ]]; then
    touch "$STATE_DIR/autotest_pass_$START_SHA"; echo "== pass marker written for $START_SHA"
  fi
  exit 0
fi
# A co-op session prints its step lines and one result line with the reason (join failed, steps failed, desync, PC link).
for t in $FAILED; do
  echo "--- $t"
  if [[ "$t" == mp:* || "$t" == mp3:* ]]; then grep -E "^FAIL |^== result|^   " "$RUN_DIR/$t.out" | head -40
  else grep -A60 "first run failed\|retry failed\|MP AUTOTEST FAILED" "$RUN_DIR/$t.out" | head -40; fi
done
echo "== failed: $FAILED ($RUN_DIR)"; exit 1
