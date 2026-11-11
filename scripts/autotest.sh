#!/usr/bin/env bash
# Run an autotest script in a headless test instance of the game on the PC and print the result.
# Usage: autotest.sh tests/autotest/<name>.txt [timeout-seconds]
# Takes a free test slot (t1..tN, N = HOT_CARGO_SLOTS, default 3), so several autotests run at the same time.
# Exit code 0 only if the log has AUTOTEST DONE and no AUTOTEST FAIL or ERROR line.
SCRIPT="${1:?usage: autotest.sh <script> [timeout]}"
TIMEOUT="${2:-300}"
SLOTS="${HOT_CARGO_SLOTS:-3}"

# Take a slot. A lock whose owner process is gone is stale and is taken over.
while :; do
  for ((i = 1; i <= SLOTS; i++)); do
    LOCK="/tmp/hot_cargo_slot_$i.lock"
    if mkdir "$LOCK" 2>/dev/null; then echo $$ > "$LOCK/pid"; break 2; fi
    if [[ -f "$LOCK/pid" ]] && ! kill -0 "$(cat "$LOCK/pid")" 2>/dev/null; then rm -rf "$LOCK"; fi
  done
  echo "all $SLOTS test slots busy; waiting"; sleep 15
done
export SLOT="t$i"
source "$(dirname "$0")/pc.sh"
echo "$REPO_ROOT" > "$LOCK/owner"
trap '"$REPO_ROOT/scripts/stop.sh" >/dev/null 2>&1; rm -rf "$LOCK"' EXIT
echo "== slot $SLOT"

"$REPO_ROOT/scripts/stop.sh" >/dev/null
"$REPO_ROOT/scripts/deploy.sh" | tail -1
pc "$PS_PATHS"'$in = [Console]::In.ReadToEnd(); [IO.File]::WriteAllText((Join-Path $modDir "autotest.txt"), $in); $l = Join-Path $modDir "csharp\hot_cargo_log.txt"; if (Test-Path $l) { Remove-Item $l }; "autotest script installed"' < "$SCRIPT"
"$REPO_ROOT/scripts/run.sh"
LOG=""
for ((t = 0; t < TIMEOUT; t += 10)); do
  sleep 10
  LOG="$(pc "$PS_PATHS"'$l = Join-Path $modDir "csharp\hot_cargo_log.txt"; if (Test-Path $l) { Get-Content $l -Raw }; if (-not (Get-SlotProcess)) { "<<GAME EXITED>>" }')"
  if grep -q '<<GAME EXITED>>' <<<"$LOG"; then break; fi
done
grep -v '<<GAME EXITED>>' <<<"$LOG"
if grep -q 'AUTOTEST DONE' <<<"$LOG" && ! grep -qE 'AUTOTEST FAIL| ERROR ' <<<"$LOG"; then echo "== AUTOTEST OK (slot $SLOT)"; exit 0; fi
echo "== AUTOTEST FAILED (slot $SLOT; game log: SLOT=$SLOT scripts/logs.sh)"; exit 1
