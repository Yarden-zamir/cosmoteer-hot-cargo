#!/usr/bin/env bash
# Run a co-op autotest: one headless instance hosts a multiplayer career, the other instances join it over 127.0.0.1.
# Usage: [PLAYERS=3] autotest-mp.sh <host script> <client script> [timeout-seconds]
# PLAYERS (default 2) is the number of players: the host and PLAYERS-1 clients that all run the client script.
# "{PORT}" in the scripts becomes the host's UDP port (35870 + host slot number), so parallel MP tests do not collide.
# "{PLAYERS}" becomes the number of players (for "mpstart {PLAYERS}").
# The host starts first. The clients start when the host log shows "AUTOTEST hosting".
# Exit code 0 only if every log passes (AUTOTEST DONE, no AUTOTEST FAIL, no ERROR, so no "MP DESYNC")
# and the MPSTATE lines that every machine logs at the same sim ticks are equal.
set -euo pipefail
HOST_SCRIPT="${1:?usage: autotest-mp.sh <host script> <client script> [timeout]}"
CLIENT_SCRIPT="${2:?usage: autotest-mp.sh <host script> <client script> [timeout]}"
TIMEOUT="${3:-600}"
PLAYERS="${PLAYERS:-2}"
case "$PLAYERS" in [2-8]) ;; *) echo "PLAYERS must be 2 to 8, not '$PLAYERS'"; exit 2 ;; esac
source "$(dirname "$0")/autotest-lib.sh"

RETRY="${AUTOTEST_RETRY:-1}"
NAME="$(basename "$HOST_SCRIPT" .host.txt)"
TAG="$NAME$([[ "$PLAYERS" -ne 2 ]] && echo "-${PLAYERS}p" || true)"
# All slots at once, so parallel co-op tests cannot deadlock (see take_slots).
read -r -a SLOTS_TAKEN <<<"$(take_slots "$PLAYERS")"
HOST="${SLOTS_TAKEN[0]}" CLIENTS=("${SLOTS_TAKEN[@]:1}")
release_all() { local s; for s in "${SLOTS_TAKEN[@]}"; do release_slot "$s"; done; }
trap release_all EXIT
PORT=$((35870 + ${HOST#t}))
echo "== host slot $HOST, client slots ${CLIENTS[*]}, port $PORT, $PLAYERS players"

script_for() { echo "telename $(basename "$1" .txt)"; sed "s/{PORT}/$PORT/g; s/{PLAYERS}/$PLAYERS/g" "$1"; }
exited() { grep -q '<<GAME EXITED>>' <<<"$1"; }

script_for "$HOST_SCRIPT" | start_slot "$HOST"
HOST_LOG="" CLIENTS_STARTED=no
CLIENT_LOG=() # index i holds the log of CLIENTS[i]
for ((t = 0; t < TIMEOUT; t += 10)); do
  sleep 10
  HOST_LOG="$(slot_log "$HOST")"
  if [[ "$CLIENTS_STARTED" == no ]]; then
    if grep -q 'AUTOTEST hosting' <<<"$HOST_LOG"; then
      for c in "${CLIENTS[@]}"; do script_for "$CLIENT_SCRIPT" | start_slot "$c"; done
      CLIENTS_STARTED=yes
    elif exited "$HOST_LOG"; then
      break
    fi
    continue
  fi
  done_all=yes
  exited "$HOST_LOG" || done_all=no
  for i in "${!CLIENTS[@]}"; do
    CLIENT_LOG[i]="$(slot_log "${CLIENTS[i]}")"
    exited "${CLIENT_LOG[i]}" || done_all=no
  done
  [[ "$done_all" == yes ]] && break
done

RUN_LOG="$LOG_DIR/mp-$TAG.$(date +%Y%m%d-%H%M%S).$(IFS=-; echo "${SLOTS_TAKEN[*]}").log"
{ echo "=== host ($HOST)"; grep -v '<<GAME EXITED>>' <<<"$HOST_LOG" || true
  for i in "${!CLIENTS[@]}"; do echo "=== client (${CLIENTS[i]})"; grep -v '<<GAME EXITED>>' <<<"${CLIENT_LOG[i]:-}" || true; done; } > "$RUN_LOG"
echo "== $TAG: log $RUN_LOG"

OK=yes
if ! log_passed <<<"$HOST_LOG"; then echo "== host log failed"; OK=no; fi
for i in "${!CLIENTS[@]}"; do
  if ! log_passed <<<"${CLIENT_LOG[i]:-}"; then echo "== client ${CLIENTS[i]} log failed"; OK=no; fi
done
# Compare the state lines of ticks that every machine logged, without the log time stamps.
states() { grep -o 'MPSTATE .*' <<<"$1" | tr -d '\r' | sort -u || true; }
ticks() { grep -o 'tick=[0-9]*' <<<"$1" | sort -u || true; }
HOST_STATES="$(states "$HOST_LOG")"
COMMON="$(ticks "$HOST_STATES")"
for i in "${!CLIENTS[@]}"; do COMMON="$(comm -12 <(echo "$COMMON") <(ticks "$(states "${CLIENT_LOG[i]:-}")"))"; done
only_common() { grep -wFf <(printf '%s\n' ${COMMON:-none}) <<<"$1" || true; }
echo "== MPSTATE: $(grep -c . <<<"$COMMON" || true) common ticks compared"
for i in "${!CLIENTS[@]}"; do
  DIFF="$(diff <(only_common "$HOST_STATES") <(only_common "$(states "${CLIENT_LOG[i]:-}")") || true)"
  if [[ -n "$DIFF" ]]; then echo "== MPSTATE differs between host and client ${CLIENTS[i]}:"; echo "$DIFF"; OK=no; fi
done
if [[ "$OK" == yes ]]; then echo "== MP AUTOTEST OK $TAG (host $HOST, clients ${CLIENTS[*]})"; exit 0; fi
{ echo; echo "=== vanilla game log, host"; slot_game_log "$HOST"
  for c in "${CLIENTS[@]}"; do echo "=== vanilla game log, client $c"; slot_game_log "$c"; done; } >> "$RUN_LOG"
failure_lines "$RUN_LOG"
# One retry, like autotest.sh: a pass on the retry is FLAKY, not FAILED.
if [[ "$RETRY" -ge 1 ]]; then
  release_all; trap - EXIT
  if AUTOTEST_RETRY=0 "$0" "$HOST_SCRIPT" "$CLIENT_SCRIPT" "$TIMEOUT" > "$RUN_LOG.retry" 2>&1; then
    echo "== MP AUTOTEST FLAKY $TAG (failed once, passed on retry; first log $RUN_LOG)"
    echo "$(date +%F\ %T) mp-$TAG $RUN_LOG" >> "/tmp/hot_cargo_flaky.log"
    exit 0
  fi
  tail -20 "$RUN_LOG.retry"
fi
echo "== MP AUTOTEST FAILED $TAG (log $RUN_LOG)"; exit 1
