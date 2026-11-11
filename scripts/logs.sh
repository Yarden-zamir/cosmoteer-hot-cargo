#!/usr/bin/env bash
# Print the Hot Cargo log and the tail of the newest Cosmoteer log of the slot that SLOT selects (see pc.sh).
source "$(dirname "$0")/pc.sh"
LINES="${1:-80}"
pc "$PS_PATHS"'
$l = Join-Path $modDir "csharp\hot_cargo_log.txt"
"=== hot_cargo_log.txt (slot $slot)"; if (Test-Path $l) { Get-Content $l -Tail '"$LINES"' } else { "(none)" }
$cl = Get-ChildItem (Join-Path $root "Logs") -Filter "*.txt" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($cl) { "=== " + $cl.FullName; Get-Content $cl.FullName -Tail '"$LINES"' }
'
