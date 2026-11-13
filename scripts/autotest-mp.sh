#!/usr/bin/env bash
# Run a co-op session: one headless instance hosts a multiplayer career, the other instances join it over 127.0.0.1 and follow.
# Usage: [PLAYERS=3] autotest-mp.sh tests/autotest-mp/<session>.txt [timeout-seconds]
# PLAYERS (default 2): the host and PLAYERS-1 clients. Every client runs the same client script; "on 1" and "as 1" address the
# first client. The session file can hold lines for one player count only ("@3p <command>", see coop_host_script).
# The session file is the host's script. Its "client: <command>" lines are the client's commands before the join (local
# settings). The client then runs tests/autotest-mp/client.txt: join, then follow the host.
# "{PORT}" becomes the host's UDP port (35870 + host slot number), so parallel sessions do not collide.
# The host starts first. The clients start when the host log shows "AUTOTEST hosting".
# scripts/coop-report.sh gives the verdict from the host log and each client log. A run is retried only when nothing ran (empty host log).
set -euo pipefail
SESSION="${1:?usage: autotest-mp.sh <session file> [timeout]}"
TIMEOUT="${2:-600}"
PLAYERS="${PLAYERS:-2}"
case "$PLAYERS" in [2-8]) ;; *) echo "PLAYERS must be 2 to 8, not '$PLAYERS'"; exit 2 ;; esac
source "$(dirname "$0")/autotest-lib.sh"

RETRY="${AUTOTEST_RETRY:-1}"
NAME="$(basename "$SESSION" .txt)"
TAG="$NAME$([[ "$PLAYERS" -ne 2 ]] && echo "-${PLAYERS}p" || true)"
CLIENT_SCRIPT="$(dirname "$SESSION")/client.txt"
[[ -f "$SESSION" && -f "$CLIENT_SCRIPT" ]] || { echo "no session $SESSION or no $CLIENT_SCRIPT"; exit 2; }
# All slots at once, so parallel co-op tests cannot deadlock (see take_slots).
BEGIN_AT=$SECONDS
read -r -a SLOTS_TAKEN <<<"$(take_slots "$PLAYERS")"
HOST="${SLOTS_TAKEN[0]}" CLIENTS=("${SLOTS_TAKEN[@]:1}")
release_all() { local s; for s in "${SLOTS_TAKEN[@]}"; do release_slot "$s"; done; }
trap release_all EXIT
SLOTS_AT=$SECONDS
PORT=$((35870 + ${HOST#t}))
echo "== host slot $HOST, client slots ${CLIENTS[*]}, port $PORT, $PLAYERS players"

coop_host_script "$SESSION" "$PORT" "$PLAYERS" | start_slot "$HOST"
DEPLOYED_AT=$SECONDS
# Small status calls every AUTOTEST_POLL seconds; the full logs are fetched once at the end.
CLIENTS_STARTED=no LINK_REFUSED=no
while (( SECONDS - DEPLOYED_AT < TIMEOUT )); do
  sleep "${AUTOTEST_POLL:-3}"
  HOST_STATUS="$(slot_status "$HOST" "AUTOTEST hosting")"
  if [[ "$HOST_STATUS" == *"$LINK_REFUSED_TEXT"* ]]; then LINK_REFUSED=yes; break; fi
  if [[ "$CLIENTS_STARTED" == no ]]; then
    if [[ "$HOST_STATUS" == *match=1* ]]; then
      for c in "${CLIENTS[@]}"; do coop_client_script "$SESSION" "$PORT" "$PLAYERS" | start_slot "$c"; done
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
CLIENT_OUT=() CLIENT_LOG=() # index i holds the output and the log of CLIENTS[i]
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
  echo "== result: FAIL: PC link (the PC refused the calls, because this agent's turn is not active)"
  echo "== MP AUTOTEST LINK REFUSED $TAG (log $RUN_LOG)"; exit 3
fi
# Each log must be this run's own (first script line "telename <session>.<side>"), not a stale log of another test in the slot.
REPORT_RC=0
if ! grep -qF "telename $NAME.host" <<<"$HOST_LOG"; then
  echo "== result: FAIL: no host log of this run (PC link or deploy)"; REPORT_RC=1
  grep -v '^$' <<<"$HOST_LOG" | head -5 | sed 's/^/   /'
else
  # One report for each client: the host's steps, and the client's desyncs and state hashes against the host's.
  for i in "${!CLIENTS[@]}"; do
    log="${CLIENT_LOG[i]}"
    [[ -n "$log" ]] && ! grep -qF "telename $NAME.client" <<<"$log" && log=""
    [[ "$PLAYERS" -ne 2 ]] && echo "== client ${CLIENTS[i]}"
    "$LIB_DIR/coop-report.sh" <(echo "$HOST_LOG") <(echo "$log") | tee -a "$RUN_LOG" || REPORT_RC=1
  done
fi
if [[ "$REPORT_RC" == 0 ]]; then echo "== MP AUTOTEST OK $TAG (host $HOST, clients ${CLIENTS[*]})"; exit 0; fi
{ echo; echo "=== vanilla game log, host"; [[ "$HOST_OUT" == *"<<<VANILLA LOG>>>"* ]] && echo "${HOST_OUT#*<<<VANILLA LOG>>>}"
  for i in "${!CLIENTS[@]}"; do
    echo "=== vanilla game log, client ${CLIENTS[i]}"; [[ "${CLIENT_OUT[i]}" == *"<<<VANILLA LOG>>>"* ]] && echo "${CLIENT_OUT[i]#*<<<VANILLA LOG>>>}"
  done; } >> "$RUN_LOG"
# Retry only when nothing ran: a session that ran reports each step, and a rerun would hide a flaky step.
if [[ "$RETRY" -ge 1 ]] && ! grep -q 'AUTOTEST line' <<<"$HOST_LOG"; then
  release_all; trap - EXIT
  echo "== nothing ran in the host; one more try"
  exec env AUTOTEST_RETRY=0 PLAYERS="$PLAYERS" "$0" "$SESSION" "$TIMEOUT"
fi
echo "== MP AUTOTEST FAILED $TAG (log $RUN_LOG)"; exit 1
