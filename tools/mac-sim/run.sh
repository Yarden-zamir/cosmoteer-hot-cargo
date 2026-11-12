#!/usr/bin/env bash
# Run the Mac sim host. It needs the game folder as working directory (Paths reads "Data" relative to it).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
GAME="$(cd "$HERE/../../../.game" && pwd)"
SAVE="${MAC_SIM_SAVE:-$(mktemp -d)}"
dotnet build "$HERE/MacSim.csproj" -c Debug -nologo -v quiet -clp:ErrorsOnly
cd "$GAME"
exec dotnet "$HERE/bin/Debug/net10.0/MacSim.dll" --headless --nosteam --rootsavepath "$SAVE" "$@"
