#!/usr/bin/env bash
# Run a co-op session on this Mac: two tools/mac-sim processes, a host and a client, over UDP on 127.0.0.1.
# Usage: autotest-mac-mp.sh tests/autotest-mp/<session>.txt [timeout-seconds]
# The same session files, scripts (coop_host_script, coop_client_script) and verdict (scripts/coop-report.sh) as the PC runner.
# Both processes keep the real-time clock (MAC_SIM_REALTIME=1, see tools/mac-sim/Program.cs).
# MAC_SIM_STAGE=<dir>: use a stage that scripts/stage.sh made (and a built host); else this script builds both.
# Full log: <container>/.state/logs/<worktree>/mp-<session>.<time>.mac.log. Exit code 0 only on "== result: PASS".
set -uo pipefail
cd "$(dirname "$0")/.."
SESSION="${1:?usage: autotest-mac-mp.sh <session file> [timeout]}"
TIMEOUT="${2:-600}"
source scripts/autotest-lib.sh
NAME="$(basename "$SESSION" .txt)"
STAGE="${MAC_SIM_STAGE:-}"
if [[ -z "$STAGE" ]]; then
  STAGE="$(mktemp -d)"
  { dotnet build tools/mac-sim/MacSim.csproj -c Release -nologo -v quiet -clp:ErrorsOnly && scripts/stage.sh "$STAGE"; } > "$STAGE.build.log" 2>&1 \
    || { cat "$STAGE.build.log"; echo "== build failed"; exit 1; }
fi
T0=$SECONDS
HOST_SAVE="$(mktemp -d)" CLIENT_SAVE="$(mktemp -d)"
# A free port: the OS picks one for a moment, then the host takes it. Parallel sessions get different ports.
PORT="$(uv run python -I -c 'import socket; s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1])')"
[[ "$PORT" =~ ^[0-9]+$ ]] || { echo "== no free port ($PORT)"; exit 1; }
HOST_LOG="$HOST_SAVE/Mods/hot_cargo/csharp/hot_cargo_log.txt" CLIENT_LOG="$CLIENT_SAVE/Mods/hot_cargo/csharp/hot_cargo_log.txt"
coop_host_script "$SESSION" "$PORT" > "$HOST_SAVE/script.txt"
coop_client_script "$SESSION" "$PORT" > "$CLIENT_SAVE/script.txt"
MAC_SIM_REALTIME=1 MAC_SIM_STAGE="$STAGE" MAC_SIM_SAVE="$HOST_SAVE" timeout "$TIMEOUT" tools/mac-sim/run.sh autotest "$HOST_SAVE/script.txt" > "$HOST_SAVE/out.txt" 2>&1 &
HOST_PID=$!
# The client starts when the host hosts. A host that exits first ends the run.
CLIENT_PID=""
while kill -0 "$HOST_PID" 2>/dev/null && (( SECONDS - T0 < TIMEOUT )); do
  if grep -q "AUTOTEST hosting" "$HOST_LOG" 2>/dev/null; then
    MAC_SIM_REALTIME=1 MAC_SIM_STAGE="$STAGE" MAC_SIM_SAVE="$CLIENT_SAVE" timeout "$TIMEOUT" tools/mac-sim/run.sh autotest "$CLIENT_SAVE/script.txt" > "$CLIENT_SAVE/out.txt" 2>&1 &
    CLIENT_PID=$!
    break
  fi
  sleep 1
done
wait "$HOST_PID"
[[ -n "$CLIENT_PID" ]] && wait "$CLIENT_PID"

RUN_LOG="$LOG_DIR/mp-$NAME.$(date +%Y%m%d-%H%M%S).mac.log"
{ echo "=== host"; cat "$HOST_LOG" 2>/dev/null; echo "=== client"; cat "$CLIENT_LOG" 2>/dev/null
  echo "=== host process"; tail -20 "$HOST_SAVE/out.txt"; echo "=== client process"; tail -20 "$CLIENT_SAVE/out.txt" 2>/dev/null; } > "$RUN_LOG"
echo "== timing mp-$NAME (mac) total=$((SECONDS - T0)) port=$PORT" | tee -a "$RUN_LOG"
echo "== $NAME: log $RUN_LOG"
RC=0
scripts/coop-report.sh "$HOST_LOG" <(cat "$CLIENT_LOG" 2>/dev/null) | tee -a "$RUN_LOG" || RC=1
rm -rf "$HOST_SAVE" "$CLIENT_SAVE"
if [[ $RC -eq 0 ]]; then echo "== MP AUTOTEST OK $NAME (mac)"; else echo "== MP AUTOTEST FAILED $NAME (mac, log $RUN_LOG)"; fi
exit $RC
