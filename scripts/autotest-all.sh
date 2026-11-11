#!/usr/bin/env bash
# Run every tests/autotest/*.txt one after another (one worktree runs one autotest at a time) and print a summary.
# Usage: autotest-all.sh [timeout-per-test]
# Exit code 0 only if every test passed. Full logs go to /tmp/hot_cargo_autotest_<name>.log.
set -uo pipefail
cd "$(dirname "$0")/.."
TIMEOUT="${1:-400}"
FAILED=()
for t in tests/autotest/*.txt; do
  name="$(basename "$t" .txt)"
  if scripts/autotest.sh "$t" "$TIMEOUT" > "/tmp/hot_cargo_autotest_$name.log" 2>&1; then echo "PASS $name"; else echo "FAIL $name"; FAILED+=("$name"); fi
done
if [[ ${#FAILED[@]} -eq 0 ]]; then echo "== all autotests passed"; exit 0; fi
echo "== failed: ${FAILED[*]}"; exit 1
