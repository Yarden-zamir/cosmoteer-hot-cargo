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
#   AUTOTEST_MAC=0 runs every test on the PC. integrate.sh sets it: the PC suite stays the merge gate.
#   Without the game files on the Mac (scripts/pull-game.sh), every test runs on the PC.
# Rule, derived from the folder names (no list to keep up to date):
# - A changed file under src/HotCargo/<A>/<B>/... or mod/<a>/<b>/... touches the areas a, b (lowercase).
# - An area runs the single-player tests named "<area>-*.txt" and every co-op session with a "step <area>.<name>" line.
# - A changed test script runs itself. A changed generic co-op client (tests/autotest-mp/client.txt) runs every session.
# - Docs (*.md, docs/), CI (.github/), scripts and unit tests (tests/HotCargo.Tests) add no autotest beyond core.
# - Anything else (Core, shared UI, root files, a folder with no tests of its own) runs the full suite.
#   Core is shared: every area builds on it, so its own "core" test alone is not enough.
# The "core" smoke test always runs.
# Tiers (tests/TIERS.md): AUTOTEST_TIER=smoke (default) or full. integrate.sh uses full for merge batches.
# - smoke: an area runs only its tests with the line "# tier: smoke" (all of them when it has none), and every co-op
#   session with a step of the area (they guard co-op determinism). "Full suite" runs every smoke test: single player,
#   and the co-op sessions with the line.
# - full: the rules above without the filter.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/autotest-lib.sh
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

while IFS= read -r path; do
  [[ -z "$path" ]] && continue
  case "$path" in
    *.md|docs/*|tests/HotCargo.Tests/*|tests/screens/*|tests/balance/*|tests/runmodes/*|tools/*|.claude/*|.github/*) ;;
    # Test and tool scripts do not change the game; the core smoke test (always in the list) runs them.
    scripts/*) ;;
    tests/autotest/*.txt) [[ -f "$path" ]] && add "$(basename "$path" .txt)" ;;
    tests/autotest-mp/client.txt) for n in $(coop_sessions); do add "mp:$n"; done ;;
    tests/autotest-mp/*.txt) [[ -f "$path" ]] && add "mp:$(basename "$path" .txt)" ;;
    src/HotCargo/Core/*) FULL=yes ;;
    src/HotCargo/*/*|mod/*/*)
      rest="${path#src/HotCargo/}"; rest="${rest#mod/}"
      dirs="$(dirname "$rest")"
      any=no
      IFS='/' read -r -a parts <<<"$dirs"
      for part in "${parts[@]}"; do add_area "$part" && any=yes; done
      [[ "$any" == no ]] && FULL=yes ;;
    *) FULL=yes ;;
  esac
done <<<"$CHANGED"

if [[ "$FULL" == yes && "$TIER" == smoke ]]; then
  FULL=no
  for f in tests/autotest/*.txt; do if smoke "$f"; then add "$(basename "$f" .txt)"; fi; done
  for n in $(coop_sessions); do if smoke "tests/autotest-mp/$n.txt"; then add "mp:$n"; fi; done
  echo "== shared code changed: every smoke test"
fi
if [[ "$FULL" == yes ]]; then
  echo "== shared code changed: full suite"
  [[ "${LIST_ONLY:-}" == 1 ]] && exit 0
else
  echo "== affected: ${TESTS[*]}"
  [[ "${LIST_ONLY:-}" == 1 ]] && exit 0
fi

mac_ready() { command -v dotnet >/dev/null && [[ -n "$(find ../.game/Data -name '*.ship.png' -print -quit 2>/dev/null)" ]]; }
if [[ "${AUTOTEST_MAC:-1}" == 0 ]] || ! mac_ready; then
  [[ "${AUTOTEST_MAC:-1}" == 0 ]] || echo "== no game files on this Mac (scripts/pull-game.sh): every test runs on the PC"
  if [[ "$FULL" == yes ]]; then exec scripts/autotest-all.sh "$TIMEOUT"; fi
  exec scripts/autotest-all.sh "$TIMEOUT" "${TESTS[@]}"
fi

if [[ "$FULL" == yes ]]; then
  TESTS=()
  for f in tests/autotest/*.txt; do TESTS+=("$(basename "$f" .txt)"); done
  for n in $(coop_sessions); do TESTS+=("mp:$n"); done
fi
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
# One line with every failed test, in the format of autotest-all.sh.
FAILED="$(grep -h '^FAIL ' "$MAC_OUT" "$PC_OUT" | cut -d' ' -f2- | tr '\n' ' ' || true)"
rm -f "$MAC_OUT" "$PC_OUT"
if [[ $rc -ne 0 ]]; then echo "== failed: ${FAILED% } (Mac and PC)"; exit 1; fi
echo "== all affected tests passed"
