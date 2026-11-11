#!/usr/bin/env bash
# Shared settings for scripts that act on the Windows PC that runs Cosmoteer.
# Override with environment variables when the PC or paths differ.
#
# SLOT selects the game instance:
# - empty or "main": the player's own Cosmoteer save folder and Mods folder. The game runs with a window (minimized).
# - any other name, for example "t1": a test instance with its own save folder %USERPROFILE%\HotCargoSlots\<SLOT>.
#   It runs headless (no window, no graphics, no audio) with --nosteam, so several can run at the same time.
set -euo pipefail
PC_MACHINE="${PC_MACHINE:-windows 2}"
PC_GAME_DIR="${PC_GAME_DIR:-C:\\Program Files (x86)\\Steam\\steamapps\\common\\Cosmoteer}"
SLOT="${SLOT:-main}"
T3="${T3:-$HOME/.t3/bin/t3}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SHARED_GAME="$REPO_ROOT/../.game"

pc() { "$T3" machine exec "$PC_MACHINE" -- "$@"; }

# PowerShell snippet that sets:
#   $game     game install folder
#   $mainMods the player's own Mods folder (used for the default mod path)
#   $root     save folder of the selected slot
#   $mods     Mods folder of the selected slot
#   $modDir   hot_cargo folder in $mods
#   $pidFile  file with the process id of the slot's game, for slots other than main
#   $slotArgs extra game arguments for the slot
PS_PATHS='$game="'"$PC_GAME_DIR"'"; $slot="'"$SLOT"'";
$mainRoot = Get-ChildItem "$env:USERPROFILE\Saved Games\Cosmoteer" -Directory | Where-Object { Test-Path (Join-Path $_.FullName "Mods") } | Select-Object -First 1 -ExpandProperty FullName
if (-not $mainRoot) { throw "Cosmoteer save folder not found" }
$mainMods = Join-Path $mainRoot "Mods"
if ($slot -eq "main") { $root = $mainRoot; $slotArgs = @() }
else { $root = Join-Path $env:USERPROFILE "HotCargoSlots\$slot"; $slotArgs = @("--headless", "--nosteam", "--cores", "2", "--rootsavepath", "`"$root`"", "--rootcachepath", "`"$root\Caches`"") }
$mods = Join-Path $root "Mods"; $modDir = Join-Path $mods "hot_cargo"; $pidFile = Join-Path $root "hot_cargo.pid";
function Get-SlotProcess { if ($slot -eq "main") { $slotPids = @(Get-ChildItem (Join-Path $env:USERPROFILE "HotCargoSlots") -Filter hot_cargo.pid -Recurse -ErrorAction SilentlyContinue | ForEach-Object { [int](Get-Content $_.FullName) }); Get-Process Cosmoteer -ErrorAction SilentlyContinue | Where-Object { $slotPids -notcontains $_.Id } } elseif (Test-Path $pidFile) { Get-Process -Id ([int](Get-Content $pidFile)) -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -eq "Cosmoteer" } } }
'
