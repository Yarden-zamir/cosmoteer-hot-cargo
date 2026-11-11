#!/usr/bin/env bash
# Keep the player's own game (slot main) on the newest pushed main.
# Every minute: fetch origin/main; if it differs from the installed build and the player's game is closed,
# check it out in a separate detached worktree and deploy it. A running game is never touched.
# Usage: keep-installed.sh [worktree, default ../play] [minutes to run, default 115]
set -uo pipefail
cd "$(dirname "$0")/.."
WT="${1:-../play}"
MINUTES="${2:-115}"
STATE=/tmp/hot_cargo_installed_sha
[[ -d "$WT" ]] || git --git-dir ../.bare worktree add -q --detach "$WT" origin/main
for ((i = 0; i < MINUTES; i++)); do
  git fetch -q origin main 2>/dev/null
  want="$(git rev-parse origin/main)"
  if [[ "$want" != "$(cat "$STATE" 2>/dev/null)" ]]; then
    state="$(SLOT=main bash -c 'source scripts/pc.sh; pc "$PS_PATHS""if (Get-SlotProcess) { \"RUNNING\" } else { \"CLOSED\" }"' 2>/dev/null | tail -1)"
    if [[ "$state" == CLOSED ]]; then
      git -C "$WT" checkout -q --detach "$want"
      if (cd "$WT" && SLOT=main ./scripts/deploy.sh >/tmp/hot_cargo_keep_installed.log 2>&1 && SLOT=main ./scripts/mod-toggle.sh on >>/tmp/hot_cargo_keep_installed.log 2>&1); then
        echo "$want" > "$STATE"; echo "$(date +%H:%M) installed $(git log --oneline -1 "$want")"
      else
        echo "$(date +%H:%M) deploy of ${want:0:7} failed; see /tmp/hot_cargo_keep_installed.log"
      fi
    fi
  fi
  sleep 60
done
echo "keep-installed stopped after $MINUTES minutes"
