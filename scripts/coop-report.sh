#!/usr/bin/env bash
# Verdict of a co-op session from its two Hot Cargo logs. It reads only the logs (no PC calls), so any runner can use it.
# Usage: coop-report.sh <host log> <client log>
# Prints one line per step, then "== result: PASS" or "== result: FAIL: <reasons>". Exit code 0 only on PASS.
# Reasons: join failed, host did not finish, steps failed: <names>, desync, state differs, errors.
set -euo pipefail
HOST="$(tr -d '\r' < "${1:?usage: coop-report.sh <host log> <client log>}")"
CLIENT="$(tr -d '\r' < "${2:?usage: coop-report.sh <host log> <client log>}")"
REASONS=()

# Sim ticks of the step starts ("mpstep" runs in the sim on both machines): "tick step" lines.
STEP_TICKS="$(sed -n 's/.*AUTOTEST sim tick \([0-9]*\): mpstep \([^ ]*\).*/\1 \2/p' <<<"$HOST")"
step_at() { awk -v t="$1" '$1 <= t { s = $2 } END { print (s == "" ? "(before the first step)" : s) }' <<<"$STEP_TICKS"; }

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
    tick="$(sed -n 's/.*sim tick=\([0-9]*\).*/\1/p' <<<"$line")"
    echo "   desync at sim tick ${tick:-?}, step $( [[ -n "$tick" ]] && step_at "$tick" || echo '?')"
  done <<<"$DESYNCS"
fi

# The state hashes of the ticks that both machines logged. The first difference names the members that differ.
hashes() { sed -n 's/.*MPHASH \(tick=[0-9]* .*\)/\1/p' <<<"$1" | sort -u; }
FIRST="$(comm -3 <(hashes "$HOST" | sort) <(hashes "$CLIENT" | sort) | sed 's/^\t//' | sed -n 's/^tick=\([0-9]*\) .*/\1/p' | sort -n | uniq -d | head -1)"
COMMON="$(comm -12 <(hashes "$HOST" | sed 's/ .*//' | sort -u) <(hashes "$CLIENT" | sed 's/ .*//' | sort -u) | wc -l | tr -d ' ')"
echo "== state hash: $COMMON common ticks compared"
if [[ -n "$FIRST" ]]; then
  REASONS+=("state differs")
  echo "   first difference at sim tick $FIRST, step $(step_at "$FIRST")"
  fields() { grep -m1 "MPFIELDS tick=$FIRST " <<<"$1" | sed 's/.*MPFIELDS tick=[0-9]* //' | tr ' ' '\n'; }
  echo "   members that differ: $(comm -3 <(fields "$HOST" | sort) <(fields "$CLIENT" | sort) | sed 's/^\t//; s/=.*//' | sort -u | tr '\n' ' ')"
  grep -m1 "MPHASH tick=$FIRST " <<<"$HOST" | sed 's/.*MPHASH/   host:  /'
  grep -m1 "MPHASH tick=$FIRST " <<<"$CLIENT" | sed 's/.*MPHASH/   client:/'
fi

# Other errors (for example a failing Harmony patch). Step failures, client "on" failures and desyncs are counted above.
ERRORS="$( { grep -h ' ERROR ' <<<"$HOST"; grep -h ' ERROR ' <<<"$CLIENT"; } | grep -vE 'AUTOTEST STEP FAIL|AUTOTEST ON FAIL|MP DESYNC' || true)"
if [[ -n "$ERRORS" ]]; then REASONS+=("errors"); head -5 <<<"$ERRORS" | sed 's/^/   /'; fi

if [[ ${#REASONS[@]} -eq 0 ]]; then echo "== result: PASS"; exit 0; fi
printf -v JOINED '%s; ' "${REASONS[@]}"
echo "== result: FAIL: ${JOINED%; }"; exit 1
