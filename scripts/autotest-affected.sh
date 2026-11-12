#!/usr/bin/env bash
# Run the autotests that the changes since a base commit touch, in parallel (through autotest-all.sh).
# Usage: autotest-affected.sh [--detach] [base-ref] [timeout-per-test]   (base default: origin/main)
#        LIST_ONLY=1 autotest-affected.sh ...   prints the tests and runs nothing.
#        COMMITTED_ONLY=1 ignores uncommitted changes.
#        HEAD_REF=<ref> uses the changes of that ref in place of HEAD (no uncommitted changes); integrate.sh uses it
#        with LIST_ONLY=1 to tell which branch of a batch touches a failed test.
# --detach: run the suite detached (see autotest-all.sh).
# Rule, derived from the folder names (no list to keep up to date):
# - A changed file under src/HotCargo/<A>/<B>/... or mod/<a>/<b>/... touches the areas a, b (lowercase).
# - An area runs the single-player tests named "<area>-*.txt" and every co-op session with a "step <area>.<name>" line.
# - A changed test script runs itself. A changed generic co-op client (tests/autotest-mp/client.txt) runs every session.
# - Docs (*.md, docs/), CI (.github/), scripts and unit tests (tests/HotCargo.Tests) add no autotest beyond core.
# - Anything else (Core, shared UI, root files, a folder with no tests of its own) runs the full suite.
#   Core is shared: every area builds on it, so its own "core" test alone is not enough.
# The "core" smoke test always runs.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/autotest-lib.sh
DETACH=()
if [[ "${1:-}" == "--detach" ]]; then DETACH=(--detach); shift; fi
BASE="${1:-origin/main}"
TIMEOUT="${2:-400}"

# Committed changes since BASE, plus uncommitted ones unless COMMITTED_ONLY=1.
HEAD_REF="${HEAD_REF:-}"
CHANGED="$( { git diff --name-only "$BASE"..."${HEAD_REF:-HEAD}"
  if [[ "${COMMITTED_ONLY:-}" != 1 && -z "$HEAD_REF" ]]; then git diff --name-only HEAD; git ls-files --others --exclude-standard; fi; } | sort -u)"
TESTS=("core")
FULL=no
add() { local x; for x in "${TESTS[@]}"; do [[ "$x" == "$1" ]] && return; done; TESTS+=("$1"); }

# Adds the tests of an area; returns 1 when the area has none.
add_area() {
  local area found=1 f
  area="$(tr '[:upper:]' '[:lower:]' <<<"$1")"
  for f in tests/autotest/"$area"-*.txt; do [[ -f "$f" ]] && { add "$(basename "$f" .txt)"; found=0; }; done
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

if [[ "$FULL" == yes ]]; then
  echo "== shared code changed: full suite"
  [[ "${LIST_ONLY:-}" == 1 ]] && exit 0
  exec scripts/autotest-all.sh "${DETACH[@]}" "$TIMEOUT"
fi
echo "== affected: ${TESTS[*]}"
[[ "${LIST_ONLY:-}" == 1 ]] && exit 0
exec scripts/autotest-all.sh "${DETACH[@]}" "$TIMEOUT" "${TESTS[@]}"
