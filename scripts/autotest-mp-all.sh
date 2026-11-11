#!/usr/bin/env bash
# Run every co-op test (tests/autotest-mp/<name>.host.txt with <name>.client.txt) one after another and print a summary.
# Usage: autotest-mp-all.sh [timeout-per-test]
# Exit code 0 only if every test passed. Full logs go to /tmp/hot_cargo_autotest_<worktree>_mp_<name>.log.
set -uo pipefail
cd "$(dirname "$0")/.."
# Each worktree writes its own logs, so parallel runs from different worktrees do not mix.
WT="$(basename "$(pwd)")"
TIMEOUT="${1:-600}"
FAILED=()
for host in tests/autotest-mp/*.host.txt; do
  name="$(basename "$host" .host.txt)"
  client="tests/autotest-mp/$name.client.txt"
  if [[ ! -f "$client" ]]; then echo "FAIL $name (no $client)"; FAILED+=("$name"); continue; fi
  if scripts/autotest-mp.sh "$host" "$client" "$TIMEOUT" > "/tmp/hot_cargo_autotest_${WT}_mp_$name.log" 2>&1; then echo "PASS mp $name"; else echo "FAIL mp $name"; FAILED+=("$name"); fi
done
if [[ ${#FAILED[@]} -eq 0 ]]; then echo "== all co-op tests passed"; exit 0; fi
echo "== co-op failed: ${FAILED[*]}"; exit 1
