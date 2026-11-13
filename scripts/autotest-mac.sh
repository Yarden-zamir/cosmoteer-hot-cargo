#!/usr/bin/env bash
# Run autotests on this Mac in the headless sim host (tools/mac-sim), in parallel, and print a summary.
# Usage: autotest-mac.sh [timeout-per-test] [test ...]
# - No test names: every tests/autotest/*.txt. Co-op sessions run with their names (mp:<session>, scripts/autotest-mac-mp.sh):
#   two processes on 127.0.0.1 with the real-time clock. A session takes 2 slots and has no retry (its steps report on their own).
# - Same pass rule as autotest.sh: the log has "telename <name>", AUTOTEST DONE, and no AUTOTEST FAIL or ERROR line.
# - Same log place as the PC runner: <container>/.state/logs/<worktree>/<name>.<time>.mac.log, summary in run-mac-<time>/.
# - Retries a failed run once, like autotest.sh (MAC_RETRY=0: no retry). A pass on the retry prints FLAKY, goes to
#   ../.state/flaky.log and puts the test in the flake quarantine (scripts/gate-lib.sh).
# - Batches: single-player tests run HOT_CARGO_MAC_BATCH at a time (default 6) in one game process (the "test" command),
#   so the process start and the rules load happen once per batch. A test with "# batch: alone" (it changes game settings
#   that the next test would see) runs on its own. A test that fails in a batch runs again alone; that run decides, and a
#   pass alone is reported as a batch flake.
# - Speed: each test runs at the game speed of scripts/autotest-lib.sh test_speed (HOT_CARGO_TEST_SPEED or its "# speed:"
#   line). A test with "# clock: real" runs with the real-time clock (MAC_SIM_REALTIME=1), like the PC.
# - Prints the slowest tests and checks the suite time budget (scripts/gate-lib.sh).
#   The fixed-step clock repeats most sim values, but background work (jumps, spawns, the telemetry writer) runs in real time.
# - Parallel runs: HOT_CARGO_MAC_JOBS, default from the cores and the memory (see below). The limit is for the whole Mac:
#   runs of every worktree share the slot locks /tmp/hot_cargo_mac_slot_<n>.lock.
# Needs the game files in ../.game (scripts/pull-game.sh). Exit code 0 only if every test passed.
set -uo pipefail
cd "$(dirname "$0")/.."
TIMEOUT="${1:-400}"
shift || true
source scripts/autotest-lib.sh
source scripts/coverage-lib.sh
source scripts/gate-lib.sh
# HOT_CARGO_COVERAGE_DIR=<dir>: each run also writes <dir>/<test>.xml (method coverage of HotCargo.dll) for the test impact
# map (scripts/coverage-map.sh). Runs are then not retried.
COVERAGE_DIR="${HOT_CARGO_COVERAGE_DIR:-}"

TESTS=()
if [[ $# -gt 0 ]]; then TESTS=("$@"); else for t in tests/autotest/*.txt; do TESTS+=("$(basename "$t" .txt)"); done; fi
for t in "${TESTS[@]}"; do
  if [[ "$t" == mp:* ]]; then f="tests/autotest-mp/${t#mp:}.txt"; else f="tests/autotest/$t.txt"; fi
  [[ -f "$f" ]] || { echo "autotest-mac: no test $t ($f)" >&2; exit 2; }
done

# One run takes about one core and 2 GB at its peak. Two cores stay free for the agents and the build.
CORES="$(sysctl -n hw.ncpu)"
MEM_GB=$(( $(sysctl -n hw.memsize) / 1024 / 1024 / 1024 ))
JOBS="${HOT_CARGO_MAC_JOBS:-$(( CORES - 2 < MEM_GB / 2 ? CORES - 2 : MEM_GB / 2 ))}"
(( JOBS >= 1 )) || JOBS=1

RUN_DIR="$LOG_DIR/run-mac-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$RUN_DIR"
SUITE_START=$SECONDS
STAGE="$RUN_DIR/stage"
{ dotnet build tools/mac-sim/MacSim.csproj -c Release -nologo -v quiet -clp:ErrorsOnly && scripts/stage.sh "$STAGE"; } > "$RUN_DIR/build.log" 2>&1 \
  || { cat "$RUN_DIR/build.log"; echo "== build failed"; exit 1; }

# Takes $2 free Mac slots at once (all or none, so two co-op runs cannot deadlock), writes the owner pid $1 into each and
# prints their lock folders. A co-op session runs two game processes.
take_mac_slots() {
  local i lock got=()
  while :; do
    got=()
    for ((i = 1; i <= JOBS && ${#got[@]} < $2; i++)); do
      lock="/tmp/hot_cargo_mac_slot_$i.lock"
      if lock_is_stale "$lock"; then rm -rf "$lock"; fi
      # A slot counts only when its pid landed: another taker's stale cleanup can remove a lock between mkdir and the write.
      if mkdir "$lock" 2>/dev/null && echo "$1" > "$lock/pid.tmp" 2>/dev/null && mv -f "$lock/pid.tmp" "$lock/pid" 2>/dev/null; then got+=("$lock"); fi
    done
    if [[ ${#got[@]} -eq $2 ]]; then echo "${got[*]}"; return; fi
    rm -rf "${got[@]}"
    sleep 2
  done
}

# One co-op session. Prints its result line; returns 0 on a pass.
run_mp() {
  local name="${1#mp:}" locks out rc=0
  locks="$(take_mac_slots "$BASHPID" 2)"
  out="$RUN_DIR/$1.out"
  local stage="$STAGE" cov=""
  if [[ -n "$COVERAGE_DIR" ]] && cov="$(coverage_begin "$STAGE" "$1")"; then stage="${cov#* }"; fi
  MAC_SIM_STAGE="$stage" scripts/autotest-mac-mp.sh "tests/autotest-mp/$name.txt" "$((TIMEOUT + 200))" > "$out" 2>&1 || rc=$?
  [[ -n "$cov" ]] && coverage_end "${cov%% *}" "$stage" "$COVERAGE_DIR/$1.xml"
  rm -rf $locks
  return $rc
}

# One run. Prints the log path; returns 0 on a pass.
run_once() {
  local name="$1" log save lock rc=0 t0=$SECONDS
  # $BASHPID: this run's process. take_mac_slots runs in a subshell that exits at once.
  lock="$(take_mac_slots "$BASHPID" 1)"
  log="$LOG_DIR/$name.$(date +%Y%m%d-%H%M%S).mac.log"
  save="$(mktemp -d)"
  { echo "telename $name"; echo "defaultspeed $(test_speed "tests/autotest/$name.txt")"; cat "tests/autotest/$name.txt"; } > "$save/script.txt"
  local stage="$STAGE" cov=""
  if [[ -n "$COVERAGE_DIR" ]] && cov="$(coverage_begin "$STAGE" "$name")"; then stage="${cov#* }"; fi
  MAC_SIM_REALTIME="$(clock_real "$name")" MAC_SIM_STAGE="$stage" MAC_SIM_SAVE="$save" timeout "$TIMEOUT" tools/mac-sim/run.sh autotest "$save/script.txt" > "$log" 2>&1 || rc=$?
  [[ -n "$cov" ]] && coverage_end "${cov%% *}" "$stage" "$COVERAGE_DIR/$name.xml"
  rm -rf "$lock"
  { echo; echo "=== hot_cargo_log.txt"; cat "$save/Mods/hot_cargo/csharp/hot_cargo_log.txt" 2>/dev/null; } >> "$log"
  [[ $rc -eq 124 ]] && echo "== timeout after $TIMEOUT s" >> "$log"
  echo "== timing $name total=$((SECONDS - t0))" >> "$log"
  rm -rf "$save"
  echo "$log"
  [[ $rc -eq 0 ]] && log_passed < "$log" && grep -qF "telename $name" "$log"
}

# 1 for a test with "# clock: real" (it measures real time), else empty.
clock_real() { grep -qx '# clock: real' "tests/autotest/$1.txt" && echo 1 || true; }

# A batch of single-player tests in one game process. Prints PASS/FAIL per test; a failed one runs again alone (run_test).
run_batch() {
  local names=("$@") lock save log t rc=0 t0=$SECONDS start passed
  lock="$(take_mac_slots "$BASHPID" 1)"
  save="$(mktemp -d)"
  log="$LOG_DIR/batch-${names[0]}.$(date +%Y%m%d-%H%M%S).mac.log"
  # Each test: "test <name>", its telename and speed, its lines without "quit". "end" logs the totals and quits.
  for t in "${names[@]}"; do
    echo "test $t"; echo "telename $t"; echo "defaultspeed $(test_speed "tests/autotest/$t.txt")"
    grep -v '^quit$' "tests/autotest/$t.txt"
  done > "$save/script.txt"
  echo "end" >> "$save/script.txt"
  MAC_SIM_STAGE="$STAGE" MAC_SIM_SAVE="$save" timeout "$((TIMEOUT * ${#names[@]}))" tools/mac-sim/run.sh autotest "$save/script.txt" > "$log" 2>&1 || rc=$?
  rm -rf "$lock"
  local hc="$save/Mods/hot_cargo/csharp/hot_cargo_log.txt"
  { echo; echo "=== hot_cargo_log.txt"; cat "$hc" 2>/dev/null; } >> "$log"
  rm -rf "$save"
  # One log per test with its part of the batch log and a timing line, like a single run (tools/test-audit.py reads them).
  for t in "${names[@]}"; do
    # ".batch": a rerun alone in the same second must not overwrite it.
    local tl="$LOG_DIR/$t.$(date +%Y%m%d-%H%M%S).batch.mac.log" secs
    awk -v t="$t" '/AUTOTEST STEP START / { on = ($NF == "tick=-" && $(NF-1) == t) } on' "$log" > "$tl"
    secs="$(awk -v t="$t" '/AUTOTEST STEP START / { if ($(NF-1) == t) a = $1; else if (a != "" && b == "") b = $1 }
      /AUTOTEST STEPS / && a != "" && b == "" { b = $1 }
      END { if (a == "" || b == "") { print 0; exit } split(a, x, ":"); split(b, y, ":");
            d = (y[1] * 3600 + y[2] * 60 + y[3]) - (x[1] * 3600 + x[2] * 60 + x[3]); print int(d < 0 ? d + 86400 : d) }' "$log")"
    echo "== timing $t total=$secs (batch $log)" >> "$tl"
    if grep -q "AUTOTEST STEP RESULT $t PASS" "$log" && ! grep -v "AUTOTEST STEP FAIL" "$tl" | grep -q " ERROR "; then
      echo "AUTOTEST DONE (end of this test in the batch)" >> "$tl"
      echo "PASS $t"
    else
      passed="$(run_test "$t")"
      if [[ "$passed" == "PASS $t" ]]; then
        echo "$(date +%F\ %T) $t $tl (failed in a batch, passed alone)" >> "$STATE_DIR/flaky.log"
        echo "FLAKY $t"
      else echo "$passed"; fi
    fi
  done
}

run_test() {
  local name="$1" first log
  if [[ "$name" == mp:* ]]; then
    if run_mp "$name"; then echo "PASS $name"; else echo "FAIL $name"; fi
    return
  fi
  if first="$(run_once "$name")"; then echo "PASS $name"; return; fi
  if [[ -n "$COVERAGE_DIR" || "${MAC_RETRY:-1}" == 0 ]]; then
    { echo "== AUTOTEST FAILED $name (log $first)"; failure_lines "$first"; } > "$RUN_DIR/$name.out"
    echo "FAIL $name"; return
  fi
  if log="$(run_once "$name")"; then
    echo "$(date +%F\ %T) $name $first" >> "$STATE_DIR/flaky.log"
    quarantine_add "$name" mac "$first"
    echo "FLAKY $name"
    return
  fi
  { echo "== AUTOTEST FAILED $name (log $log, first log $first)"; failure_lines "$log"; } > "$RUN_DIR/$name.out"
  echo "FAIL $name"
}

# Single-player tests that may share a process go in batches; co-op sessions, "# batch: alone" and "# clock: real" tests and
# coverage runs (one coverage file per test) run on their own.
BATCHABLE=() SINGLE=()
for t in "${TESTS[@]}"; do
  if [[ "$t" != mp:* && -z "$COVERAGE_DIR" && "${HOT_CARGO_MAC_BATCH:-6}" -gt 1 ]] \
    && ! grep -qx '# batch: alone' "tests/autotest/$t.txt" && [[ -z "$(clock_real "$t")" ]]; then BATCHABLE+=("$t"); else SINGLE+=("$t"); fi
done
{
  # Long jobs first: co-op sessions, then batches, then single tests.
  for t in "${SINGLE[@]}"; do
    [[ "$t" == mp:* ]] || continue
    # A co-op session counts as one job here; take_mac_slots holds its second slot.
    while [[ $(jobs -rp | wc -l) -ge $JOBS ]]; do sleep 1; done
    run_test "$t" &
  done
  B="${HOT_CARGO_MAC_BATCH:-6}"
  for ((i = 0; i < ${#BATCHABLE[@]}; i += B)); do
    while [[ $(jobs -rp | wc -l) -ge $JOBS ]]; do sleep 1; done
    run_batch "${BATCHABLE[@]:i:B}" &
  done
  for t in "${SINGLE[@]}"; do
    [[ "$t" == mp:* ]] && continue
    while [[ $(jobs -rp | wc -l) -ge $JOBS ]]; do sleep 1; done
    run_test "$t" &
  done
  wait
} | tee "$RUN_DIR/summary.txt"

FAILED="$(grep '^FAIL ' "$RUN_DIR/summary.txt" | cut -d' ' -f2- | tr '\n' ' ')"
echo "== $(grep -c . "$RUN_DIR/summary.txt") tests, $JOBS parallel, wall $((SECONDS - SUITE_START)) s ($RUN_DIR)"
slowest_tests $(find "$LOG_DIR" -name '*.mac.log' -newer "$RUN_DIR/build.log") "$RUN_DIR"/*.out
BUDGET_OK=yes
[[ -n "$COVERAGE_DIR" ]] || check_budget "Mac suite of ${#TESTS[@]} tests" "$((SECONDS - SUITE_START))" "${TESTS[@]}" || BUDGET_OK=no
FLAKY="$(grep '^FLAKY ' "$RUN_DIR/summary.txt" | cut -d' ' -f2- | tr '\n' ' ')"
[[ -n "$FLAKY" ]] && echo "== flaky on the Mac (passed on retry): $FLAKY"
if [[ -z "$FAILED" ]]; then echo "== all passed on the Mac"; [[ "$BUDGET_OK" == yes ]] || exit 1; exit 0; fi
for t in $FAILED; do
  echo "--- $t"
  # A co-op session: its step lines and one result line with the reason.
  if [[ "$t" == mp:* ]]; then grep -E "^FAIL |^== result|^   |log /" "$RUN_DIR/$t.out" | head -40; else head -40 "$RUN_DIR/$t.out"; fi
done
echo "== failed on the Mac: $FAILED"
exit 1
