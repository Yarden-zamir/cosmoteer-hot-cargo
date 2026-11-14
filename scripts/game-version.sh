#!/usr/bin/env bash
# Print the game versions that the mod targets: CompatibleGameVersions in mod/mod.header.rules, for example "0.30.5a".
# Several versions print comma separated. The version lives only there; release.sh, dev-release.yml and pull-game.sh read it.
# Usage: game-version.sh [repo root, default this repo]
set -euo pipefail
ROOT="${1:-$(dirname "$0")/..}"
LINE="$(grep -F 'CompatibleGameVersions' "$ROOT/mod/mod.header.rules")" || { echo "game-version: no CompatibleGameVersions in $ROOT/mod/mod.header.rules" >&2; exit 1; }
V="${LINE#*[}"; V="${V%]*}"; V="${V//\"/}"
[[ -n "$V" && "$V" != *CompatibleGameVersions* ]] || { echo "game-version: cannot read the list in: $LINE" >&2; exit 1; }
echo "$V"
