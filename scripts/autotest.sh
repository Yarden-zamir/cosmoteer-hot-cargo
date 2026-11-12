#!/usr/bin/env bash
# Run an autotest script in a headless test instance of the game on the PC and print the result.
# Usage: autotest.sh tests/autotest/<name>.txt [timeout-seconds]
# - Takes a free test slot (t1..tN, N = HOT_CARGO_SLOTS, default 8). Parallel runs work, also from one worktree.
# - Keeps the full log in $LOG_DIR (see autotest-lib.sh) and prints only the lines that explain a failure.
# - Retries a failed run once (AUTOTEST_RETRY, default 1). A pass on the retry prints "AUTOTEST FLAKY" and exits 0,
#   so timing problems and PC link errors do not block work. Limit: a real failure costs two runs.
# - Prints one "== timing" line per run with the seconds of each phase: wait (for a slot), deploy (stop, deploy, start),
#   start (game start to the first script line), script (to the end of the script), teardown (log fetch, stop, release).
# Exit code 0 only if a run has AUTOTEST DONE and no AUTOTEST FAIL or ERROR line.
set -euo pipefail
SCRIPT="${1:?usage: autotest.sh <script> [timeout]}"
TIMEOUT="${2:-300}"
RETRY="${AUTOTEST_RETRY:-1}"
source "$(dirname "$0")/autotest-lib.sh"
NAME="$(basename "$SCRIPT" .txt)"

SLOT=""
trap '[[ -n "$SLOT" ]] && release_slot "$SLOT"' EXIT

# One run. Sets RUN_LOG and RUN_SLOT; returns 0 on a pass.
run_once() {
  local t0=$SECONDS t1 t2 t3 out log
  SLOT="$(take_slot)"
  t1=$SECONDS
  RUN_SLOT="$SLOT"
  RUN_LOG="$LOG_DIR/$NAME.$(date +%Y%m%d-%H%M%S).$SLOT.log"
  echo "== $NAME: slot $SLOT, log $RUN_LOG"
  # telename labels the telemetry of this test with the script name (TELEMETRY.md).
  # A failed deploy started no game, and the slot can still hold the log of an older run: never read it.
  out=""
  if { echo "telename $NAME"; cat "$SCRIPT"; } | start_slot "$SLOT" > "$RUN_LOG.deploy" 2>&1; then
    t2=$SECONDS; wait_slot_end "$SLOT" "$TIMEOUT"
    # A failed PC call leaves an empty log, which never passes.
    if out="$(slot_finish "$SLOT" 2>&1)"; then rm -rf "$(slot_lock "$SLOT")"; else out=""; release_slot "$SLOT"; fi
  else
    t2=$SECONDS; STARTED_AT="" ENDED_AT=$SECONDS
    release_slot "$SLOT"
  fi
  SLOT=""
  log="${out%%<<<VANILLA LOG>>>*}"
  { cat "$RUN_LOG.deploy"; echo "$log"; } > "$RUN_LOG"
  local ok=1
  log_passed < "$RUN_LOG" && ok=0
  if [[ $ok -ne 0 && "$out" == *"<<<VANILLA LOG>>>"* ]]; then { echo; echo "=== vanilla game log"; echo "${out#*<<<VANILLA LOG>>>}"; } >> "$RUN_LOG"; fi
  t3=$SECONDS
  local started="${STARTED_AT:-$ENDED_AT}" upload=hit
  grep -q "uploaded build" "$RUN_LOG.deploy" && upload=uploaded
  rm -f "$RUN_LOG.deploy"
  local timing="== timing $NAME wait=$((t1 - t0)) deploy=$((t2 - t1)) start=$((started - t2)) script=$((ENDED_AT - started)) teardown=$((t3 - ENDED_AT)) total=$((t3 - t0)) build=$upload"
  echo "$timing" | tee -a "$RUN_LOG"
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
