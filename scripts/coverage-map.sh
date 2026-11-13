#!/usr/bin/env bash
# Refresh the test impact map: run every game test on this Mac with coverage and write <container>/.state/coverage/map.json.
# Usage: coverage-map.sh [timeout-per-test]. Needs a clean tree: the map holds line numbers of the committed HEAD.
# scripts/autotest-affected.sh selects tests with the map (tools/test-impact.py); scripts/nightly.sh refreshes it.
# Uses the Mac slots and HOT_CARGO_MAC_JOBS like scripts/autotest-mac.sh. A failed run still adds its coverage.
set -uo pipefail
cd "$(dirname "$0")/.."
source scripts/autotest-lib.sh
[[ -z "$(git status --porcelain --untracked-files=no -- src)" ]] || { echo "coverage-map: commit the changes in src first"; exit 1; }
SHA="$(git rev-parse HEAD)"
OUT="$STATE_DIR/coverage"
DIR="$OUT/run-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$DIR"
TESTS=()
for t in tests/autotest/*.txt; do TESTS+=("$(basename "$t" .txt)"); done
for n in $(coop_sessions); do TESTS+=("mp:$n"); done
T0=$SECONDS
HOT_CARGO_COVERAGE_DIR="$DIR" scripts/autotest-mac.sh "${1:-400}" "${TESTS[@]}" > "$DIR/suite.txt" 2>&1
tail -3 "$DIR/suite.txt"
uv run tools/test-impact.py build "$DIR" "$OUT/map.json.new" "$SHA" && mv "$OUT/map.json.new" "$OUT/map.json"
echo "== coverage map at ${SHA:0:9}: $OUT/map.json ($((SECONDS - T0)) s)"
