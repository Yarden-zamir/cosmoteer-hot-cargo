#!/usr/bin/env bash
# Run the autotests that the changes since a base commit touch, in parallel (through autotest-all.sh).
# Usage: autotest-affected.sh [base-ref] [timeout-per-test]   (base default: origin/main)
#        LIST_ONLY=1 autotest-affected.sh ...   prints the tests and runs nothing.
#        COMMITTED_ONLY=1 ignores uncommitted changes.
# Rule, derived from the folder names (no list to keep up to date):
# - A changed file under src/HotCargo/<A>/<B>/... or mod/<a>/<b>/... touches the areas a, b (lowercase).
# - An area runs the single-player tests named "<area>-*.txt" and the co-op tests whose name starts with "<area>".
# - A changed test script runs itself.
# - Docs (*.md, docs/), CI (.github/), scripts and unit tests (tests/HotCargo.Tests) add no autotest beyond core.
# - Anything else (Core, shared UI, root files, a folder with no tests of its own) runs the full suite.
#   Core is shared: every area builds on it, so its own "core" test alone is not enough.
# The "core" smoke test always runs.
# Tiers (tests/TIERS.md): AUTOTEST_TIER=smoke (default) or full. integrate.sh uses full for merge batches.
# - smoke: an area runs only its tests with the line "# tier: smoke" (all of them when it has none), and every co-op test
#   of the area (they guard co-op determinism). "Full suite" runs every smoke test: single player, and co-op with the line.
# - full: the rules above without the filter.
set -euo pipefail
cd "$(dirname "$0")/.."
BASE="${1:-origin/main}"
TIMEOUT="${2:-400}"

# Committed changes since BASE, plus uncommitted ones unless COMMITTED_ONLY=1.
CHANGED="$( { git diff --name-only "$BASE"...HEAD
  if [[ "${COMMITTED_ONLY:-}" != 1 ]]; then git diff --name-only HEAD; git ls-files --others --exclude-standard; fi; } | sort -u)"
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
  for f in tests/autotest-mp/"$area"*.host.txt; do [[ -f "$f" ]] && add "mp:$(basename "$f" .host.txt)"; done
  return $found
}

while IFS= read -r path; do
  [[ -z "$path" ]] && continue
  case "$path" in
    *.md|docs/*|tests/HotCargo.Tests/*|tests/screens/*|tests/balance/*|tests/runmodes/*|tools/*|.claude/*|.github/*) ;;
    # Test and tool scripts do not change the game; the core smoke test (always in the list) runs them.
    scripts/*) ;;
    tests/autotest/*.txt) [[ -f "$path" ]] && add "$(basename "$path" .txt)" ;;
    tests/autotest-mp/*.txt) n="$(basename "$path" .txt)"; n="${n%.host}"; n="${n%.client}"; [[ -f "tests/autotest-mp/$n.host.txt" ]] && add "mp:$n" ;;
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
  for f in tests/autotest-mp/*.host.txt; do if smoke "$f"; then add "mp:$(basename "$f" .host.txt)"; fi; done
  echo "== shared code changed: every smoke test"
fi
if [[ "$FULL" == yes ]]; then
  echo "== shared code changed: full suite"
  [[ "${LIST_ONLY:-}" == 1 ]] && exit 0
  exec scripts/autotest-all.sh "$TIMEOUT"
fi
echo "== affected: ${TESTS[*]}"
[[ "${LIST_ONLY:-}" == 1 ]] && exit 0
exec scripts/autotest-all.sh "$TIMEOUT" "${TESTS[@]}"
