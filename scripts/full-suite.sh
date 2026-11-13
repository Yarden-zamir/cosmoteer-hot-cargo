#!/usr/bin/env bash
# The full PC suite, in parts that each stay within the suite budget (scripts/gate-lib.sh, 600 s by default).
# Usage: full-suite.sh [--plan] [timeout-per-test]   --plan prints the parts and runs nothing.
# - Every game test and co-op session. A test with "# budget: exempt" (the real playthrough) runs in a part of its own.
# - Parts are filled greedily, slowest test first, up to HOT_CARGO_SLOTS_WHILE_PLAYING x HOT_CARGO_PART_SECONDS (default
#   420) slot-seconds of median PC cost (tools/test-audit.py; 120 s for a test without history). A co-op session counts
#   twice (2 slots). The parts run one after the other; each checks its own budget (scripts/autotest-all.sh).
# - On a clean tree that stays on one commit, a pass of every part writes the marker that scripts/release.sh needs.
# PC calls need an active turn: run it with --detach or as a background Bash call, and wait with t3_background_wait.
set -uo pipefail
cd "$(dirname "$0")/.."
source scripts/autotest-lib.sh
source scripts/gate-lib.sh
PLAN=no
if [[ "${1:-}" == --plan ]]; then PLAN=yes; shift; fi
TIMEOUT="${1:-400}"
START_SHA="$(git rev-parse HEAD)"
CLEAN="$([[ -z "$(git status --porcelain)" ]] && echo yes || echo no)"

declare -A COST
while read -r name pc _; do COST["$name"]="$pc"; done < <(uv run tools/test-audit.py --costs)
TESTS=() EXEMPT=()
for f in tests/autotest/*.txt; do if budget_exempt "$f"; then EXEMPT+=("$(basename "$f" .txt)"); else TESTS+=("$(basename "$f" .txt)"); fi; done
for n in $(coop_sessions); do TESTS+=("mp:$n"); done

CAPACITY=$(( ${HOT_CARGO_SLOTS_WHILE_PLAYING:-4} * ${HOT_CARGO_PART_SECONDS:-420} ))
cost_of() { local c="${COST[$1]:-0}"; (( c > 0 )) || c=120; [[ "$1" == mp:* ]] && c=$((c * 2)); echo "$c"; }
# Slowest first, then fill parts in order.
mapfile -t SORTED < <(for t in "${TESTS[@]}"; do echo "$(cost_of "$t") $t"; done | sort -rn | cut -d' ' -f2)
PARTS=() part="" used=0
for t in "${SORTED[@]}"; do
  c="$(cost_of "$t")"
  if [[ -n "$part" ]] && (( used + c > CAPACITY )); then PARTS+=("$part"); part="" used=0; fi
  part+="$t "; used=$((used + c))
done
[[ -n "$part" ]] && PARTS+=("$part")
for t in "${EXEMPT[@]}"; do PARTS+=("$t "); done

for i in "${!PARTS[@]}"; do echo "== part $((i + 1)) of ${#PARTS[@]}: ${PARTS[$i]}"; done
[[ "$PLAN" == yes ]] && exit 0

FAILED_PARTS=() T0=$SECONDS
for i in "${!PARTS[@]}"; do
  echo; echo "== full suite part $((i + 1)) of ${#PARTS[@]}"
  out="$(mktemp)"
  # shellcheck disable=SC2086
  scripts/autotest-all.sh "$TIMEOUT" ${PARTS[$i]} > "$out" 2>&1 || FAILED_PARTS+=("$((i + 1))")
  tail -25 "$out"; rm -f "$out"
done
echo "== full suite: ${#PARTS[@]} parts in $((SECONDS - T0)) s; failed parts: ${FAILED_PARTS[*]:-none}"
if [[ ${#FAILED_PARTS[@]} -eq 0 && "$CLEAN" == yes && "$(git rev-parse HEAD)" == "$START_SHA" && -z "$(git status --porcelain)" ]]; then
  touch "$STATE_DIR/autotest_pass_$START_SHA"; echo "== pass marker written for $START_SHA"
fi
[[ ${#FAILED_PARTS[@]} -eq 0 ]]
