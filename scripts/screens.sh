#!/usr/bin/env bash
# Run a screenshot script (tests/screens/<name>.txt) in the player's game (slot main) and copy the PNGs to this Mac.
# Usage: screens.sh <script> <out-dir> [timeout-seconds, default 300]
# Only the main slot has graphics. The script refuses while the player's game runs, deploys this worktree,
# starts the game minimized, waits until the script quits the game, prints the log lines and copies the screenshots.
# The lock /tmp/hot_cargo_main_slot.lock makes main-slot users on this Mac take turns.
set -euo pipefail
SCRIPT="${1:?usage: screens.sh <script> <out-dir> [timeout]}"
OUT="${2:?usage: screens.sh <script> <out-dir> [timeout]}"
TIMEOUT="${3:-300}"
source "$(dirname "$0")/autotest-lib.sh"
export SLOT=main
LOCK=/tmp/hot_cargo_main_slot.lock

until mkdir "$LOCK" 2>/dev/null; do
  if [[ -f "$LOCK/pid" ]] && ! kill -0 "$(cat "$LOCK/pid")" 2>/dev/null; then rm -rf "$LOCK"; continue; fi
  echo "main slot busy; waiting" >&2
  sleep 15
done
echo $$ > "$LOCK/pid"
trap 'rm -rf "$LOCK"' EXIT

source "$LIB_DIR/pc.sh"
# Never touch the player's game: when it runs (or the PC call fails), stop here. Retry when the player closes it.
if ! STATE="$(pc "$PS_PATHS"'if (Get-SlotProcess) { "running" } else { "closed" }' </dev/null 2>&1)" || [[ "$STATE" != *closed* ]]; then
  echo "screens: the player's game is running (or the PC did not answer); no screenshots taken" >&2
  exit 2
fi
# From here the game in the main slot is ours, so stop it on exit.
trap '"$LIB_DIR/stop.sh" >/dev/null 2>&1 || true; rm -rf "$LOCK"' EXIT
"$LIB_DIR/mod-toggle.sh" on </dev/null | tail -1
pc "$PS_PATHS"'$d = Join-Path $modDir "screenshots"; if (Test-Path $d) { Remove-Item $d -Recurse -Force }' </dev/null
start_slot main < "$SCRIPT"
LOG=""
for ((t = 0; t < TIMEOUT; t += 10)); do
  sleep 10
  LOG="$(slot_log main)"
  if grep -q '<<GAME EXITED>>' <<<"$LOG"; then break; fi
done
grep -E 'AUTOTEST (FAIL|DONE|PASS)|ux: screenshot' <<<"$LOG" || true
grep -A3 ' ERROR ' <<<"$LOG" || true
mkdir -p "$OUT"
echo "$LOG" > "$OUT/$(basename "$SCRIPT" .txt).log"
pc "$PS_PATHS"'$d = Join-Path $modDir "screenshots"; if (Test-Path $d) { tar.exe -cf - -C $d . }' </dev/null | tar -xf - -C "$OUT"
ls "$OUT"
log_passed <<<"$LOG"
