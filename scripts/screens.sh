#!/usr/bin/env bash
# Run a screenshot script (tests/screens/<name>.txt) in a test slot with graphics, and copy the PNGs to this Mac.
# Usage: [SLOT=tN] screens.sh <script> <out-dir> [timeout-seconds, default 300]
# The slot: SLOT, or a free test slot (the test slot locks). Never main: the player's game stays untouched.
# The game starts with a window, minimized, with the slot's own save folder (--rootsavepath, scripts/pc.sh SLOT_WINDOWED).
# While <container>/.state/player-hold exists it refuses (exit 3): a window could take focus or GPU time from the player.
# It deploys this worktree, runs the script until it quits the game, prints the log lines and copies the screenshots.
set -euo pipefail
SCRIPT="${1:?usage: screens.sh <script> <out-dir> [timeout]}"
OUT="${2:?usage: screens.sh <script> <out-dir> [timeout]}"
TIMEOUT="${3:-300}"
source "$(dirname "$0")/autotest-lib.sh"
[[ "${SLOT:-}" != main ]] || { echo "screens: never slot main (the player's game); use a test slot" >&2; exit 2; }
export SLOT_WINDOWED=1
[[ -e "$STATE_DIR/player-hold" ]] && { echo "screens: the player plays (.state/player-hold); no windowed test slot now" >&2; exit 3; }
if [[ -n "${SLOT:-}" ]]; then
  until try_lock "$(slot_lock "$SLOT")"; do clean_stale_locks "$(slot_lock "$SLOT")"; echo "screens: slot $SLOT busy; waiting" >&2; sleep 15; done
else
  SLOT="$(take_slot)"
fi
export SLOT
trap 'release_slot "$SLOT"' EXIT
echo "== screens in slot $SLOT (windowed, minimized)"

source "$LIB_DIR/pc.sh"
pc "$PS_PATHS"'$d = Join-Path $modDir "screenshots"; if (Test-Path $d) { Remove-Item $d -Recurse -Force }' </dev/null >/dev/null 2>&1 || true
# Deploy and start (deploy.sh autotest mode). A test slot turns the mod on in its own settings.rules.
{ echo "telename $(basename "$SCRIPT" .txt)"; cat "$SCRIPT"; } | start_slot "$SLOT"
LOG=""
for ((t = 0; t < TIMEOUT; t += 10)); do
  sleep 10
  LOG="$(slot_log "$SLOT")"
  if grep -q '<<GAME EXITED>>' <<<"$LOG"; then break; fi
done
grep -E 'AUTOTEST (FAIL|DONE|PASS)|ux: screenshot' <<<"$LOG" || true
grep -A3 ' ERROR ' <<<"$LOG" || true
mkdir -p "$OUT"
echo "$LOG" > "$OUT/$(basename "$SCRIPT" .txt).log"
pc "$PS_PATHS"'$d = Join-Path $modDir "screenshots"; if (Test-Path $d) { tar.exe -cf - -C $d . }' </dev/null | tar -xf - -C "$OUT"
ls "$OUT"
log_passed <<<"$LOG"
