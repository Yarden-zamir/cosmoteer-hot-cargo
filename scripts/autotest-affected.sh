#!/usr/bin/env bash
# Run the autotests that the changes since a base commit touch, in parallel (through autotest-all.sh).
# Usage: autotest-affected.sh [--detach] [base-ref] [timeout-per-test]   (base default: origin/main)
#        LIST_ONLY=1 autotest-affected.sh ...   prints the tests and runs nothing.
#        COMMITTED_ONLY=1 ignores uncommitted changes.
#        HEAD_REF=<ref> uses the changes of that ref in place of HEAD (no uncommitted changes); integrate.sh uses it
#        with LIST_ONLY=1 to tell which branch of a batch touches a failed test.
# --detach: run the suite detached (see autotest-all.sh).
# Runner: tests run on this Mac (scripts/autotest-mac.sh; co-op sessions as two sim processes on 127.0.0.1), and tests with
# the line "# runner: pc" on the PC (scripts/autotest-all.sh), both at the same time.
#   AUTOTEST_MAC=0 runs every test on the PC. Without the game files on the Mac (scripts/pull-game.sh), every test runs on
#   the PC. integrate.sh uses the Mac (the merge gate); scripts/nightly.sh runs the full PC suite.
# C# changes: the test impact map (<container>/.state/coverage/map.json, tools/test-impact.py) selects the tests that run a
# changed method. A change that the map cannot attribute (outside every method, a new file, a method no test runs) falls
# back to the folder rule below. Without a map, or with a map from a commit that is not an ancestor, every C# change uses
# the folder rule. AUTOTEST_IMPACT=0 turns the map off.
# Rules files: mod/<area>/... runs the area's tests (folder rule), and scripts/check-rules.sh loads every rules file once
# on the Mac before the tests (it catches syntax and reference errors). Root rules files (tunables.rules, mod.header.rules)
# change every test's setup: full set.
# Folder rule, derived from the folder names (no list to keep up to date):
# - A changed file under src/HotCargo/<A>/<B>/... or mod/<a>/<b>/... touches the areas a, b (lowercase).
# - An area runs the single-player tests named "<area>-*.txt" and every co-op session with a "step <area>.<name>" line.
# - A changed test script runs itself. A changed generic co-op client (tests/autotest-mp/client.txt) runs every session.
# - Docs (*.md, docs/), CI (.github/), scripts and unit tests (tests/HotCargo.Tests) add no autotest beyond core.
# - Anything else (Core, shared UI, root files, a folder with no tests of its own) runs the full set.
#   Core is shared: every area builds on it, so its own "core" test alone is not enough.
# The full set leaves out tests with "# budget: exempt" (the real playthrough runs on its own). A failed test in the flake
# quarantine (scripts/gate-lib.sh) is reported but does not fail the run. The whole run checks the suite time budget.
# The "core" smoke test always runs.
# Tiers (tests/TIERS.md): AUTOTEST_TIER=smoke (default) or full. integrate.sh uses full for merge batches.
# - smoke: an area runs only its tests with the line "# tier: smoke" (all of them when it has none), and every co-op
#   session with a step of the area (they guard co-op determinism). "Full suite" runs every smoke test: single player,
#   and the co-op sessions with the line.
# - full: the rules above without the filter.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/autotest-lib.sh
source scripts/gate-lib.sh
if [[ "${1:-}" == "--detach" ]]; then
  shift
  LOG="$(scripts/detach.sh "autotest-affected-$(basename "$PWD")" scripts/autotest-affected.sh "$@")"
  echo "== suite runs detached. Log: $LOG"
  echo "== wait for it: scripts/suite-wait.sh $LOG"
  exit 0
fi
BASE="${1:-origin/main}"
TIMEOUT="${2:-400}"

# Committed changes since BASE, plus uncommitted ones unless COMMITTED_ONLY=1.
HEAD_REF="${HEAD_REF:-}"
CHANGED="$( { git diff --name-only "$BASE"..."${HEAD_REF:-HEAD}"
  if [[ "${COMMITTED_ONLY:-}" != 1 && -z "$HEAD_REF" ]]; then git diff --name-only HEAD; git ls-files --others --exclude-standard; fi; } | sort -u)"
TESTS=("core")
FULL=no
TIER="${AUTOTEST_TIER:-smoke}"
[[ "$TIER" == smoke || "$TIER" == full ]] || { echo "AUTOTEST_TIER must be smoke or full (got '$TIER')" >&2; exit 2; }
smoke() { grep -qx '# tier: smoke' "$1"; }
add() { local x; for x in "${TESTS[@]}"; do [[ "$x" == "$1" ]] && return; done; TESTS+=("$1"); }

# Adds the tests of an area; returns 1 when the area has none.
add_area() {
  local area found=1 f
  area="$(tr '[:upper:]' '[:lower:]' <<<"$1")"
  local smoke_only=no
  if [[ "$TIER" == smoke ]]; then
    for f in tests/autotest/"$area"-*.txt; do if [[ -f "$f" ]] && smoke "$f"; then smoke_only=yes; fi; done
  fi
  for f in tests/autotest/"$area"-*.txt; do
    [[ -f "$f" ]] || continue
    found=0
    if [[ "$smoke_only" == no ]] || smoke "$f"; then add "$(basename "$f" .txt)"; fi
  done
  for n in $(coop_sessions); do grep -q "^step $area\." "tests/autotest-mp/$n.txt" && { add "mp:$n"; found=0; }; done
  return $found
}

# Folder rule for one path: its folders are areas; Core or a folder with no tests means the full set.
by_folder() {
  local rest dirs any=no part parts
  [[ "$1" == src/HotCargo/Core/* ]] && { FULL=yes; return; }
  rest="${1#src/HotCargo/}"; rest="${rest#mod/}"
  dirs="$(dirname "$rest")"
  [[ "$dirs" == . ]] && { FULL=yes; return; }
  IFS='/' read -r -a parts <<<"$dirs"
  for part in "${parts[@]}"; do add_area "$part" && any=yes; done
  [[ "$any" == no ]] && FULL=yes
  return 0
}

# Test impact map: tests per changed C# method (tools/test-impact.py). MAPPED holds the selection, one line per test or
# "FALLBACK <path>[:line]: reason"; mapped <path> adds the tests for a path and the folder rule for its fallbacks.
MAP="$STATE_DIR/coverage/map.json" MAPPED="" RULES_CHANGED=no
if [[ "${AUTOTEST_IMPACT:-1}" != 0 && -f "$MAP" ]]; then
  MAP_SHA="$(uv run python -I -c 'import json, sys; print(json.load(open(sys.argv[1]))["sha"])' "$MAP")"
  if git merge-base --is-ancestor "$MAP_SHA" "${HEAD_REF:-HEAD}" 2>/dev/null; then
    MAPPED="$(uv run tools/test-impact.py select "$MAP" ${HEAD_REF:+"$HEAD_REF"})"
    echo "== test impact map from ${MAP_SHA:0:9}: $(grep -vc '^FALLBACK' <<<"$MAPPED" || true) tests for C# changes, $(grep -c '^FALLBACK' <<<"$MAPPED" || true) fallbacks"
    grep '^FALLBACK' <<<"$MAPPED" | sed 's/^/   /' || true
  else
    echo "== test impact map ${MAP_SHA:0:9} is not an ancestor of ${HEAD_REF:-HEAD}: folder rule only"
  fi
fi
mapped() {
  local t
  while IFS= read -r t; do
    [[ -z "$t" ]] && continue
    if [[ "$t" == "FALLBACK $1"* ]]; then by_folder "$1"; break; fi
  done <<<"$MAPPED"
  return 0
}

while IFS= read -r path; do
  [[ -z "$path" ]] && continue
  case "$path" in
    *.md|docs/*|tests/HotCargo.Tests/*|tests/screens/*|tests/balance/*|tests/runmodes/*|tools/*|.claude/*|.github/*) ;;
    # Test and tool scripts do not change the game; the core smoke test (always in the list) runs them.
    scripts/*) ;;
    tests/autotest/*.txt) [[ -f "$path" ]] && add "$(basename "$path" .txt)" ;;
    tests/autotest-mp/client.txt) for n in $(coop_sessions); do add "mp:$n"; done ;;
    tests/autotest-mp/*.txt) [[ -f "$path" ]] && add "mp:$(basename "$path" .txt)" ;;
    src/HotCargo/*.cs|src/HotCargo/*/*.cs) if [[ -n "$MAPPED" ]] && mapped "$path"; then :; else by_folder "$path"; fi ;;
    mod/strings/*) RULES_CHANGED=yes ;;
    src/HotCargo/*/*|mod/*/*)
      [[ "$path" == mod/* ]] && RULES_CHANGED=yes
      by_folder "$path" ;;
    *) FULL=yes ;;
  esac
done <<<"$CHANGED"
# Tests that run a changed C# method.
while IFS= read -r t; do [[ -n "$t" && "$t" != FALLBACK* ]] && add "$t"; done <<<"$MAPPED"

if [[ "$FULL" == yes && "$TIER" == smoke ]]; then
  FULL=no
  for f in tests/autotest/*.txt; do if smoke "$f"; then add "$(basename "$f" .txt)"; fi; done
  for n in $(coop_sessions); do if smoke "tests/autotest-mp/$n.txt"; then add "mp:$n"; fi; done
  echo "== shared code changed: every smoke test"
fi
# The full set: every test and session except the budget-exempt ones (the real playthrough runs on its own).
if [[ "$FULL" == yes ]]; then
  TESTS=()
  for f in tests/autotest/*.txt; do budget_exempt "$f" || TESTS+=("$(basename "$f" .txt)"); done
  for n in $(coop_sessions); do TESTS+=("mp:$n"); done
  echo "== shared code changed: full set (${#TESTS[@]} tests)"
fi
echo "== affected: ${TESTS[*]}"
[[ "${LIST_ONLY:-}" == 1 ]] && exit 0
T0=$SECONDS

mac_ready() { command -v dotnet >/dev/null && [[ -n "$(find ../.game/Data -name '*.ship.png' -print -quit 2>/dev/null)" ]]; }
if [[ "${AUTOTEST_MAC:-1}" == 0 ]] || ! mac_ready; then
  [[ "${AUTOTEST_MAC:-1}" == 0 ]] || echo "== no game files on this Mac (scripts/pull-game.sh): every test runs on the PC"
  exec scripts/autotest-all.sh "$TIMEOUT" "${TESTS[@]}"
fi

# A rules change: load every rules file once before the tests (about 20 s; scripts/check-rules.sh).
if [[ "$RULES_CHANGED" == yes || "$FULL" == yes ]]; then scripts/check-rules.sh || exit 1; fi
MAC=() PC=()
for t in "${TESTS[@]}"; do
  if [[ "$t" == mp:* ]]; then f="tests/autotest-mp/${t#mp:}.txt"; else f="tests/autotest/$t.txt"; fi
  if ! grep -qx '# runner: pc' "$f"; then MAC+=("$t"); else PC+=("$t"); fi
done
echo "== on the Mac: ${MAC[*]:-none}"
echo "== on the PC: ${PC[*]:-none}"
PC_OUT="$(mktemp)" PC_PID="" rc=0
if [[ ${#PC[@]} -gt 0 ]]; then scripts/autotest-all.sh "$TIMEOUT" "${PC[@]}" > "$PC_OUT" 2>&1 & PC_PID=$!; fi
MAC_OUT="$(mktemp)"
if [[ ${#MAC[@]} -gt 0 ]]; then scripts/autotest-mac.sh "$TIMEOUT" "${MAC[@]}" 2>&1 | tee "$MAC_OUT" || rc=1; fi
if [[ -n "$PC_PID" ]]; then
  echo "== waiting for the PC tests"
  wait "$PC_PID" || rc=1
  cat "$PC_OUT"
fi
# Failed tests; those in the flake quarantine for their runner do not fail the run (scripts/gate-lib.sh).
BLOCKING=() QUARANTINED=()
for t in $(grep -h '^FAIL ' "$MAC_OUT" | cut -d' ' -f2-); do if is_quarantined "$t" mac; then QUARANTINED+=("$t"); else BLOCKING+=("$t"); fi; done
for t in $(grep -h '^FAIL ' "$PC_OUT" | cut -d' ' -f2-); do if is_quarantined "$t" pc; then QUARANTINED+=("$t"); else BLOCKING+=("$t"); fi; done
rm -f "$MAC_OUT" "$PC_OUT"
[[ ${#QUARANTINED[@]} -gt 0 ]] && echo "== failed but quarantined (not blocking; scripts/nightly.sh tracks them): ${QUARANTINED[*]}"
BUDGET_OK=yes
check_budget "affected run of ${#TESTS[@]} tests" "$((SECONDS - T0))" "${TESTS[@]}" || BUDGET_OK=no
if [[ ${#BLOCKING[@]} -gt 0 ]]; then echo "== failed: ${BLOCKING[*]} (Mac and PC)"; exit 1; fi
# A runner that failed with no failed test (a build error, its own budget, the PC link) blocks too.
if [[ $rc -ne 0 && ${#QUARANTINED[@]} -eq 0 ]]; then echo "== failed: a runner failed with no failed test (build, budget or PC link; see above)"; exit 1; fi
[[ "$BUDGET_OK" == yes ]] || exit 1
echo "== all affected tests passed"
