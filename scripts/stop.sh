#!/usr/bin/env bash
# Close Cosmoteer on the PC in the slot that SLOT selects (see pc.sh). main never stops test slots.
source "$(dirname "$0")/pc.sh"
pc "$PS_PATHS"'$p = Get-SlotProcess; if ($p) { $p | Stop-Process -Force; $p | Wait-Process -Timeout 20 -ErrorAction SilentlyContinue }; if ($slot -ne "main" -and (Test-Path $pidFile)) { Remove-Item $pidFile -Force }; "stopped (slot $slot)"'
