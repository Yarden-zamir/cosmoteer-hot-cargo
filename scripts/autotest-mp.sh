#!/usr/bin/env bash
# Run a co-op autotest: one headless instance hosts a multiplayer career, a second one joins it over 127.0.0.1.
# Usage: autotest-mp.sh <host script> <client script> [timeout-seconds]
# "{PORT}" in both scripts becomes the host's UDP port (35870 + host slot number), so parallel MP tests do not collide.
# The host starts first. The client starts when the host log shows "AUTOTEST hosting".
# Exit code 0 only if both logs pass (AUTOTEST DONE, no AUTOTEST FAIL, no ERROR, so no "MP DESYNC")
# and the MPSTATE lines that both machines log at the same sim ticks are equal.
set -euo pipefail
HOST_SCRIPT="${1:?usage: autotest-mp.sh <host script> <client script> [timeout]}"
CLIENT_SCRIPT="${2:?usage: autotest-mp.sh <host script> <client script> [timeout]}"
TIMEOUT="${3:-600}"
source "$(dirname "$0")/autotest-lib.sh"

HOST="$(take_slot)"
CLIENT=""
trap 'release_slot "$HOST"; if [[ -n "$CLIENT" ]]; then release_slot "$CLIENT"; fi' EXIT
CLIENT="$(take_slot)"
PORT=$((35870 + ${HOST#t}))
echo "== host slot $HOST, client slot $CLIENT, port $PORT"

sed "s/{PORT}/$PORT/g" "$HOST_SCRIPT" | start_slot "$HOST"
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

echo "=== host ($HOST)"; grep -v '<<GAME EXITED>>' <<<"$HOST_LOG" || true
echo "=== client ($CLIENT)"; grep -v '<<GAME EXITED>>' <<<"$CLIENT_LOG" || true

OK=yes
if ! log_passed <<<"$HOST_LOG"; then echo "== host log failed"; OK=no; fi
if ! log_passed <<<"$CLIENT_LOG"; then echo "== client log failed"; OK=no; fi
# Compare the state lines of ticks that both machines logged, without the log time stamps.
states() { grep -o 'MPSTATE .*' <<<"$1" | tr -d '\r' | sort -u || true; }
HOST_STATES="$(states "$HOST_LOG")" CLIENT_STATES="$(states "$CLIENT_LOG")"
ticks() { grep -o 'tick=[0-9]*' <<<"$1" | sort -u || true; }
COMMON="$(comm -12 <(ticks "$HOST_STATES") <(ticks "$CLIENT_STATES"))"
DIFF="$(diff <(grep -wFf <(printf '%s\n' ${COMMON:-none}) <<<"$HOST_STATES" || true) <(grep -wFf <(printf '%s\n' ${COMMON:-none}) <<<"$CLIENT_STATES" || true) || true)"
echo "== MPSTATE: $(grep -c . <<<"$COMMON" || true) common ticks compared"
if [[ -n "$DIFF" ]]; then echo "== MPSTATE differs between host and client:"; echo "$DIFF"; OK=no; fi
if [[ "$OK" == yes ]]; then echo "== MP AUTOTEST OK (host $HOST, client $CLIENT)"; exit 0; fi
echo "== MP AUTOTEST FAILED (game logs: SLOT=$HOST scripts/logs.sh, SLOT=$CLIENT scripts/logs.sh)"; exit 1
