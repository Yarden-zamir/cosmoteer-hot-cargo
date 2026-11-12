#!/usr/bin/env bash
# Run an autotest script in a headless test instance of the game on the PC and print the result.
# Usage: autotest.sh tests/autotest/<name>.txt [timeout-seconds]
# - Takes a free test slot (t1..tN, N = HOT_CARGO_SLOTS, default 6). Parallel runs work, also from one worktree.
# - Keeps the full log in $LOG_DIR (see autotest-lib.sh) and prints only the lines that explain a failure.
# - Retries a failed run once (AUTOTEST_RETRY, default 1). A pass on the retry prints "AUTOTEST FLAKY" and exits 0,
#   so timing problems and PC link errors do not block work. Limit: a real failure costs two runs.
# - Prints one "== timing" line per run with the seconds of each phase: wait (for a slot), deploy (stop, deploy, start),
#   start (game start to the first script line), script (to the end of the script), teardown (log fetch, stop, release).
# Exit code 0 only if a run has AUTOTEST DONE and no AUTOTEST FAIL or ERROR line. Exit code 3: the PC refused the calls
# because this agent's turn is not active (see LINK_REFUSED_TEXT in autotest-lib.sh); keep the turn open while tests run.
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
    t2=$SECONDS; STARTED_AT="" ENDED_AT=$SECONDS LINK_REFUSED=no
    release_slot "$SLOT"
  fi
  SLOT=""
  log="${out%%<<<VANILLA LOG>>>*}"
  { cat "$RUN_LOG.deploy"; echo "$log"; } > "$RUN_LOG"
  local ok=1
  # A pass needs this run's own log: the first script line is "telename <this test>". A stale log of another test or
  # worktree in the same slot never counts as a pass.
  if log_passed < "$RUN_LOG" && grep -qF "telename $NAME" "$RUN_LOG"; then ok=0; fi
  if [[ $ok -ne 0 && "$out" == *"<<<VANILLA LOG>>>"* ]]; then { echo; echo "=== vanilla game log"; echo "${out#*<<<VANILLA LOG>>>}"; } >> "$RUN_LOG"; fi
  t3=$SECONDS
  RUN_REFUSED=no
  if [[ "${LINK_REFUSED:-no}" == yes ]] || grep -q "$LINK_REFUSED_TEXT" "$RUN_LOG"; then RUN_REFUSED=yes; fi
  local started="${STARTED_AT:-$ENDED_AT}" upload=hit
  grep -q "uploaded build" "$RUN_LOG.deploy" && upload=uploaded
  rm -f "$RUN_LOG.deploy"
  local timing="== timing $NAME wait=$((t1 - t0)) deploy=$((t2 - t1)) start=$((started - t2)) script=$((ENDED_AT - started)) teardown=$((t3 - ENDED_AT)) total=$((t3 - t0)) build=$upload"
  echo "$timing" | tee -a "$RUN_LOG"
  return $ok
}

refused() { echo "== AUTOTEST LINK REFUSED $NAME: the PC refused the calls, because this agent's turn is not active. Keep the turn open while tests run (log $RUN_LOG)"; exit 3; }
if run_once; then echo "== AUTOTEST OK $NAME (slot $RUN_SLOT)"; exit 0; fi
[[ "$RUN_REFUSED" == yes ]] && refused
FIRST_LOG="$RUN_LOG"
echo "== first run failed:"
failure_lines "$FIRST_LOG"
if [[ "$RETRY" -ge 1 ]] && run_once; then
  echo "== AUTOTEST FLAKY $NAME (failed once, passed on retry; first log $FIRST_LOG)"
  echo "$(date +%F\ %T) $NAME $FIRST_LOG" >> "$STATE_DIR/flaky.log"
  exit 0
fi
[[ "$RUN_REFUSED" == yes ]] && refused
[[ "$RUN_LOG" != "$FIRST_LOG" ]] && { echo "== retry failed:"; failure_lines "$RUN_LOG"; }
echo "== AUTOTEST FAILED $NAME (log $RUN_LOG)"
exit 1
