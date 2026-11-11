#!/usr/bin/env bash
# Turn the Hot Cargo rules mod on or off in the game settings on the PC. The game must be closed.
# Usage: mod-toggle.sh on|off
# The first change keeps a backup: settings.rules.hot_cargo_bak.
# The game writes EnabledMods as a multi-line list with paths relative to the save folder (for example Mods/hot_cargo).
# This script rewrites the list on one line and keeps the other entries as they are.
source "$(dirname "$0")/pc.sh"
MODE="${1:?usage: mod-toggle.sh on|off}"
pc "$PS_PATHS"'
$ErrorActionPreference = "Stop"
if (Get-Process Cosmoteer -ErrorAction SilentlyContinue) { throw "Cosmoteer is running. Close it first." }
$settings = Join-Path (Split-Path $mods) "settings.rules"
if (-not (Test-Path "$settings.hot_cargo_bak")) { Copy-Item $settings "$settings.hot_cargo_bak" }
$lines = [Collections.Generic.List[string]](Get-Content $settings)
$start = $lines.FindIndex([Predicate[string]]{ param($l) $l.TrimStart().StartsWith("EnabledMods") })
if ($start -lt 0) { throw "EnabledMods not found in settings.rules" }
$indent = $lines[$start].Substring(0, $lines[$start].Length - $lines[$start].TrimStart().Length)
# Collect the text of the list, from the EnabledMods line to the closing bracket.
$end = $start; $text = $lines[$start]
while (-not $text.Contains("]")) { $end++; if ($end -ge $lines.Count) { throw "EnabledMods list has no closing bracket" }; $text += "`n" + $lines[$end] }
$body = $text.Substring($text.IndexOf("[") + 1); $body = $body.Substring(0, $body.LastIndexOf("]"))
$items = @($body.Split([char[]]",`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" -and -not $_.Trim([char]34).EndsWith("hot_cargo") })
if ("'"$MODE"'" -eq "on") { $items += ([char]34 + ($modDir -replace "\\", "/") + [char]34) }
$lines.RemoveRange($start, $end - $start + 1)
$lines.Insert($start, $indent + "EnabledMods [" + ($items -join ", ") + "]")
[IO.File]::WriteAllLines($settings, $lines)
"EnabledMods now: " + $lines[$start].Trim()
'
