#!/usr/bin/env bash
# The nightly run on main: the full PC suite, the test impact map, the flake quarantine, and a bisect of new failures.
# Usage: nightly.sh [timeout-per-test]
# Run it once a night from a scheduled agent turn: PC calls need an active turn, so start it as a background Bash call and
# wait with t3_background_wait (or --detach in scripts/detach.sh and scripts/suite-wait.sh).
# It works in its own worktree <container>/nightly (detached at origin/main), never in main. Steps:
# 1. The full PC suite in parts of at most the suite budget (scripts/full-suite.sh; a pass writes the release marker), and
#    at the same time on the Mac the test impact map (scripts/coverage-map.sh).
# 2. Each quarantined test runs HOT_CARGO_QUARANTINE_RUNS times (default 3) with no retry, on its runner. All passed: one
#    clean night (scripts/gate-lib.sh releases it after HOT_CARGO_QUARANTINE_NIGHTS).
# 3. Each failed test that is not quarantined (at most 3 a night) is bisected over the merges since the last green night
#    (scripts/bisect-autotest.sh --first-parent), on the Mac when it also fails there, else on the PC.
# 4. A short report: <container>/.state/nightly/<date>.md. A green night records origin/main in .state/nightly/last-green.
set -uo pipefail
cd "$(dirname "$0")/.."
source scripts/autotest-lib.sh
source scripts/gate-lib.sh
TIMEOUT="${1:-400}"
CONTAINER="$(cd .. && pwd)"
WT="$CONTAINER/nightly"
OUT="$STATE_DIR/nightly"
mkdir -p "$OUT"
DAY="$(date +%Y%m%d-%H%M)"
REPORT="$OUT/$DAY.md"

git fetch -q origin main
if [[ -d "$WT" ]]; then
  [[ -z "$(git -C "$WT" status --porcelain)" ]] || { echo "nightly: $WT has local changes; look at them first"; exit 1; }
  git -C "$WT" checkout -q --detach origin/main
else
  git worktree add -q --detach "$WT" origin/main
fi
cd "$WT"
SHA="$(git rev-parse HEAD)"
LAST_GREEN="$(cat "$OUT/last-green" 2>/dev/null || true)"
echo "== nightly at ${SHA:0:9} (last green: ${LAST_GREEN:0:9})"

# 1. PC full suite and Mac map at the same time.
T0=$SECONDS
touch "$OUT/$DAY.start"
scripts/coverage-map.sh "$TIMEOUT" > "$OUT/$DAY.map.txt" 2>&1 & MAP_PID=$!
scripts/full-suite.sh "$TIMEOUT" > "$OUT/$DAY.suite.txt" 2>&1; SUITE_RC=$?
wait "$MAP_PID"; MAP_RC=$?
SUITE_S=$((SECONDS - T0))
FAILED="$(grep -h '^FAIL ' "$OUT/$DAY.suite.txt" | cut -d' ' -f2- | sort -u | tr '\n' ' ')"

# 2. Quarantine pass rates.
QROWS=""
if [[ -s "$QUARANTINE" ]]; then
  while IFS=$'\t' read -r name runner since clean last; do
    [[ -z "$name" ]] && continue
    passes=0 runs="${HOT_CARGO_QUARANTINE_RUNS:-3}"
    for ((i = 0; i < runs; i++)); do
      if [[ "$runner" == pc ]]; then AUTOTEST_RETRY=0 scripts/autotest-all.sh "$TIMEOUT" "$name" > /dev/null 2>&1 && passes=$((passes + 1))
      else MAC_RETRY=0 scripts/autotest-mac.sh "$TIMEOUT" "$name" > /dev/null 2>&1 && passes=$((passes + 1)); fi
    done
    quarantine_night "$name" "$runner" "$([[ $passes -eq $runs ]] && echo yes || echo no)" 2>> "$OUT/$DAY.quarantine.txt"
    QROWS+="| $name | $runner | since $since | $passes of $runs passed | $((clean + (passes == runs ? 1 : 0))) clean nights |"$'\n'
  done < <(cat "$QUARANTINE")
fi

# 3. Bisect new failures over the merges since the last green night.
BISECT=""
if [[ -n "$FAILED" && -n "$LAST_GREEN" ]]; then
  n=0
  for t in $FAILED; do
    [[ "$t" == mp:* ]] && { BISECT+="- $t: co-op session; bisect it by hand (scripts/bisect-autotest.sh takes single tests)"$'\n'; continue; }
    is_quarantined "$t" pc && continue
    (( n++ >= 3 )) && { BISECT+="- $t: not bisected (3 a night)"$'\n'; continue; }
    runner=pc
    MAC_RETRY=0 scripts/autotest-mac.sh "$TIMEOUT" "$t" > /dev/null 2>&1 || runner=mac
    culprit="$(BISECT_RUNNER=$runner scripts/bisect-autotest.sh --first-parent "$t" "$LAST_GREEN" "$SHA" "$TIMEOUT" 2>&1 | grep -m1 'is the first bad commit' || true)"
    BISECT+="- $t (bisected on the $runner): ${culprit:-no culprit found (flaky, or it fails on the good commit too)}"$'\n'
  done
elif [[ -n "$FAILED" ]]; then
  BISECT="- no last green night yet: nothing to bisect against"$'\n'
fi
[[ $SUITE_RC -eq 0 ]] && echo "$SHA" > "$OUT/last-green"

{
  echo "# Nightly $DAY at ${SHA:0:9}"
  echo
  echo "- Full PC suite: $([[ $SUITE_RC -eq 0 ]] && echo "PASS (release marker written)" || echo "FAIL: ${FAILED:-a part failed without a failed test (budget, build, link)}"), $SUITE_S s for every part."
  grep -h '^== part \|BUDGET EXCEEDED\|== budget:' "$OUT/$DAY.suite.txt" | sed 's/^/  - /'
  echo "- Test impact map: $([[ $MAP_RC -eq 0 ]] && tail -1 "$OUT/$DAY.map.txt" || echo "refresh failed ($OUT/$DAY.map.txt)")"
  echo
  echo "## New failures"
  echo "${BISECT:-none}"
  echo "## Quarantine"
  if [[ -n "$QROWS" ]]; then echo "| Test | Runner | In since | Tonight | Clean nights |"; echo "|---|---|---|---|---|"; echo -n "$QROWS"; else echo "empty"; fi
  [[ -s "$OUT/$DAY.quarantine.txt" ]] && sed 's/^/- /' "$OUT/$DAY.quarantine.txt"
  echo
  echo "## Slowest tests"
  # Tonight's runs only: the PC suite outputs (.out) and the Mac logs (.mac.log).
  slowest_tests $(find "$LOG_DIR" \( -name '*.out' -o -name '*.mac.log' \) -newer "$OUT/$DAY.start") | tail -10
} > "$REPORT"
echo "== nightly report: $REPORT"
cat "$REPORT"
[[ $SUITE_RC -eq 0 ]]
