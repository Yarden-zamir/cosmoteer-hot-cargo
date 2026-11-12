#!/usr/bin/env bash
# Build Tech Vaults into ship designs with the game's own code (autotest command vaultdesigns, spec 1016-1018).
# Usage:
#   vault-saved-ships.sh               the player's saved ships (<save folder>\Saved Ships, Paths.SavedShipsFolder), changed in place after a full backup.
#   vault-saved-ships.sh --from B      the same, but the designs come from the folder B in the save folder (an earlier backup,
#                                      for example "Saved Ships.backup-20261112-222246"). B is only read.
#   vault-saved-ships.sh --starters D  the vanilla starter ships; the new designs go to the local folder D (mod/tech/starters).
# Runs in the player's game slot (main, the only slot with graphics), only when that game is closed, under the main-slot lock.
# Then it installs origin/main back into the player's game (scripts/install-player.sh).
set -euo pipefail
MODE=saved; OUT=""; FROM=""
case "${1:-}" in
  --starters) MODE=starters; OUT="${2:?usage: vault-saved-ships.sh [--from <backup folder> | --starters <local out dir>]}" ;;
  --from) FROM="${2:?usage: vault-saved-ships.sh --from <backup folder>}" ;;
  "") ;;
  *) echo "usage: vault-saved-ships.sh [--from <backup folder> | --starters <local out dir>]"; exit 2 ;;
esac
source "$(dirname "$0")/autotest-lib.sh"
export SLOT=main
LOCK=/tmp/hot_cargo_main_slot.lock
until mkdir "$LOCK" 2>/dev/null; do
  if [[ -f "$LOCK/pid" ]] && ! kill -0 "$(cat "$LOCK/pid")" 2>/dev/null; then rm -rf "$LOCK"; continue; fi
  echo "main slot busy; waiting" >&2
  sleep 15
done
echo $$ > "$LOCK/pid"
trap 'rm -rf "$LOCK"' EXIT
source "$LIB_DIR/pc.sh"

if [[ -n "$(pc "$PS_PATHS"'Get-SlotProcess' </dev/null)" ]]; then echo "the player's game runs; nothing changed. Try again when it is closed."; exit 2; fi

# PC folders: source and output of the designs.
if [[ "$MODE" == saved ]]; then
  PATHS="$(pc "$PS_PATHS"'
    $ErrorActionPreference = "Stop"
    $ships = Join-Path $mainRoot "Saved Ships"
    if (-not (Test-Path $ships)) { throw "no Saved Ships folder in the save folder" }
    $backup = "$ships.backup-" + (Get-Date -Format "yyyyMMdd-HHmmss")
    Copy-Item $ships $backup -Recurse
    $a = @(Get-ChildItem $ships -Recurse -File).Count; $b = @(Get-ChildItem $backup -Recurse -File).Count
    if ($a -ne $b) { throw "backup has $b files, Ships has $a" }
    $from = "'"$FROM"'"; $src = if ($from) { Join-Path $mainRoot $from } else { $ships }
    if (-not (Test-Path $src)) { throw "no folder $from in the save folder" }
    "$src|$ships|$backup|$a"' </dev/null | tr -d '\r')"
  IFS='|' read -r SRC DST BACKUP COUNT <<<"$PATHS"
  echo "backup: $BACKUP ($COUNT files)"
else
  PATHS="$(pc "$PS_PATHS"'$o = Join-Path $modDir "starters_out"; if (Test-Path $o) { Remove-Item $o -Recurse -Force }; (Join-Path $game "Data\builtin_ships\Starter") + "|" + $o' </dev/null | tr -d '\r')"
  IFS='|' read -r SRC DST <<<"$PATHS"
fi

"$LIB_DIR/mod-toggle.sh" on </dev/null | tail -1
# The vanilla starters have no vault, so the test game starts without the new-game check. Saved ships use the Hot Cargo starter.
{
  [[ "$MODE" == starters ]] && echo "newgamecheck off"
  echo "newgame run"
  echo "wait 8"
  echo "vaultdesigns $SRC|$DST"
  echo "quit"
} | start_slot main
LOG=""
for ((t = 0; t < 600; t += 10)); do
  sleep 10
  LOG="$(slot_log main)"
  if grep -q '<<GAME EXITED>>' <<<"$LOG"; then break; fi
done
mkdir -p "$LOG_DIR"; echo "$LOG" > "$LOG_DIR/vault-$MODE.log"
grep -A3 -E 'AUTOTEST FAIL| ERROR ' <<<"$LOG" || true
echo "=== report"
pc "$PS_PATHS"'Get-Content (Join-Path $modDir "vault-report.txt")' </dev/null
if [[ "$MODE" == starters ]]; then
  mkdir -p "$OUT"
  pc "$PS_PATHS"'tar.exe -cf - -C "'"$DST"'" .' </dev/null | tar -xf - -C "$OUT"
  ls "$OUT"
fi
[[ "$MODE" == saved ]] && echo "backup: $BACKUP"
log_passed <<<"$LOG" || { echo "the run failed: see $LOG_DIR/vault-$MODE.log"; exit 1; }
rm -rf "$LOCK"
"$LIB_DIR/install-player.sh"
