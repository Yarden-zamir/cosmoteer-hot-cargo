#!/usr/bin/env bash
# Wait for a detached run (scripts/detach.sh, autotest-all.sh --detach, integrate.sh --detach) and print its end.
# Usage: suite-wait.sh <log> [timeout-seconds, default 14400]
# Exit code: the run's exit code; 3 when the run died without an exit line; 124 on timeout (the run keeps running).
# Killing this script never stops the run. Run it again to keep waiting.
set -uo pipefail
LOG="${1:?usage: suite-wait.sh <log> [timeout]}"
TIMEOUT="${2:-14400}"
[[ -f "$LOG" ]] || { echo "suite-wait: no log $LOG"; exit 2; }
PID=""
while (( SECONDS < TIMEOUT )); do
  [[ -z "$PID" ]] && PID="$(head -1 "$LOG" | cut -s -d' ' -f4)"
  END="$(grep '^== suite exit ' "$LOG" | tail -1)"
  if [[ -n "$END" ]]; then tail -60 "$LOG"; exit "${END##* }"; fi
  if [[ -n "$PID" ]] && ! kill -0 "$PID" 2>/dev/null; then
    tail -30 "$LOG"; echo "suite-wait: the run (pid $PID) died without an exit line"; exit 3
  fi
  sleep 5
done
echo "suite-wait: still running after ${TIMEOUT}s ($LOG)"; exit 124
