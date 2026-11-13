#!/usr/bin/env bash
# Verdict of a co-op session from its two Hot Cargo logs. It reads only the logs (no PC calls), so any runner can use it.
# Usage: coop-report.sh <host log> <client log>
# Prints one line per step, then "== result: PASS" or "== result: FAIL: <reasons>". Exit code 0 only on PASS.
# Reasons: join failed, host did not finish, steps failed: <names>, desync, state differs, errors.
set -euo pipefail
HOST="$(tr -d '\r' < "${1:?usage: coop-report.sh <host log> <client log>}")"
CLIENT="$(tr -d '\r' < "${2:?usage: coop-report.sh <host log> <client log>}")"
REASONS=()

# Step starts by game and sim tick ("mpstep" runs in the sim on both machines): "game tick step" lines. Each game (a resync,
# a load, mprestart) counts ticks from 0, so a tick names a step only together with its game number.
STEP_TICKS="$(awk '/AUTOTEST MP game [0-9]+:/ { g = $6 + 0 }
  /AUTOTEST sim tick [0-9]+: mpstep / { t = $6; sub(":", "", t); print g, t, $8 }' <<<"$HOST")"
# The step that ran at game $1, sim tick $2: the last step that started before it, in this game or an earlier one.
step_at() { awk -v g="$1" -v t="$2" '$1 < g || ($1 == g && $2 <= t) { s = $3 } END { print (s == "" ? "(before the first step)" : s) }' <<<"$STEP_TICKS"; }

if ! grep -q 'AUTOTEST MP game' <<<"$CLIENT"; then
  REASONS+=("join failed")
  grep -E 'join failed|timed out waiting for joining|AUTOTEST FAIL' <<<"$CLIENT" | head -3 | sed 's/^/   client: /' || true
fi

# Steps: every started step, failed when the host logged a STEP FAIL for it (a sim line can fail it after its result line).
FAILED=()
while read -r step; do
  [[ -z "$step" ]] && continue
  reason="$(grep -F "AUTOTEST STEP FAIL $step:" <<<"$HOST" | head -1 | sed "s/.*AUTOTEST STEP FAIL $step: //" || true)"
  if [[ -n "$reason" ]]; then echo "FAIL $step: $reason"; FAILED+=("$step"); else echo "PASS $step"; fi
done < <(sed -n 's/.*AUTOTEST STEP START \([^ ]*\).*/\1/p' <<<"$HOST")
grep -q 'AUTOTEST STEP START' <<<"$HOST" || REASONS+=("no steps ran")
[[ ${#FAILED[@]} -gt 0 ]] && REASONS+=("steps failed: ${FAILED[*]}")
grep -q 'AUTOTEST STEPS ' <<<"$HOST" && grep -q 'AUTOTEST DONE' <<<"$HOST" || REASONS+=("host did not finish")

# Desyncs that the game detected, with the step that ran at that sim tick.
DESYNCS="$( { grep -h 'MP DESYNC' <<<"$HOST"; grep -h 'MP DESYNC' <<<"$CLIENT"; } || true)"
if [[ -n "$DESYNCS" ]]; then
  REASONS+=("desync")
  while read -r line; do
    game="$(sed -n 's/.*game=\([0-9]*\).*/\1/p' <<<"$line")" tick="$(sed -n 's/.*sim tick=\([0-9]*\).*/\1/p' <<<"$line")"
    echo "   desync in game ${game:-?} at sim tick ${tick:-?}, step $( [[ -n "$game" && -n "$tick" ]] && step_at "$game" "$tick" || echo '?')"
  done <<<"$DESYNCS"
fi

# The state hashes of the ticks that both machines logged. The first difference names the members that differ.
# Lines are keyed by "game=N tick=T".
hashes() { sed -n 's/.*MPHASH \(game=[0-9]* tick=[0-9]* .*\)/\1/p' <<<"$1" | sort -u; }
keys() { hashes "$1" | cut -d' ' -f1,2 | sort -u; }
# Keys that both machines logged with different lines, in game and tick order.
FIRST="$(comm -3 <(hashes "$HOST") <(hashes "$CLIENT") | sed 's/^\t//' | cut -d' ' -f1,2 | sort | uniq -d \
  | sed 's/game=\([0-9]*\) tick=\([0-9]*\)/\1 \2/' | sort -n -k1,1 -k2,2 | head -1)"
echo "== state hash: $(comm -12 <(keys "$HOST") <(keys "$CLIENT") | wc -l | tr -d ' ') common ticks compared"
if [[ -n "$FIRST" ]]; then
  read -r GAME TICK <<<"$FIRST"
  REASONS+=("state differs")
  echo "   first difference in game $GAME at sim tick $TICK, step $(step_at "$GAME" "$TICK")"
  fields() { grep -m1 "MPFIELDS game=$GAME tick=$TICK " <<<"$1" | sed 's/.*MPFIELDS game=[0-9]* tick=[0-9]* //' | tr ' ' '\n'; }
  echo "   members that differ: $(comm -3 <(fields "$HOST" | sort) <(fields "$CLIENT" | sort) | sed 's/^\t//; s/=.*//' | sort -u | tr '\n' ' ')"
  grep -m1 "MPHASH game=$GAME tick=$TICK " <<<"$HOST" | sed 's/.*MPHASH/   host:  /'
  grep -m1 "MPHASH game=$GAME tick=$TICK " <<<"$CLIENT" | sed 's/.*MPHASH/   client:/'
fi

# A member that the hash cannot serialize is a gap that hides desyncs in it.
GAPS="$(sed -n 's/.*MPHASH cannot serialize \([^ ]*\) .*/\1/p' <<<"$HOST$CLIENT" | sort -u | tr '\n' ' ')"
if [[ -n "$GAPS" ]]; then REASONS+=("state hash gap"); echo "   the state hash cannot serialize: $GAPS"; fi

# Other errors (for example a failing Harmony patch). Step failures, client "on" failures and desyncs are counted above.
ERRORS="$( { grep -h ' ERROR ' <<<"$HOST"; grep -h ' ERROR ' <<<"$CLIENT"; } | grep -vE 'AUTOTEST STEP FAIL|AUTOTEST ON FAIL|MP DESYNC' || true)"
if [[ -n "$ERRORS" ]]; then REASONS+=("errors"); head -5 <<<"$ERRORS" | sed 's/^/   /'; fi

if [[ ${#REASONS[@]} -eq 0 ]]; then echo "== result: PASS"; exit 0; fi
printf -v JOINED '%s; ' "${REASONS[@]}"
echo "== result: FAIL: ${JOINED%; }"; exit 1
