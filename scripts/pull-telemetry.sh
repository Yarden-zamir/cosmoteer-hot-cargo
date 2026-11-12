#!/usr/bin/env bash
# Copy telemetry files from the PC into ../.telemetry/<slot>/ (outside the repo). TELEMETRY.md describes the files.
# Usage: pull-telemetry.sh [SLOT...]   Without a slot it pulls every test slot (t1..tN) and the main slot.
# Files that are already here with the same size are not copied again. Then run: cd tools/telemetry && uv run hc-telemetry
set -euo pipefail
source "$(dirname "$0")/autotest-lib.sh"
OUT="${HOT_CARGO_TELEMETRY_DIR:-$REPO_ROOT/../.telemetry}"
if [[ $# -gt 0 ]]; then SLOT_LIST=("$@"); else SLOT_LIST=(main); for ((i = 1; i <= SLOTS; i++)); do SLOT_LIST+=("t$i"); done; fi
for slot in "${SLOT_LIST[@]}"; do
  mkdir -p "$OUT/$slot"
  # The PC side lists name and size, so only new or grown files travel.
  listing="$(
    export SLOT="$slot"
    source "$LIB_DIR/pc.sh"
    pc "$PS_PATHS"'$d = Join-Path $modDir "telemetry"; if (Test-Path $d) { Get-ChildItem $d -Filter *.jsonl | ForEach-Object { $_.Name + "|" + $_.Length } }'
  )"
  want=()
  while IFS='|' read -r name size; do
    name="${name%$'\r'}"; size="${size%$'\r'}"
    [[ -z "$name" ]] && continue
    local_file="$OUT/$slot/$name"
    if [[ -f "$local_file" && "$(stat -f%z "$local_file")" == "$size" ]]; then continue; fi
    want+=("$name")
  done <<<"$listing"
  if [[ ${#want[@]} -eq 0 ]]; then echo "$slot: up to date"; continue; fi
  (
    export SLOT="$slot"
    source "$LIB_DIR/pc.sh"
    printf '%s\n' "${want[@]}" | pc "$PS_PATHS"'$d = Join-Path $modDir "telemetry"; $l = Join-Path $env:TEMP "hc_tele_list.txt"; [IO.File]::WriteAllText($l, [Console]::In.ReadToEnd()); tar.exe -cf - -C $d -T $l'
  ) | tar -xf - -C "$OUT/$slot"
  echo "$slot: ${#want[@]} file(s) -> $OUT/$slot"
done
