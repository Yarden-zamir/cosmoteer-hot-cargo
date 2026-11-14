#!/usr/bin/env bash
# Run a co-op session in the headless sim: two tools/headless-sim processes, a host and a client, over UDP on 127.0.0.1.
# Usage: autotest-sim-mp.sh tests/autotest-mp/<session>.txt [timeout-seconds]
# The same session files, scripts (coop_host_script, coop_client_script) and verdict (scripts/coop-report.sh) as the PC runner.
# Both processes keep the real-time clock (SIM_REALTIME=1, see tools/headless-sim/Program.cs).
# SIM_STAGE=<dir>: use a stage that scripts/stage.sh made (and a built host); else this script builds both.
# Full log: <container>/.state/logs/<worktree>/mp-<session>.<time>.sim.log. Exit code 0 only on "== result: PASS".
set -uo pipefail
cd "$(dirname "$0")/.."
SESSION="${1:?usage: autotest-sim-mp.sh <session file> [timeout]}"
TIMEOUT="${2:-600}"
source scripts/autotest-lib.sh
NAME="$(basename "$SESSION" .txt)"
STAGE="${SIM_STAGE:-}"
if [[ -z "$STAGE" ]]; then
  STAGE="$(mktemp -d)"
  { dotnet build tools/headless-sim/HeadlessSim.csproj -c Release -nologo -v quiet -clp:ErrorsOnly && scripts/stage.sh "$STAGE"; } > "$STAGE.build.log" 2>&1 \
    || { cat "$STAGE.build.log"; echo "== build failed"; exit 1; }
fi
T0=$SECONDS
HOST_SAVE="$(mktemp -d)" CLIENT_SAVE="$(mktemp -d)"
# A free port: the OS picks one for a moment, then the host takes it. Parallel sessions get different ports.
# A random port in 40000-59999: parallel sessions rarely meet, and mphost fails with a clear message when the port is taken.
PORT=$((40000 + RANDOM % 20000))
HOST_LOG="$HOST_SAVE/Mods/hot_cargo/csharp/hot_cargo_log.txt" CLIENT_LOG="$CLIENT_SAVE/Mods/hot_cargo/csharp/hot_cargo_log.txt"
coop_host_script "$SESSION" "$PORT" > "$HOST_SAVE/script.txt"
coop_client_script "$SESSION" "$PORT" > "$CLIENT_SAVE/script.txt"
SIM_REALTIME=1 SIM_STAGE="$STAGE" SIM_SAVE="$HOST_SAVE" timeout "$TIMEOUT" tools/headless-sim/run.sh autotest "$HOST_SAVE/script.txt" > "$HOST_SAVE/out.txt" 2>&1 &
HOST_PID=$!
# The client starts when the host hosts. A host that exits first ends the run.
CLIENT_PID=""
while kill -0 "$HOST_PID" 2>/dev/null && (( SECONDS - T0 < TIMEOUT )); do
  if grep -q "AUTOTEST hosting" "$HOST_LOG" 2>/dev/null; then
    SIM_REALTIME=1 SIM_STAGE="$STAGE" SIM_SAVE="$CLIENT_SAVE" timeout "$TIMEOUT" tools/headless-sim/run.sh autotest "$CLIENT_SAVE/script.txt" > "$CLIENT_SAVE/out.txt" 2>&1 &
    CLIENT_PID=$!
    break
  fi
  sleep 1
done
wait "$HOST_PID"
[[ -n "$CLIENT_PID" ]] && wait "$CLIENT_PID"

RUN_LOG="$LOG_DIR/mp-$NAME.$(date +%Y%m%d-%H%M%S).sim.log"
{ echo "=== host"; cat "$HOST_LOG" 2>/dev/null; echo "=== client"; cat "$CLIENT_LOG" 2>/dev/null
  echo "=== host process"; tail -20 "$HOST_SAVE/out.txt"; echo "=== client process"; tail -20 "$CLIENT_SAVE/out.txt" 2>/dev/null; } > "$RUN_LOG"
echo "== timing mp-$NAME (sim) total=$((SECONDS - T0)) port=$PORT" | tee -a "$RUN_LOG"
echo "== $NAME: log $RUN_LOG"
RC=0
scripts/coop-report.sh "$HOST_LOG" <(cat "$CLIENT_LOG" 2>/dev/null) | tee -a "$RUN_LOG" || RC=1
rm -rf "$HOST_SAVE" "$CLIENT_SAVE"
if [[ $RC -eq 0 ]]; then echo "== MP AUTOTEST OK $NAME (sim)"; else echo "== MP AUTOTEST FAILED $NAME (sim, log $RUN_LOG)"; fi
exit $RC
