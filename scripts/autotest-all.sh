#!/usr/bin/env bash
# Run autotests in parallel on the free test slots and print a summary.
# Usage: autotest-all.sh [timeout-per-test] [test ...]
# - No test names: every tests/autotest/*.txt and every co-op test (tests/autotest-mp/<name>.host.txt).
# - Test names: only those, for example "tech-sell mp:trade" (autotest-affected.sh passes its list this way).
# Each test retries once (see autotest.sh); a pass on the retry counts as FLAKY, not as a failure.
# Exit code 0 only if every test passed. Full logs: /tmp/hot_cargo_logs/<worktree>/ (summary per test in run/).
set -uo pipefail
cd "$(dirname "$0")/.."
TIMEOUT="${1:-400}"
shift || true
source scripts/autotest-lib.sh
RUN_DIR="$LOG_DIR/run-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$RUN_DIR"
# A clean tree that stays on the same commit gets a pass marker, which scripts/release.sh requires (full runs only).
START_SHA="$(git rev-parse HEAD)"
CLEAN_AT_START="$([[ -z "$(git status --porcelain)" && $# -eq 0 ]] && echo yes || echo no)"

TESTS=()
if [[ $# -gt 0 ]]; then TESTS=("$@"); else
  for t in tests/autotest/*.txt; do TESTS+=("$(basename "$t" .txt)"); done
  for h in tests/autotest-mp/*.host.txt; do TESTS+=("mp:$(basename "$h" .host.txt)"); done
fi

# Build once before the parallel runs, so the first deploys do not all wait on the build lock.
dotnet build src/HotCargo/HotCargo.csproj -c Release -nologo -v quiet > "$RUN_DIR/build.log" 2>&1 || { cat "$RUN_DIR/build.log"; echo "== build failed"; exit 1; }

run_test() {
  local t="$1" out="$RUN_DIR/$1.out"
  if [[ "$t" == mp:* ]]; then
    local n="${t#mp:}"
    scripts/autotest-mp.sh "tests/autotest-mp/$n.host.txt" "tests/autotest-mp/$n.client.txt" "$((TIMEOUT + 200))" > "$out" 2>&1
  else
    scripts/autotest.sh "tests/autotest/$t.txt" "$TIMEOUT" > "$out" 2>&1
  fi
  local rc=$?
  if [[ $rc -ne 0 ]]; then echo "FAIL $t"; elif grep -q "AUTOTEST FLAKY" "$out"; then echo "FLAKY $t"; else echo "PASS $t"; fi
}

# At most SLOTS tests wait for slots at the same time; take_slots hands out the slots.
{
  for t in "${TESTS[@]}"; do
    while [[ $(jobs -rp | wc -l) -ge $SLOTS ]]; do sleep 2; done
    run_test "$t" &
  done
  wait
} | tee "$RUN_DIR/summary.txt"

FAILED="$(grep '^FAIL ' "$RUN_DIR/summary.txt" | cut -d' ' -f2- | tr '\n' ' ')"
FLAKY="$(grep '^FLAKY ' "$RUN_DIR/summary.txt" | cut -d' ' -f2- | tr '\n' ' ')"
[[ -n "$FLAKY" ]] && echo "== flaky (passed on retry): $FLAKY"
if [[ -z "$FAILED" ]]; then
  echo "== all $(grep -c . "$RUN_DIR/summary.txt") autotests passed ($RUN_DIR)"
  if [[ "$CLEAN_AT_START" == yes && "$(git rev-parse HEAD)" == "$START_SHA" && -z "$(git status --porcelain)" ]]; then
    touch "/tmp/hot_cargo_autotest_pass_$START_SHA"; echo "== pass marker written for $START_SHA"
  fi
  exit 0
fi
for t in $FAILED; do echo "--- $t"; grep -A60 "first run failed\|retry failed\|MP AUTOTEST FAILED" "$RUN_DIR/$t.out" | head -40; done
echo "== failed: $FAILED ($RUN_DIR)"; exit 1
