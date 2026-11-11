#!/usr/bin/env bash
# Run an autotest script in the game on the PC, minimized, and print the result.
# Usage: autotest.sh tests/autotest/<name>.txt [timeout-seconds]
# Deploys first, turns the mod on, runs the game until the script quits, then prints the Hot Cargo log.
# Exit code 0 only if the log has AUTOTEST DONE and no AUTOTEST FAIL or ERROR line.
source "$(dirname "$0")/pc.sh"
SCRIPT="${1:?usage: autotest.sh <script> [timeout]}"
TIMEOUT="${2:-300}"
"$REPO_ROOT/scripts/stop.sh" >/dev/null
"$REPO_ROOT/scripts/deploy.sh" | tail -1
"$REPO_ROOT/scripts/mod-toggle.sh" on | tail -1
pc "$PS_PATHS"'$in = [Console]::In.ReadToEnd(); [IO.File]::WriteAllText((Join-Path $modDir "autotest.txt"), $in); "autotest script installed"' < "$SCRIPT"
"$REPO_ROOT/scripts/run.sh" >/dev/null
LOG=""
for ((t = 0; t < TIMEOUT; t += 10)); do
  sleep 10
  LOG="$(pc "$PS_PATHS"'$l = Join-Path $modDir "csharp\hot_cargo_log.txt"; if (Test-Path $l) { Get-Content $l -Raw }; if (-not (Get-Process Cosmoteer -ErrorAction SilentlyContinue)) { "<<GAME EXITED>>" }')"
  if grep -q '<<GAME EXITED>>' <<<"$LOG"; then break; fi
done
"$REPO_ROOT/scripts/stop.sh" >/dev/null
grep -v '<<GAME EXITED>>' <<<"$LOG"
if grep -q 'AUTOTEST DONE' <<<"$LOG" && ! grep -qE 'AUTOTEST FAIL| ERROR ' <<<"$LOG"; then echo "== AUTOTEST OK"; exit 0; fi
echo "== AUTOTEST FAILED (see log above; game log: scripts/logs.sh)"; exit 1
