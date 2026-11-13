#!/usr/bin/env bash
# Run autotests in the headless sim host (tools/headless-sim) on this machine (macOS or Linux, also in CI), in parallel,
# and print a summary.
# Usage: autotest-sim.sh [timeout-per-test] [test ...]
# - No test names: every tests/autotest/*.txt. Co-op sessions run with their names (mp:<session>, scripts/autotest-sim-mp.sh):
#   two processes on 127.0.0.1 with the real-time clock. A session takes 2 slots and has no retry (its steps report on their own).
# - Same pass rule as autotest.sh: the log has "telename <name>", AUTOTEST DONE, and no AUTOTEST FAIL or ERROR line.
# - Same log place as the PC runner: <container>/.state/logs/<worktree>/<name>.<time>.sim.log, summary in run-sim-<time>/.
# - Retries a failed run once, like autotest.sh. A pass on the retry prints FLAKY and goes to ../.state/flaky.log.
#   The fixed-step clock repeats most sim values, but background work (jumps, spawns, the telemetry writer) runs in real time.
# - Parallel runs: HOT_CARGO_SIM_JOBS, default from the cores and the memory (see below). The limit is for the whole machine:
#   runs of every worktree share the slot locks /tmp/hot_cargo_sim_slot_<n>.lock.
# Needs the game files in ../.game (scripts/pull-game.sh). Exit code 0 only if every test passed.
set -uo pipefail
cd "$(dirname "$0")/.."
TIMEOUT="${1:-400}"
shift || true
source scripts/autotest-lib.sh

TESTS=()
if [[ $# -gt 0 ]]; then TESTS=("$@"); else for t in tests/autotest/*.txt; do TESTS+=("$(basename "$t" .txt)"); done; fi
for t in "${TESTS[@]}"; do
  if [[ "$t" == mp:* ]]; then f="tests/autotest-mp/${t#mp:}.txt"; else f="tests/autotest/$t.txt"; fi
  [[ -f "$f" ]] || { echo "autotest-sim: no test $t ($f)" >&2; exit 2; }
done

# One run takes about one core and 2 GB at its peak. Two cores stay free for the agents and the build.
if [[ "$(uname)" == Darwin ]]; then CORES="$(sysctl -n hw.ncpu)" MEM_GB=$(( $(sysctl -n hw.memsize) / 1024 / 1024 / 1024 ))
else CORES="$(nproc)" MEM_GB=$(( $(awk '/^MemTotal:/ { print $2 }' /proc/meminfo) / 1024 / 1024 )); fi
JOBS="${HOT_CARGO_SIM_JOBS:-$(( CORES - 2 < MEM_GB / 2 ? CORES - 2 : MEM_GB / 2 ))}"
(( JOBS >= 1 )) || JOBS=1

RUN_DIR="$LOG_DIR/run-sim-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$RUN_DIR"
SUITE_START=$SECONDS
STAGE="$RUN_DIR/stage"
{ dotnet build tools/headless-sim/HeadlessSim.csproj -c Release -nologo -v quiet -clp:ErrorsOnly && scripts/stage.sh "$STAGE"; } > "$RUN_DIR/build.log" 2>&1 \
  || { cat "$RUN_DIR/build.log"; echo "== build failed"; exit 1; }

# Takes $2 free sim slots at once (all or none, so two co-op runs cannot deadlock), writes the owner pid $1 into each and
# prints their lock folders. A co-op session runs two game processes.
take_sim_slots() {
  local i lock got=()
  while :; do
    got=()
    for ((i = 1; i <= JOBS && ${#got[@]} < $2; i++)); do
      lock="/tmp/hot_cargo_sim_slot_$i.lock"
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
  local name="${1#mp:}" locks out rc=0 me=$BASHPID
  # Read $BASHPID out here: inside "$(...)" it is the pid of the command substitution, which exits at once,
  # so every other taker would judge the lock stale and remove it.
  locks="$(take_sim_slots "$me" 2)"
  out="$RUN_DIR/$1.out"
  SIM_STAGE="$STAGE" scripts/autotest-sim-mp.sh "tests/autotest-mp/$name.txt" "$((TIMEOUT + 200))" > "$out" 2>&1 || rc=$?
  rm -rf $locks
  return $rc
}

# One run. Prints the log path; returns 0 on a pass.
run_once() {
  local name="$1" log save lock rc=0 t0=$SECONDS me=$BASHPID
  # $me: this run's process. Inside "$(...)" $BASHPID is the short-lived substitution subshell (see run_mp).
  lock="$(take_sim_slots "$me" 1)"
  log="$LOG_DIR/$name.$(date +%Y%m%d-%H%M%S).sim.log"
  save="$(mktemp -d)"
  { echo "telename $name"; cat "tests/autotest/$name.txt"; } > "$save/script.txt"
  SIM_STAGE="$STAGE" SIM_SAVE="$save" timeout "$TIMEOUT" tools/headless-sim/run.sh autotest "$save/script.txt" > "$log" 2>&1 || rc=$?
  rm -rf "$lock"
  { echo; echo "=== hot_cargo_log.txt"; cat "$save/Mods/hot_cargo/csharp/hot_cargo_log.txt" 2>/dev/null; } >> "$log"
  [[ $rc -eq 124 ]] && echo "== timeout after $TIMEOUT s" >> "$log"
  echo "== timing $name total=$((SECONDS - t0))" >> "$log"
  rm -rf "$save"
  echo "$log"
  [[ $rc -eq 0 ]] && log_passed < "$log" && grep -qF "telename $name" "$log"
}

run_test() {
  local name="$1" first log
  if [[ "$name" == mp:* ]]; then
    if run_mp "$name"; then echo "PASS $name"; else echo "FAIL $name"; fi
    return
  fi
  if first="$(run_once "$name")"; then echo "PASS $name"; return; fi
  if log="$(run_once "$name")"; then
    echo "$(date +%F\ %T) $name $first" >> "$STATE_DIR/flaky.log"
    echo "FLAKY $name"
    return
  fi
  { echo "== AUTOTEST FAILED $name (log $log, first log $first)"; failure_lines "$log"; } > "$RUN_DIR/$name.out"
  echo "FAIL $name"
}

{
  for t in "${TESTS[@]}"; do
    # A co-op session counts as one job here; take_sim_slots holds its second slot.
    while [[ $(jobs -rp | wc -l) -ge $JOBS ]]; do sleep 1; done
    run_test "$t" &
  done
  wait
} | tee "$RUN_DIR/summary.txt"

FAILED="$(grep '^FAIL ' "$RUN_DIR/summary.txt" | cut -d' ' -f2- | tr '\n' ' ')"
echo "== $(grep -c . "$RUN_DIR/summary.txt") tests, $JOBS parallel, wall $((SECONDS - SUITE_START)) s ($RUN_DIR)"
FLAKY="$(grep '^FLAKY ' "$RUN_DIR/summary.txt" | cut -d' ' -f2- | tr '\n' ' ')"
[[ -n "$FLAKY" ]] && echo "== flaky in the sim (passed on retry): $FLAKY"
if [[ -z "$FAILED" ]]; then echo "== all passed in the sim"; exit 0; fi
for t in $FAILED; do
  echo "--- $t"
  # A co-op session: its step lines and one result line with the reason.
  if [[ "$t" == mp:* ]]; then grep -E "^FAIL |^== result|^   |log /" "$RUN_DIR/$t.out" | head -40; else head -40 "$RUN_DIR/$t.out"; fi
done
echo "== failed in the sim: $FAILED"
exit 1
