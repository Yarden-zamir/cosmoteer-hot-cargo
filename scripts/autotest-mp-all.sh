#!/usr/bin/env bash
# Run every co-op session (tests/autotest-mp/<session>.txt) in parallel and print a summary.
# Usage: autotest-mp-all.sh [timeout-per-test]. A wrapper around autotest-all.sh with the session names.
set -uo pipefail
cd "$(dirname "$0")/.."
source scripts/autotest-lib.sh
NAMES=()
for n in $(coop_sessions); do NAMES+=("mp:$n"); done
exec scripts/autotest-all.sh "${1:-400}" "${NAMES[@]}"
