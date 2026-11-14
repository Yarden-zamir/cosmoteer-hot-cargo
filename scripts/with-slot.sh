#!/usr/bin/env bash
# Run a command with a free PC test slot. It takes the slot lock like autotest.sh, exports SLOT=tN, runs the command, then
# stops the slot's game and frees the slot. run.sh, stop.sh, deploy.sh and logs.sh take no lock, so use this for manual
# work in a test slot: a test that holds the slot cannot lose its game to you.
# Usage: with-slot.sh <command> [args...]
# Example: scripts/with-slot.sh bash -c 'scripts/deploy.sh && scripts/run.sh 60'
set -euo pipefail
[[ $# -gt 0 ]] || { echo "usage: with-slot.sh <command> [args...]" >&2; exit 2; }
source "$(dirname "$0")/autotest-lib.sh"
SLOT="$(take_slot)"
export SLOT
trap 'release_slot "$SLOT"' EXIT
echo "with-slot: $SLOT" >&2
"$@"
