#!/usr/bin/env bash
# Rules-load check on this Mac: boot the game with the mod in the headless sim host (tools/mac-sim, "rules" mode), load
# every rules file, and fail on a mod load error. About 15 s; catches .rules syntax and reference errors before a game run.
# Usage: check-rules.sh   MAC_SIM_STAGE=<dir>: use a stage that scripts/stage.sh made (and a built host).
set -uo pipefail
cd "$(dirname "$0")/.."
OUT="$(mktemp)"
T0=$SECONDS
MAC_SIM_SAVE="$(mktemp -d)" tools/mac-sim/run.sh > "$OUT" 2>&1
RC=$?
LOADED="$(grep -m1 'rules loaded in' "$OUT")"
ERRORS="$(sed -n 's/.*mod errors: \([0-9]*\).*/\1/p' "$OUT" | head -1)"
if [[ $RC -ne 0 || -z "$LOADED" || "${ERRORS:-1}" != 0 ]]; then
  echo "== rules check FAILED ($((SECONDS - T0)) s):"
  grep -E "mod |FAILED|rror" "$OUT" | head -20
  rm -f "$OUT"
  exit 1
fi
echo "== rules check: ok ($LOADED, $((SECONDS - T0)) s)"
rm -f "$OUT"
