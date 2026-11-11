#!/usr/bin/env bash
# Run an autotest script in a headless test instance of the game on the PC and print the result.
# Usage: autotest.sh tests/autotest/<name>.txt [timeout-seconds]
# Takes a free test slot (t1..tN, N = HOT_CARGO_SLOTS, default 8), so several autotests run at the same time.
# Exit code 0 only if the log has AUTOTEST DONE and no AUTOTEST FAIL or ERROR line.
set -euo pipefail
SCRIPT="${1:?usage: autotest.sh <script> [timeout]}"
TIMEOUT="${2:-300}"
source "$(dirname "$0")/autotest-lib.sh"

SLOT="$(take_slot)"
trap 'release_slot "$SLOT"' EXIT
echo "== slot $SLOT"

start_slot "$SLOT" < "$SCRIPT"
LOG=""
for ((t = 0; t < TIMEOUT; t += 10)); do
  sleep 10
  LOG="$(slot_log "$SLOT")"
  if grep -q '<<GAME EXITED>>' <<<"$LOG"; then break; fi
done
grep -v '<<GAME EXITED>>' <<<"$LOG" || true
if log_passed <<<"$LOG"; then echo "== AUTOTEST OK (slot $SLOT)"; exit 0; fi
echo "== AUTOTEST FAILED (slot $SLOT; game log: SLOT=$SLOT scripts/logs.sh)"; exit 1
