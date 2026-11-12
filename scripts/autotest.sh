#!/usr/bin/env bash
# Run an autotest script in a headless test instance of the game on the PC and print the result.
# Usage: autotest.sh tests/autotest/<name>.txt [timeout-seconds]. A "# timeout: N" line in the script raises the timeout to N.
# - Takes a free test slot (t1..tN, N = HOT_CARGO_SLOTS, default 8). Parallel runs work, also from one worktree.
# - Keeps the full log in $LOG_DIR (see autotest-lib.sh) and prints only the lines that explain a failure.
# - Retries a failed run once (AUTOTEST_RETRY, default 1). A pass on the retry prints "AUTOTEST FLAKY" and exits 0,
#   so timing problems and PC link errors do not block work. Limit: a real failure costs two runs.
# Exit code 0 only if a run has AUTOTEST DONE and no AUTOTEST FAIL or ERROR line.
set -euo pipefail
SCRIPT="${1:?usage: autotest.sh <script> [timeout]}"
TIMEOUT="${2:-300}"
# A script can ask for more time with a "# timeout: <seconds>" line (long playthroughs). The larger value wins.
SCRIPT_TIMEOUT="$(grep -m1 '^# timeout:' "$SCRIPT" | cut -d: -f2 | tr -d ' ' || true)"
[[ -n "$SCRIPT_TIMEOUT" && "$SCRIPT_TIMEOUT" -gt "$TIMEOUT" ]] && TIMEOUT="$SCRIPT_TIMEOUT"
RETRY="${AUTOTEST_RETRY:-1}"
source "$(dirname "$0")/autotest-lib.sh"
NAME="$(basename "$SCRIPT" .txt)"

SLOT=""
trap '[[ -n "$SLOT" ]] && release_slot "$SLOT"' EXIT

# One run. Sets RUN_LOG and RUN_SLOT; returns 0 on a pass.
run_once() {
  SLOT="$(take_slot)"
  RUN_SLOT="$SLOT"
  RUN_LOG="$LOG_DIR/$NAME.$(date +%Y%m%d-%H%M%S).$SLOT.log"
  echo "== $NAME: slot $SLOT, log $RUN_LOG"
  # telename labels the telemetry of this test with the script name (TELEMETRY.md).
  { echo "telename $NAME"; cat "$SCRIPT"; } | start_slot "$SLOT" > "$RUN_LOG.deploy" 2>&1 || true
  local log="" t
  for ((t = 0; t < TIMEOUT; t += 10)); do
    sleep 10
    log="$(slot_log "$SLOT" 2>&1 || true)"
    if grep -q '<<GAME EXITED>>' <<<"$log"; then break; fi
  done
  { cat "$RUN_LOG.deploy"; grep -v '<<GAME EXITED>>' <<<"$log" || true; } > "$RUN_LOG"
  rm -f "$RUN_LOG.deploy"
  local ok=1
  log_passed < "$RUN_LOG" && ok=0
  if [[ $ok -ne 0 ]]; then { echo; echo "=== vanilla game log"; slot_game_log "$SLOT"; } >> "$RUN_LOG"; fi
  release_slot "$SLOT"
  SLOT=""
  return $ok
}

if run_once; then echo "== AUTOTEST OK $NAME (slot $RUN_SLOT)"; exit 0; fi
FIRST_LOG="$RUN_LOG"
echo "== first run failed:"
failure_lines "$FIRST_LOG"
if [[ "$RETRY" -ge 1 ]] && run_once; then
  echo "== AUTOTEST FLAKY $NAME (failed once, passed on retry; first log $FIRST_LOG)"
  echo "$(date +%F\ %T) $NAME $FIRST_LOG" >> "$STATE_DIR/flaky.log"
  exit 0
fi
[[ "$RUN_LOG" != "$FIRST_LOG" ]] && { echo "== retry failed:"; failure_lines "$RUN_LOG"; }
echo "== AUTOTEST FAILED $NAME (log $RUN_LOG)"
exit 1
