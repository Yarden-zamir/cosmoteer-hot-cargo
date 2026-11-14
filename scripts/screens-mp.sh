#!/usr/bin/env bash
# Co-op screenshots: the host runs in a windowed test slot (graphics, minimized), the client in a headless test slot.
# Usage: screens-mp.sh <host script> <client script> <local output folder> [timeout-seconds]
# Never slot main. While <container>/.state/player-hold exists it refuses (exit 3), like scripts/screens.sh.
# "{PORT}" in both scripts becomes the host port (35870 + host slot number, as scripts/autotest-mp.sh).
set -euo pipefail
HOST_SCRIPT="${1:?usage: screens-mp.sh <host script> <client script> <out folder> [timeout]}"
CLIENT_SCRIPT="${2:?usage: screens-mp.sh <host script> <client script> <out folder> [timeout]}"
OUT="${3:?usage: screens-mp.sh <host script> <client script> <out folder> [timeout]}"
TIMEOUT="${4:-400}"
source "$(dirname "$0")/autotest-lib.sh"
[[ -e "$STATE_DIR/player-hold" ]] && { echo "screens-mp: the player plays (.state/player-hold); no windowed test slot now" >&2; exit 3; }
read -r HOST CLIENT <<<"$(take_slots 2)"
trap 'release_slot "$HOST"; release_slot "$CLIENT"' EXIT
PORT=$((35870 + ${HOST#t}))
echo "== host slot $HOST (windowed), client slot $CLIENT (headless), port $PORT"
# Only the host start gets the window (scripts/pc.sh SLOT_WINDOWED).
sed "s/{PORT}/$PORT/g" "$HOST_SCRIPT" | SLOT_WINDOWED=1 start_slot "$HOST"
HOST_LOG="" CLIENT_LOG="" CLIENT_STARTED=no
for ((t = 0; t < TIMEOUT; t += 10)); do
  sleep 10
  HOST_LOG="$(slot_log "$HOST")"
  if [[ "$CLIENT_STARTED" == no ]]; then
    if grep -q 'AUTOTEST hosting' <<<"$HOST_LOG"; then
      sed "s/{PORT}/$PORT/g" "$CLIENT_SCRIPT" | start_slot "$CLIENT"
      CLIENT_STARTED=yes
    elif grep -q '<<GAME EXITED>>' <<<"$HOST_LOG"; then
      break
    fi
    continue
  fi
  CLIENT_LOG="$(slot_log "$CLIENT")"
  if grep -q '<<GAME EXITED>>' <<<"$HOST_LOG" && grep -q '<<GAME EXITED>>' <<<"$CLIENT_LOG"; then break; fi
done

echo "=== host ($HOST)"; grep -E 'AUTOTEST (PASS|FAIL|DONE)|ERROR|screenshot' <<<"$HOST_LOG" || true
echo "=== client ($CLIENT)"; grep -E 'AUTOTEST (PASS|FAIL|DONE)|ERROR' <<<"$CLIENT_LOG" || true
mkdir -p "$OUT"
echo "$HOST_LOG" > "$OUT/$(basename "$HOST_SCRIPT" .txt).log"
echo "$CLIENT_LOG" > "$OUT/$(basename "$CLIENT_SCRIPT" .txt).log"
(
  export SLOT="$HOST"
  source "$LIB_DIR/pc.sh"
  pc "$PS_PATHS"'$d = Join-Path $modDir "screenshots"; if (Test-Path $d) { tar.exe -cf - -C $d . }' </dev/null | tar -xf - -C "$OUT"
)
echo "== screenshots in $OUT"
log_passed <<<"$HOST_LOG" && log_passed <<<"$CLIENT_LOG"
