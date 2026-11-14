#!/usr/bin/env bash
# Refresh the private game references repo (Yarden-zamir/cosmoteer-hot-cargo-game-refs) from ../.game, after a game update
# and scripts/pull-game.sh. CI checks it out read-only (secret GAME_REFS_DEPLOY_KEY) to build the mod and to run the
# headless sim tests (.github/workflows/check.yml).
# Usage: refresh-game-refs.sh [--push]   Without --push it only shows what would change.
# Layout, the same as ../.game: Bin/, Data/ and StandardMods/example_mod/, exactly the files that pull-game.sh copies (not
# decompiled/). The repo's own clone lives in <container>/.game-refs.
set -euo pipefail
cd "$(dirname "$0")/.."
GAME="$(cd .. && pwd)/.game"
CLONE="$(cd .. && pwd)/.game-refs"
[[ -f "$GAME/Bin/Cosmoteer.dll" && -d "$GAME/Data" && -d "$GAME/StandardMods/example_mod" ]] \
  || { echo "refresh-game-refs: ../.game is incomplete; run scripts/pull-game.sh first"; exit 1; }
# No git hooks in this clone: a global post-checkout hook could link local machine files into it.
if [[ ! -d "$CLONE/.git" ]]; then gh repo clone Yarden-zamir/cosmoteer-hot-cargo-game-refs "$CLONE" -- -q -c core.hooksPath=/dev/null; fi
git -C "$CLONE" config core.hooksPath /dev/null
# No global ignore rules either: a personal "build/" rule once dropped Data/codex/tutorials/build/*.rules.
git -C "$CLONE" config core.excludesFile /dev/null
git -C "$CLONE" pull -q --ff-only
mkdir -p "$CLONE/Bin" "$CLONE/Data" "$CLONE/StandardMods"
rsync -a --delete "$GAME/Bin/" "$CLONE/Bin/"
rsync -a --delete "$GAME/Data/" "$CLONE/Data/"
rsync -a --delete "$GAME/StandardMods/example_mod/" "$CLONE/StandardMods/example_mod/"
# Only the game files: nothing else may enter this repo.
git -C "$CLONE" add -A -- Bin Data StandardMods
# Every game file must be in the index; a missing one means an ignore rule or a failed add.
MISSING="$(comm -23 <(cd "$CLONE" && find Bin Data StandardMods -type f | sort) <(git -C "$CLONE" -c core.quotePath=false ls-files -- Bin Data StandardMods | sort))"
[[ -z "$MISSING" ]] || { echo "refresh-game-refs: not tracked:"; echo "$MISSING" | head; exit 1; }
if git -C "$CLONE" diff --cached --quiet; then echo "refresh-game-refs: no change"; exit 0; fi
git -C "$CLONE" diff --cached --stat | tail -1
if [[ "${1:-}" != --push ]]; then echo "refresh-game-refs: run with --push to commit and push"; git -C "$CLONE" reset -q; exit 0; fi
git -C "$CLONE" commit -q -m "Game files from ../.game, $(date +%F)"
git -C "$CLONE" push -q
echo "refresh-game-refs: pushed $(git -C "$CLONE" rev-parse --short HEAD)"
