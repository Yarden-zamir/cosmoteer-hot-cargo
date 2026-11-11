#!/usr/bin/env bash
# Start Cosmoteer on the PC minimized, so it does not take focus from the PC user.
# Usage: run.sh [seconds-to-wait-then-print-logs]
source "$(dirname "$0")/pc.sh"
WAIT="${1:-0}"
pc "$PS_PATHS"'
if (Get-Process Cosmoteer -ErrorAction SilentlyContinue) { "already running" } else {
  Start-Process -FilePath (Join-Path $game "Bin\Cosmoteer.exe") -WorkingDirectory (Join-Path $game "Bin") -WindowStyle Minimized
  "started"
}'
if [[ "$WAIT" -gt 0 ]]; then sleep "$WAIT"; "$(dirname "$0")/logs.sh" 40; fi
