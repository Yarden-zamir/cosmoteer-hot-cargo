#!/usr/bin/env bash
# Shared settings for scripts that act on the Windows PC that runs Cosmoteer.
# Override with environment variables when the PC or paths differ.
#
# SLOT selects the game instance:
# - empty or "main": the player's own Cosmoteer save folder and Mods folder. The game runs with a window (minimized).
# - any other name, for example "t1": a test instance with its own save folder %USERPROFILE%\HotCargoSlots\<SLOT>.
#   It runs headless (no window, no graphics, no audio) with --nosteam, so several can run at the same time.
set -euo pipefail
# Machine-specific values (PC_MACHINE, optional PC_GAME_DIR) live in scripts/pc.local.sh, which git ignores.
# Put it in ../_shared/scripts/pc.local.sh so every worktree gets it. Example content:
#   PC_MACHINE="my-pc"   # the T3 name of the PC that runs Cosmoteer
LOCAL_CONFIG="$(dirname "${BASH_SOURCE[0]}")/pc.local.sh"
if [[ -f "$LOCAL_CONFIG" ]]; then source "$LOCAL_CONFIG"; fi
PC_MACHINE="${PC_MACHINE:?set PC_MACHINE in scripts/pc.local.sh (see the top of scripts/pc.sh)}"
PC_GAME_DIR="${PC_GAME_DIR:-C:\\Program Files (x86)\\Steam\\steamapps\\common\\Cosmoteer}"
SLOT="${SLOT:-main}"
# Path of the t3 CLI. A specific name, so a variable of a script that sources pc.sh cannot shadow it.
T3_CLI="${T3_CLI:-$HOME/.t3/bin/t3}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SHARED_GAME="$REPO_ROOT/../.game"

pc() { "$T3_CLI" machine exec "$PC_MACHINE" -- "$@"; }

# The player hold: while <container>/.state/player-hold exists, the main slot belongs to the player.
# Then nothing may start or stop a game in slot main, even when it looks closed (the player can be between two sessions).
# scripts/player-hold.sh sets and clears it. Only the coordinator clears it, when the player says the PC is free.
PLAYER_HOLD="$REPO_ROOT/../.state/player-hold"
# refuse_if_player_hold start (scripts that start a game) also refuses a windowed test slot (SLOT_WINDOWED=1,
# scripts/screens.sh): a window could take focus or GPU time from the player. Stopping one stays allowed; headless test
# slots stay allowed.
refuse_if_player_hold() {
  if [[ "$SLOT" == main && -e "$PLAYER_HOLD" ]]; then
    echo "refused: the player holds the main slot ($PLAYER_HOLD). Use test slots, or ask the coordinator." >&2
    exit 3
  fi
  if [[ "${1:-}" == start && "$SLOT" != main && "${SLOT_WINDOWED:-}" == 1 && -e "$PLAYER_HOLD" ]]; then
    echo "refused: the player plays ($PLAYER_HOLD); a windowed test slot could disturb the game. Try again when the hold ends." >&2
    exit 3
  fi
}

# PowerShell snippet that sets:
#   $game     game install folder
#   $mainMods the player's own Mods folder (used for the default mod path)
#   $root     save folder of the selected slot
#   $mods     Mods folder of the selected slot
#   $modDir   hot_cargo folder in $mods
#   $pidFile  file with the process id of the slot's game, for slots other than main (information only)
# Get-SlotProcess finds games by their command line, never by a pid file: Windows reuses process ids, so a stale pid
# file can name the player's game (a stop of a test slot then kills it).
# - main: every Cosmoteer process whose command line has no "HotCargoSlots\" path, with or without --headless. A process
#   whose command line cannot be read counts as main too, so an unknown game is never taken for a test game.
# - a test slot: only games with this slot's --rootsavepath folder (the closing quote keeps t1 apart from t10), windowed
#   (scripts/screens.sh) or headless. So a windowed test game is never taken for the player's game.
#   $slotArgs extra game arguments for the slot: test slots run headless, or with a window when SLOT_WINDOWED=1
PS_PATHS='$game="'"$PC_GAME_DIR"'"; $slot="'"$SLOT"'"; $windowed = "'"${SLOT_WINDOWED:-}"'" -eq "1";
$mainRoot = Get-ChildItem "$env:USERPROFILE\Saved Games\Cosmoteer" -Directory | Where-Object { Test-Path (Join-Path $_.FullName "Mods") } | Select-Object -First 1 -ExpandProperty FullName
if (-not $mainRoot) { throw "Cosmoteer save folder not found" }
$mainMods = Join-Path $mainRoot "Mods"
if ($slot -eq "main") { $root = $mainRoot; $slotArgs = @() }
else { $root = Join-Path $env:USERPROFILE "HotCargoSlots\$slot"; $slotArgs = @("--nosteam", "--cores", "2", "--rootsavepath", "`"$root`"", "--rootcachepath", "`"$root\Caches`""); if (-not $windowed) { $slotArgs = @("--headless") + $slotArgs } }
$mods = Join-Path $root "Mods"; $modDir = Join-Path $mods "hot_cargo"; $pidFile = Join-Path $root "hot_cargo.pid";
function Get-SlotProcess { $all = @(Get-CimInstance Win32_Process -Filter ("Name = " + [char]39 + "Cosmoteer.exe" + [char]39)); if ($slot -eq "main") { $all | Where-Object { -not ($_.CommandLine -like "*HotCargoSlots\*") } | ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue } } else { $mark = "HotCargoSlots\" + $slot + [char]34; $all | Where-Object { $_.CommandLine -like "*$mark*" } | ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue } } }
'

# PowerShell snippet that starts the slot's game (needs PS_PATHS before it). run.sh and deploy.sh (autotest mode) use it.
# main: with a window, minimized, so it does not take focus. Test slots: headless, no window at all; with SLOT_WINDOWED=1
# (scripts/screens.sh) with graphics, minimized.
PS_START='
if (Get-SlotProcess) { "already running in slot $slot" } else {
  $exe = Join-Path $game "Bin\Cosmoteer.exe"; $bin = Join-Path $game "Bin"
  if ($slot -eq "main") { Start-Process -FilePath $exe -WorkingDirectory $bin -WindowStyle Minimized; "started (main, minimized)" }
  else {
    $style = if ($windowed) { "Minimized" } else { "Hidden" }
    $p = Start-Process -FilePath $exe -WorkingDirectory $bin -ArgumentList $slotArgs -WindowStyle $style -PassThru
    [IO.File]::WriteAllText($pidFile, [string]$p.Id); "started (slot $slot, " + $(if ($windowed) { "windowed, minimized" } else { "headless" }) + ", pid " + $p.Id + ")"
  }
}'
