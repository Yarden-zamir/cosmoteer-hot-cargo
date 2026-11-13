#!/usr/bin/env bash
# Run a co-op autotest: one headless instance hosts a multiplayer career, the other instances join it over 127.0.0.1.
# Usage: [PLAYERS=3] autotest-mp.sh <host script> <client script> [timeout-seconds]
# PLAYERS (default 2) is the number of players: the host and PLAYERS-1 clients that all run the client script.
# "{PORT}" in the scripts becomes the host's UDP port (35870 + host slot number), so parallel MP tests do not collide.
# "{PLAYERS}" becomes the number of players (for "mpstart {PLAYERS}").
# Line filters: a line that starts with "@<n>p " runs only with n players, "@c<k> " only on client k (1 is the first client).
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
BEGIN_AT=$SECONDS
read -r -a SLOTS_TAKEN <<<"$(take_slots "$PLAYERS")"
HOST="${SLOTS_TAKEN[0]}" CLIENTS=("${SLOTS_TAKEN[@]:1}")
release_all() { local s; for s in "${SLOTS_TAKEN[@]}"; do release_slot "$s"; done; }
trap release_all EXIT
SLOTS_AT=$SECONDS
PORT=$((35870 + ${HOST#t}))
echo "== host slot $HOST, client slots ${CLIENTS[*]}, port $PORT, $PLAYERS players"

# script_for <script> <client index, 0 for the host>
script_for() {
  echo "telename $(basename "$1" .txt)"
  awk -v p="$PLAYERS" -v c="$2" '
    /^@[0-9]+p / { if (substr($1, 2) + 0 != p) next; sub(/^@[0-9]+p /, "") }
    /^@c[0-9]+ / { if (substr($1, 3) + 0 != c) next; sub(/^@c[0-9]+ /, "") }
    { print }' "$1" | sed "s/{PORT}/$PORT/g; s/{PLAYERS}/$PLAYERS/g"
}

script_for "$HOST_SCRIPT" 0 | start_slot "$HOST"
DEPLOYED_AT=$SECONDS
# Small status calls every AUTOTEST_POLL seconds; the full logs are fetched once at the end.
CLIENTS_STARTED=no LINK_REFUSED=no
while (( SECONDS - DEPLOYED_AT < TIMEOUT )); do
  sleep "${AUTOTEST_POLL:-3}"
  HOST_STATUS="$(slot_status "$HOST" "AUTOTEST hosting")"
  if [[ "$HOST_STATUS" == *"$LINK_REFUSED_TEXT"* ]]; then LINK_REFUSED=yes; break; fi
  if [[ "$CLIENTS_STARTED" == no ]]; then
    if [[ "$HOST_STATUS" == *match=1* ]]; then
      for i in "${!CLIENTS[@]}"; do script_for "$CLIENT_SCRIPT" $((i + 1)) | start_slot "${CLIENTS[i]}"; done
      CLIENTS_STARTED=yes
    elif [[ "$HOST_STATUS" == *running=0* ]]; then
      break
    fi
    continue
  fi
  all_exited=yes
  [[ "$HOST_STATUS" == *running=0* ]] || all_exited=no
  for c in "${CLIENTS[@]}"; do
    CLIENT_STATUS="$(slot_status "$c")"
    if [[ "$CLIENT_STATUS" == *"$LINK_REFUSED_TEXT"* ]]; then LINK_REFUSED=yes; break 2; fi
    [[ "$CLIENT_STATUS" == *running=0* ]] || all_exited=no
  done
  [[ "$all_exited" == yes ]] && break
done
SCRIPT_END_AT=$SECONDS
# A failed PC call leaves an empty log, which never passes.
HOST_OUT="$(slot_finish "$HOST" 2>&1 || true)"
HOST_LOG="${HOST_OUT%%<<<VANILLA LOG>>>*}"
CLIENT_OUT=() CLIENT_LOG=() # index i holds the output and log of CLIENTS[i]
for i in "${!CLIENTS[@]}"; do
  CLIENT_OUT[i]=""; [[ "$CLIENTS_STARTED" == yes ]] && CLIENT_OUT[i]="$(slot_finish "${CLIENTS[i]}" 2>&1 || true)"
  CLIENT_LOG[i]="${CLIENT_OUT[i]%%<<<VANILLA LOG>>>*}"
done

RUN_LOG="$LOG_DIR/mp-$TAG.$(date +%Y%m%d-%H%M%S).$(IFS=-; echo "${SLOTS_TAKEN[*]}").log"
{ echo "=== host ($HOST)"; echo "$HOST_LOG"
  for i in "${!CLIENTS[@]}"; do echo "=== client (${CLIENTS[i]})"; echo "${CLIENT_LOG[i]}"; done; } > "$RUN_LOG"
TIMING="== timing mp-$TAG wait=$((SLOTS_AT - BEGIN_AT)) deploy=$((DEPLOYED_AT - SLOTS_AT)) script=$((SCRIPT_END_AT - DEPLOYED_AT)) teardown=$((SECONDS - SCRIPT_END_AT)) total=$((SECONDS - BEGIN_AT))"
echo "$TIMING" | tee -a "$RUN_LOG"
echo "== $TAG: log $RUN_LOG"

if [[ "$LINK_REFUSED" == yes ]] || grep -q "$LINK_REFUSED_TEXT" "$RUN_LOG"; then
  echo "== MP AUTOTEST LINK REFUSED $TAG: the PC refused the calls, because this agent's turn is not active (log $RUN_LOG)"; exit 3
fi
OK=yes
# Each log must be this run's own (first script line "telename <script name>"), not a stale log of another test in the slot.
if ! log_passed <<<"$HOST_LOG" || ! grep -qF "telename $(basename "$HOST_SCRIPT" .txt)" <<<"$HOST_LOG"; then echo "== host log failed"; OK=no; fi
for i in "${!CLIENTS[@]}"; do
  if ! log_passed <<<"${CLIENT_LOG[i]}" || ! grep -qF "telename $(basename "$CLIENT_SCRIPT" .txt)" <<<"${CLIENT_LOG[i]}"; then echo "== client ${CLIENTS[i]} log failed"; OK=no; fi
done
# Compare the state lines of ticks that every machine logged, without the log time stamps.
states() { grep -o 'MPSTATE .*' <<<"$1" | tr -d '\r' | sort -u || true; }
ticks() { grep -o 'tick=[0-9]*' <<<"$1" | sort -u || true; }
HOST_STATES="$(states "$HOST_LOG")"
COMMON="$(ticks "$HOST_STATES")"
for i in "${!CLIENTS[@]}"; do COMMON="$(comm -12 <(echo "$COMMON") <(ticks "$(states "${CLIENT_LOG[i]}")"))"; done
only_common() { grep -wFf <(printf '%s\n' ${COMMON:-none}) <<<"$1" || true; }
echo "== MPSTATE: $(grep -c . <<<"$COMMON" || true) common ticks compared"
for i in "${!CLIENTS[@]}"; do
  DIFF="$(diff <(only_common "$HOST_STATES") <(only_common "$(states "${CLIENT_LOG[i]}")") || true)"
  if [[ -n "$DIFF" ]]; then echo "== MPSTATE differs between host and client ${CLIENTS[i]}:"; echo "$DIFF"; OK=no; fi
done
if [[ "$OK" == yes ]]; then echo "== MP AUTOTEST OK $TAG (host $HOST, clients ${CLIENTS[*]})"; exit 0; fi
{ echo; echo "=== vanilla game log, host"; [[ "$HOST_OUT" == *"<<<VANILLA LOG>>>"* ]] && echo "${HOST_OUT#*<<<VANILLA LOG>>>}"
  for i in "${!CLIENTS[@]}"; do
    echo "=== vanilla game log, client ${CLIENTS[i]}"; [[ "${CLIENT_OUT[i]}" == *"<<<VANILLA LOG>>>"* ]] && echo "${CLIENT_OUT[i]#*<<<VANILLA LOG>>>}"
  done; } >> "$RUN_LOG"
failure_lines "$RUN_LOG"
# One retry, like autotest.sh: a pass on the retry is FLAKY, not FAILED.
if [[ "$RETRY" -ge 1 ]]; then
  release_all; trap - EXIT
  if AUTOTEST_RETRY=0 "$0" "$HOST_SCRIPT" "$CLIENT_SCRIPT" "$TIMEOUT" > "$RUN_LOG.retry" 2>&1; then
    echo "== MP AUTOTEST FLAKY $TAG (failed once, passed on retry; first log $RUN_LOG)"
    echo "$(date +%F\ %T) mp-$TAG $RUN_LOG" >> "$STATE_DIR/flaky.log"
    exit 0
  fi
  tail -20 "$RUN_LOG.retry"
fi
echo "== MP AUTOTEST FAILED $TAG (log $RUN_LOG)"; exit 1
