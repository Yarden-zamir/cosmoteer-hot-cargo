#!/usr/bin/env bash
# Sets or clears the player hold on the main slot (see refuse_if_player_hold in pc.sh).
# Usage: player-hold.sh on | off | status
# on: no script may start or stop a game in slot main. off: only when the player says the PC is free.
set -euo pipefail
source "$(dirname "$0")/pc.sh"
case "${1:-status}" in
  on) mkdir -p "$(dirname "$PLAYER_HOLD")"; date > "$PLAYER_HOLD"; echo "player hold: on" ;;
  off) rm -f "$PLAYER_HOLD"; echo "player hold: off" ;;
  status) if [[ -e "$PLAYER_HOLD" ]]; then echo "player hold: on since $(cat "$PLAYER_HOLD")"; else echo "player hold: off"; fi ;;
  *) echo "usage: player-hold.sh on | off | status" >&2; exit 2 ;;
esac
