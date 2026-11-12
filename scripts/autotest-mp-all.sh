#!/usr/bin/env bash
# Run every co-op test (tests/autotest-mp/<name>.host.txt with <name>.client.txt) in parallel and print a summary.
# Usage: autotest-mp-all.sh [timeout-per-test]. A wrapper around autotest-all.sh with the co-op test names.
set -uo pipefail
cd "$(dirname "$0")/.."
NAMES=()
for h in tests/autotest-mp/*.host.txt; do NAMES+=("mp:$(basename "$h" .host.txt)"); done
exec scripts/autotest-all.sh "${1:-400}" "${NAMES[@]}"
