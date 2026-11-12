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

RETRY="${AUTOTEST_RETRY:-1}"
NAME="$(basename "$HOST_SCRIPT" .host.txt)"
# Both slots at once, so parallel co-op tests cannot deadlock (see take_slots).
read -r HOST CLIENT <<<"$(take_slots 2)"
trap 'release_slot "$HOST"; release_slot "$CLIENT"' EXIT
PORT=$((35870 + ${HOST#t}))
echo "== host slot $HOST, client slot $CLIENT, port $PORT"

{ echo "telename $(basename "$HOST_SCRIPT" .txt)"; sed "s/{PORT}/$PORT/g" "$HOST_SCRIPT"; } | start_slot "$HOST"
HOST_LOG="" CLIENT_LOG="" CLIENT_STARTED=no
for ((t = 0; t < TIMEOUT; t += 10)); do
  sleep 10
  HOST_LOG="$(slot_log "$HOST")"
  if [[ "$CLIENT_STARTED" == no ]]; then
    if grep -q 'AUTOTEST hosting' <<<"$HOST_LOG"; then
      { echo "telename $(basename "$CLIENT_SCRIPT" .txt)"; sed "s/{PORT}/$PORT/g" "$CLIENT_SCRIPT"; } | start_slot "$CLIENT"
      CLIENT_STARTED=yes
    elif grep -q '<<GAME EXITED>>' <<<"$HOST_LOG"; then
      break
    fi
    continue
  fi
  CLIENT_LOG="$(slot_log "$CLIENT")"
  if grep -q '<<GAME EXITED>>' <<<"$HOST_LOG" && grep -q '<<GAME EXITED>>' <<<"$CLIENT_LOG"; then break; fi
done

RUN_LOG="$LOG_DIR/mp-$NAME.$(date +%Y%m%d-%H%M%S).$HOST-$CLIENT.log"
{ echo "=== host ($HOST)"; grep -v '<<GAME EXITED>>' <<<"$HOST_LOG" || true
  echo "=== client ($CLIENT)"; grep -v '<<GAME EXITED>>' <<<"$CLIENT_LOG" || true; } > "$RUN_LOG"
echo "== $NAME: log $RUN_LOG"

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
if [[ "$OK" == yes ]]; then echo "== MP AUTOTEST OK $NAME (host $HOST, client $CLIENT)"; exit 0; fi
{ echo; echo "=== vanilla game log, host"; slot_game_log "$HOST"; echo "=== vanilla game log, client"; slot_game_log "$CLIENT"; } >> "$RUN_LOG"
failure_lines "$RUN_LOG"
# One retry, like autotest.sh: a pass on the retry is FLAKY, not FAILED.
if [[ "$RETRY" -ge 1 ]]; then
  release_slot "$HOST"; release_slot "$CLIENT"; trap - EXIT
  if AUTOTEST_RETRY=0 "$0" "$HOST_SCRIPT" "$CLIENT_SCRIPT" "$TIMEOUT" > "$RUN_LOG.retry" 2>&1; then
    echo "== MP AUTOTEST FLAKY $NAME (failed once, passed on retry; first log $RUN_LOG)"
    echo "$(date +%F\ %T) mp-$NAME $RUN_LOG" >> "$STATE_DIR/flaky.log"
    exit 0
  fi
  tail -20 "$RUN_LOG.retry"
fi
echo "== MP AUTOTEST FAILED $NAME (log $RUN_LOG)"; exit 1
