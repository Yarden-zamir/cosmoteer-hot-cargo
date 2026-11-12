#!/usr/bin/env bash
# Run a co-op session: one headless instance hosts a multiplayer career, a second one joins it over 127.0.0.1 and follows.
# Usage: autotest-mp.sh tests/autotest-mp/<session>.txt [timeout-seconds]
# The session file is the host's script. Its "client: <command>" lines are the client's commands before the join (local
# settings). The client then runs tests/autotest-mp/client.txt: join, then follow the host.
# "{PORT}" becomes the host's UDP port (35870 + host slot number), so parallel sessions do not collide.
# The host starts first. The client starts when the host log shows "AUTOTEST hosting".
# scripts/coop-report.sh gives the verdict from both logs. A run is retried only when nothing ran (empty host log).
set -euo pipefail
SESSION="${1:?usage: autotest-mp.sh <session file> [timeout]}"
TIMEOUT="${2:-600}"
source "$(dirname "$0")/autotest-lib.sh"

RETRY="${AUTOTEST_RETRY:-1}"
NAME="$(basename "$SESSION" .txt)"
CLIENT_SCRIPT="$(dirname "$SESSION")/client.txt"
[[ -f "$SESSION" && -f "$CLIENT_SCRIPT" ]] || { echo "no session $SESSION or no $CLIENT_SCRIPT"; exit 2; }
# Both slots at once, so parallel co-op tests cannot deadlock (see take_slots).
BEGIN_AT=$SECONDS
read -r HOST CLIENT <<<"$(take_slots 2)"
trap 'release_slot "$HOST"; release_slot "$CLIENT"' EXIT
SLOTS_AT=$SECONDS
PORT=$((35870 + ${HOST#t}))
echo "== host slot $HOST, client slot $CLIENT, port $PORT"

{ echo "telename $NAME.host"; grep -v '^client:' "$SESSION" | sed "s/{PORT}/$PORT/g"; } | start_slot "$HOST"
DEPLOYED_AT=$SECONDS
# Small status calls every AUTOTEST_POLL seconds; the full logs are fetched once at the end.
CLIENT_STARTED=no LINK_REFUSED=no
while (( SECONDS - DEPLOYED_AT < TIMEOUT )); do
  sleep "${AUTOTEST_POLL:-3}"
  HOST_STATUS="$(slot_status "$HOST" "AUTOTEST hosting")"
  if [[ "$HOST_STATUS" == *"$LINK_REFUSED_TEXT"* ]]; then LINK_REFUSED=yes; break; fi
  if [[ "$CLIENT_STARTED" == no ]]; then
    if [[ "$HOST_STATUS" == *match=1* ]]; then
      { echo "telename $NAME.client"; sed -n 's/^client: *//p' "$SESSION"; sed "s/{PORT}/$PORT/g" "$CLIENT_SCRIPT"; } | start_slot "$CLIENT"
      CLIENT_STARTED=yes
    elif [[ "$HOST_STATUS" == *running=0* ]]; then
      break
    fi
    continue
  fi
  CLIENT_STATUS="$(slot_status "$CLIENT")"
  if [[ "$CLIENT_STATUS" == *"$LINK_REFUSED_TEXT"* ]]; then LINK_REFUSED=yes; break; fi
  if [[ "$HOST_STATUS" == *running=0* && "$CLIENT_STATUS" == *running=0* ]]; then break; fi
done
SCRIPT_END_AT=$SECONDS
# A failed PC call leaves an empty log, which never passes.
HOST_OUT="$(slot_finish "$HOST" 2>&1 || true)"
CLIENT_OUT=""; [[ "$CLIENT_STARTED" == yes ]] && CLIENT_OUT="$(slot_finish "$CLIENT" 2>&1 || true)"
HOST_LOG="${HOST_OUT%%<<<VANILLA LOG>>>*}" CLIENT_LOG="${CLIENT_OUT%%<<<VANILLA LOG>>>*}"

RUN_LOG="$LOG_DIR/mp-$NAME.$(date +%Y%m%d-%H%M%S).$HOST-$CLIENT.log"
{ echo "=== host ($HOST)"; echo "$HOST_LOG"
  echo "=== client ($CLIENT)"; echo "$CLIENT_LOG"; } > "$RUN_LOG"
TIMING="== timing mp-$NAME wait=$((SLOTS_AT - BEGIN_AT)) deploy=$((DEPLOYED_AT - SLOTS_AT)) script=$((SCRIPT_END_AT - DEPLOYED_AT)) teardown=$((SECONDS - SCRIPT_END_AT)) total=$((SECONDS - BEGIN_AT))"
echo "$TIMING" | tee -a "$RUN_LOG"
echo "== $NAME: log $RUN_LOG"

if [[ "$LINK_REFUSED" == yes ]] || grep -q "$LINK_REFUSED_TEXT" "$RUN_LOG"; then
  echo "== result: FAIL: PC link (the PC refused the calls, because this agent's turn is not active)"
  echo "== MP AUTOTEST LINK REFUSED $NAME (log $RUN_LOG)"; exit 3
fi
# Each log must be this run's own (first script line "telename <session>.<side>"), not a stale log of another test in the slot.
REPORT_RC=0
if ! grep -qF "telename $NAME.host" <<<"$HOST_LOG"; then
  echo "== result: FAIL: no host log of this run (PC link or deploy)"; REPORT_RC=1
  grep -v '^$' <<<"$HOST_LOG" | head -5 | sed 's/^/   /'
else
  [[ -n "$CLIENT_LOG" ]] && ! grep -qF "telename $NAME.client" <<<"$CLIENT_LOG" && CLIENT_LOG=""
  "$LIB_DIR/coop-report.sh" <(echo "$HOST_LOG") <(echo "$CLIENT_LOG") | tee -a "$RUN_LOG" || REPORT_RC=1
fi
if [[ "$REPORT_RC" == 0 ]]; then echo "== MP AUTOTEST OK $NAME (host $HOST, client $CLIENT)"; exit 0; fi
{ echo; echo "=== vanilla game log, host"; [[ "$HOST_OUT" == *"<<<VANILLA LOG>>>"* ]] && echo "${HOST_OUT#*<<<VANILLA LOG>>>}"
  echo "=== vanilla game log, client"; [[ "$CLIENT_OUT" == *"<<<VANILLA LOG>>>"* ]] && echo "${CLIENT_OUT#*<<<VANILLA LOG>>>}"; } >> "$RUN_LOG"
# Retry only when nothing ran: a session that ran reports each step, and a rerun would hide a flaky step.
if [[ "$RETRY" -ge 1 ]] && ! grep -q 'AUTOTEST line' <<<"$HOST_LOG"; then
  release_slot "$HOST"; release_slot "$CLIENT"; trap - EXIT
  echo "== nothing ran in the host; one more try"
  exec env AUTOTEST_RETRY=0 "$0" "$SESSION" "$TIMEOUT"
fi
echo "== MP AUTOTEST FAILED $NAME (log $RUN_LOG)"; exit 1
