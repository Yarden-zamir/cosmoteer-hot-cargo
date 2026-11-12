#!/usr/bin/env bash
# Run every tests/autotest/*.txt one after another (one worktree runs one autotest at a time), then every co-op test
# (scripts/autotest-mp-all.sh), and print a summary.
# Usage: autotest-all.sh [--telemetry] [timeout-per-test]
# --telemetry: after the tests, copy the telemetry of every slot to ../.telemetry (scripts/pull-telemetry.sh) and write a report
#   of the runs that these tests made (tools/telemetry). The report path is printed at the end.
# Exit code 0 only if every test passed. Full logs go to /tmp/hot_cargo_autotest_<worktree>_<name>.log.
set -uo pipefail
cd "$(dirname "$0")/.."
# Each worktree writes its own logs, so parallel runs from different worktrees do not mix.
WT="$(basename "$(pwd)")"
TELEMETRY=no
if [[ "${1:-}" == "--telemetry" ]]; then TELEMETRY=yes; shift; fi
TIMEOUT="${1:-400}"
STARTED_UTC="$(date -u +%Y-%m-%dT%H:%M:%S)"
FAILED=()
# A clean tree that stays on the same commit gets a pass marker, which scripts/release.sh requires.
START_SHA="$(git rev-parse HEAD)"
CLEAN_AT_START="$([[ -z "$(git status --porcelain)" ]] && echo yes || echo no)"
for t in tests/autotest/*.txt; do
  name="$(basename "$t" .txt)"
  if scripts/autotest.sh "$t" "$TIMEOUT" > "/tmp/hot_cargo_autotest_${WT}_$name.log" 2>&1; then echo "PASS $name"; else echo "FAIL $name"; FAILED+=("$name"); fi
done
# Co-op tests take two slots each. They are part of a full pass, so a release is checked in multiplayer too.
if ! scripts/autotest-mp-all.sh; then FAILED+=("co-op"); fi
if [[ "$TELEMETRY" == yes ]]; then
  scripts/pull-telemetry.sh >/dev/null && (cd tools/telemetry && uv run hc-telemetry --since "$STARTED_UTC") || echo "== telemetry report failed"
fi
if [[ ${#FAILED[@]} -eq 0 ]]; then
  echo "== all autotests passed"
  if [[ "$CLEAN_AT_START" == yes && "$(git rev-parse HEAD)" == "$START_SHA" && -z "$(git status --porcelain)" ]]; then
    touch "/tmp/hot_cargo_autotest_pass_$START_SHA"; echo "== pass marker written for $START_SHA"
  fi
  exit 0
fi
echo "== failed: ${FAILED[*]}"; exit 1
