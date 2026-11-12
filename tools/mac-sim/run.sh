#!/usr/bin/env bash
# Run the Mac sim host against the game copy in ../.game (scripts/pull-game.sh).
# - Builds Hot Cargo and stages it like deploy.sh does for a test slot: <save>/Mods/hot_cargo with csharp/HotCargo.dll,
#   and a settings.rules that turns the mod on. MAC_SIM_NOMOD=1 skips the mod.
# - Runs with the game folder as working directory: Paths reads "Data" relative to it.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
GAME="$(cd "$REPO/../.game" && pwd)"
SAVE="${MAC_SIM_SAVE:-$(mktemp -d)}"
dotnet build "$HERE/MacSim.csproj" -c Debug -nologo -v quiet -clp:ErrorsOnly
if [[ -z "${MAC_SIM_NOMOD:-}" ]]; then
  dotnet build "$REPO/src/HotCargo/HotCargo.csproj" -c Release -nologo -v quiet -clp:ErrorsOnly
  MOD="$SAVE/Mods/hot_cargo"
  rm -rf "$MOD"
  "$REPO/scripts/assemble-mod.sh" "$MOD"
  mkdir -p "$MOD/csharp"
  cp "$REPO/src/HotCargo/bin/Release/net10.0-windows/"{HotCargo.dll,HotCargo.xml,0Harmony.dll} "$MOD/csharp/"
  printf 'GameSettings\n{\n\tEnabledMods ["%s"]\n\tAutoDisableMods = false\n\tShowNewsOnStartup = false\n\tShowTipsOnStartup = false\n\tIsFirstBoot = false\n}\n' "$MOD" > "$SAVE/settings.rules"
fi
cd "$GAME"
exec dotnet "$HERE/bin/Debug/net10.0/MacSim.dll" --headless --nosteam --rootsavepath "$SAVE" "$@"
