#!/usr/bin/env bash
# Co-op screenshots: the host runs in the player's main slot (the only slot with graphics), the client in a headless test slot.
# Usage: screens-mp.sh <host script> <client script> <local output folder> [timeout-seconds]
# Takes the main slot lock and stops if the player's game runs. "{PORT}" in both scripts becomes the host port.
set -euo pipefail
HOST_SCRIPT="${1:?usage: screens-mp.sh <host script> <client script> <out folder> [timeout]}"
CLIENT_SCRIPT="${2:?usage: screens-mp.sh <host script> <client script> <out folder> [timeout]}"
OUT="${3:?usage: screens-mp.sh <host script> <client script> <out folder> [timeout]}"
TIMEOUT="${4:-400}"
source "$(dirname "$0")/autotest-lib.sh"
# Test slots use 35870 + slot number (autotest-mp.sh); 35879 is above them.
PORT=35879
MAIN_LOCK=/tmp/hot_cargo_main_slot.lock

until mkdir "$MAIN_LOCK" 2>/dev/null; do echo "main slot busy; waiting" >&2; sleep 15; done
echo $$ > "$MAIN_LOCK/pid"
CLIENT=""
trap 'if [[ -n "$CLIENT" ]]; then release_slot "$CLIENT"; fi; rm -rf "$MAIN_LOCK"' EXIT
running="$(export SLOT=main; source "$LIB_DIR/pc.sh"; pc "$PS_PATHS"'Get-SlotProcess')"
if [[ -n "$running" ]]; then echo "the player's game runs in the main slot; try later"; exit 2; fi

CLIENT="$(take_slot)"
echo "== host main, client slot $CLIENT, port $PORT"
sed "s/{PORT}/$PORT/g" "$HOST_SCRIPT" | start_slot main
HOST_LOG="" CLIENT_LOG="" CLIENT_STARTED=no
for ((t = 0; t < TIMEOUT; t += 10)); do
  sleep 10
  HOST_LOG="$(slot_log main)"
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
SLOT=main "$LIB_DIR/stop.sh" >/dev/null 2>&1 || true

echo "=== host (main)"; grep -E 'AUTOTEST (PASS|FAIL|DONE)|ERROR|screenshot' <<<"$HOST_LOG" || true
echo "=== client ($CLIENT)"; grep -E 'AUTOTEST (PASS|FAIL|DONE)|ERROR' <<<"$CLIENT_LOG" || true
mkdir -p "$OUT"
(
  export SLOT=main
  source "$LIB_DIR/pc.sh"
  pc "$PS_PATHS"'tar.exe -cf - -C (Join-Path $modDir "screenshots") .' | tar -xf - -C "$OUT"
)
echo "== screenshots in $OUT"
log_passed <<<"$HOST_LOG" && log_passed <<<"$CLIENT_LOG"
