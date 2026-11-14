#!/usr/bin/env bash
# Copy game DLLs, symbols, .rules files, ship designs (*.ship.png) and name generator files (*.markov) from the PC into ../.game,
# then decompile. The headless sim (tools/headless-sim) needs the ship and name files. Only copies files: safe while the player plays.
# Run again after a game update.
source "$(dirname "$0")/pc.sh"
mkdir -p "$SHARED_GAME/Bin" "$SHARED_GAME/Data" "$SHARED_GAME/StandardMods"
pc 'tar.exe -cf - -C "'"$PC_GAME_DIR"'\Bin" Cosmoteer.dll Cosmoteer.pdb Cosmoteer.xml HalflingCore.dll HalflingCore.pdb HalflingCore.xml HalflingPlatformWDX.dll HalflingPlatformWDX.pdb Cosmoteer.deps.json' | tar -xf - -C "$SHARED_GAME/Bin"
# Limit: tar.exe reads the file list in the ANSI code page. It skips a file name outside that code page (one design-contest
# ship) with a "Couldn't visit" error. Revisit if a test needs such a file.
pc 'Set-Location "'"$PC_GAME_DIR"'\Data"; $f = Get-ChildItem -Recurse -File -Include *.rules,*.txt,*.ship.png,*.markov | Resolve-Path -Relative; $l = Join-Path $env:TEMP "hc_rules_list.txt"; [IO.File]::WriteAllLines($l, ($f | ForEach-Object { $_.Substring(2) })); tar.exe -cf - -T $l' | tar -xf - -C "$SHARED_GAME/Data"
pc 'tar.exe -cf - -C "'"$PC_GAME_DIR"'\Standard Mods" example_mod' | tar -xf - -C "$SHARED_GAME/StandardMods"
rm -rf "$SHARED_GAME/decompiled"; mkdir -p "$SHARED_GAME/decompiled"
for a in Cosmoteer HalflingCore; do ilspycmd -p -o "$SHARED_GAME/decompiled/$a" --nested-directories -r "$SHARED_GAME/Bin" "$SHARED_GAME/Bin/$a.dll" >/dev/null; done
echo "game files in $SHARED_GAME"
# The game declares its version in Cosmoteer.Versions.GameVersion. A version that the mod does not target means a game update.
GAME_VERSION="$(grep -F 'const string GameVersion = ' "$SHARED_GAME/decompiled/Cosmoteer/Cosmoteer/Versions.cs" | cut -d'"' -f2)"
[[ -n "$GAME_VERSION" ]] || { echo "pull-game: no GameVersion in the decompiled Versions.cs" >&2; exit 1; }
TARGET="$("$REPO_ROOT/scripts/game-version.sh")"
if [[ ", $TARGET, " == *", $GAME_VERSION, "* ]]; then echo "game version $GAME_VERSION (targeted)"
else echo "pull-game: game version $GAME_VERSION is not in CompatibleGameVersions ($TARGET). Follow the hot-cargo-game-update skill."; fi
