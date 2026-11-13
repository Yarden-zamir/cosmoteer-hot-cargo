#!/usr/bin/env bash
# Run the Mac sim host against the game copy in ../.game (scripts/pull-game.sh).
# - Builds the host, and builds and stages Hot Cargo with scripts/stage.sh. The mod goes to <save>/Mods/hot_cargo with a
#   settings.rules that turns it on, like deploy.sh does for a test slot. MAC_SIM_NOMOD=1 skips the mod.
# - MAC_SIM_STAGE=<dir>: use a stage that scripts/stage.sh made, and build nothing (the host must be built already).
#   scripts/autotest-mac.sh uses it, so parallel runs share one build.
# - Runs with the game folder as working directory: Paths reads "Data" relative to it.
# - --cores 2: the same game thread count as a PC test slot (scripts/pc.sh).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
GAME="$(cd "$REPO/../.game" && pwd)"
SAVE="${MAC_SIM_SAVE:-$(mktemp -d)}"
STAGE="${MAC_SIM_STAGE:-}"
if [[ -z "$STAGE" ]]; then
  dotnet build "$HERE/MacSim.csproj" -c Release -nologo -v quiet -clp:ErrorsOnly
  if [[ -z "${MAC_SIM_NOMOD:-}" ]]; then
    STAGE="$(mktemp -d)"
    "$REPO/scripts/stage.sh" "$STAGE"
  fi
fi
if [[ -n "$STAGE" ]]; then
  MOD="$SAVE/Mods/hot_cargo"
  rm -rf "$MOD"
  mkdir -p "$SAVE/Mods"
  cp -R "$STAGE/build/hot_cargo" "$MOD"
  # stage.sh renames the tunables file for the PC side of deploy.sh. A new save folder has no live copy, so it is the live copy.
  if [[ -f "$MOD/tunables.rules.deploy" ]]; then mv "$MOD/tunables.rules.deploy" "$MOD/tunables.rules"; fi
  printf 'GameSettings\n{\n\tEnabledMods ["%s"]\n\tAutoDisableMods = false\n\tShowNewsOnStartup = false\n\tShowTipsOnStartup = false\n\tIsFirstBoot = false\n}\n' "$MOD" > "$SAVE/settings.rules"
fi
cd "$GAME"
exec dotnet "$HERE/bin/Release/net10.0/MacSim.dll" --headless --nosteam --cores 2 --rootsavepath "$SAVE" "$@"
