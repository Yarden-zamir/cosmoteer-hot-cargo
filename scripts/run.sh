#!/usr/bin/env bash
# Start Cosmoteer on the PC in the slot that SLOT selects (see pc.sh).
# main: with a window, minimized, so it does not take focus. Test slots: headless, no window at all.
# Usage: run.sh [seconds-to-wait-then-print-logs]
source "$(dirname "$0")/pc.sh"
WAIT="${1:-0}"
pc "$PS_PATHS"'
if (Get-SlotProcess) { "already running in slot $slot" } else {
  $exe = Join-Path $game "Bin\Cosmoteer.exe"; $bin = Join-Path $game "Bin"
  if ($slot -eq "main") { Start-Process -FilePath $exe -WorkingDirectory $bin -WindowStyle Minimized; "started (main, minimized)" }
  else {
    $p = Start-Process -FilePath $exe -WorkingDirectory $bin -ArgumentList $slotArgs -WindowStyle Hidden -PassThru
    [IO.File]::WriteAllText($pidFile, [string]$p.Id); "started (slot $slot, headless, pid " + $p.Id + ")"
  }
}'
if [[ "$WAIT" -gt 0 ]]; then sleep "$WAIT"; "$(dirname "$0")/logs.sh" 40; fi
