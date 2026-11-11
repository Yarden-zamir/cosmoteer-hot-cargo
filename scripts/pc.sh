#!/usr/bin/env bash
# Shared settings for scripts that act on the Windows PC that runs Cosmoteer.
# Override with environment variables when the PC or paths differ.
set -euo pipefail
PC_MACHINE="${PC_MACHINE:-windows 2}"
PC_GAME_DIR="${PC_GAME_DIR:-C:\\Program Files (x86)\\Steam\\steamapps\\common\\Cosmoteer}"
# The Mods folder sits under the Steam user id folder. The PowerShell side resolves it.
T3="${T3:-$HOME/.t3/bin/t3}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SHARED_GAME="$REPO_ROOT/../_shared/game"

pc() { "$T3" machine exec "$PC_MACHINE" -- "$@"; }

# PowerShell snippet that sets $mods to the user Mods folder and $game to the game folder.
PS_PATHS='$game="'"$PC_GAME_DIR"'"; $mods = Get-ChildItem "$env:USERPROFILE\Saved Games\Cosmoteer" -Directory | Where-Object { Test-Path (Join-Path $_.FullName "Mods") } | Select-Object -First 1 | ForEach-Object { Join-Path $_.FullName "Mods" }; if (-not $mods) { throw "Cosmoteer Mods folder not found" }; $modDir = Join-Path $mods "hot_cargo";'
