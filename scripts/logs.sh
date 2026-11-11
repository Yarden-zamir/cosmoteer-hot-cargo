#!/usr/bin/env bash
# Print the Hot Cargo log and the tail of the newest Cosmoteer log from the PC.
source "$(dirname "$0")/pc.sh"
LINES="${1:-80}"
pc "$PS_PATHS"'
$l = Join-Path $modDir "csharp\hot_cargo_log.txt"
"=== hot_cargo_log.txt"; if (Test-Path $l) { Get-Content $l -Tail '"$LINES"' } else { "(none)" }
$cl = Get-ChildItem (Join-Path (Split-Path $mods) "Logs") -Filter "*.txt" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$cl2 = Get-ChildItem "$env:USERPROFILE\Saved Games\Cosmoteer" -Filter "*.txt" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
foreach ($f in @($cl, $cl2) | Where-Object { $_ } | Select-Object -Unique) { "=== " + $f.FullName; Get-Content $f.FullName -Tail '"$LINES"' }
'
