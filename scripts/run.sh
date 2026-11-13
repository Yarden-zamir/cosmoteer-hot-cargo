#!/usr/bin/env bash
# Start Cosmoteer on the PC in the slot that SLOT selects (see pc.sh).
# main: with a window, minimized, so it does not take focus. Test slots: headless, no window at all.
# Usage: run.sh [seconds-to-wait-then-print-logs]
source "$(dirname "$0")/pc.sh"
refuse_if_player_hold
WAIT="${1:-0}"
pc "$PS_PATHS$PS_START"
if [[ "$WAIT" -gt 0 ]]; then sleep "$WAIT"; "$(dirname "$0")/logs.sh" 40; fi
