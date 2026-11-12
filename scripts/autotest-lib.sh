#!/usr/bin/env bash
# Shared parts of autotest.sh and autotest-mp.sh. Source it; it does not run anything.
# Slots t1..tN (N = HOT_CARGO_SLOTS, default 8) are headless test instances on the PC (see pc.sh).
LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$LIB_DIR/.." && pwd)"
SLOTS="${HOT_CARGO_SLOTS:-8}"
# Full logs of every run stay here, one folder per worktree, so a later run in the same slot cannot overwrite them.
LOG_DIR="${HOT_CARGO_LOG_DIR:-/tmp/hot_cargo_logs/$(basename "$REPO_ROOT")}"
mkdir -p "$LOG_DIR"

slot_lock() { echo "/tmp/hot_cargo_slot_${1#t}.lock"; }

# Waits until it holds N free test slots at once, locks them for this shell process and prints their names.
# It never holds some slots while it waits for more, so parallel co-op tests (2 slots each) cannot deadlock.
# A lock whose owner process is gone is stale and is taken over.
take_slots() {
  local want="${1:-1}" i lock s
  local got=()
  while :; do
    got=()
    for ((i = 1; i <= SLOTS && ${#got[@]} < want; i++)); do
      lock="$(slot_lock "t$i")"
      if mkdir "$lock" 2>/dev/null; then echo $$ > "$lock/pid"; echo "$REPO_ROOT" > "$lock/owner"; got+=("t$i"); continue; fi
      if [[ -f "$lock/pid" ]] && ! kill -0 "$(cat "$lock/pid")" 2>/dev/null; then rm -rf "$lock"; fi
    done
    if [[ ${#got[@]} -eq "$want" ]]; then echo "${got[*]}"; return; fi
    for s in ${got[@]+"${got[@]}"}; do rm -rf "$(slot_lock "$s")"; done
    echo "waiting for $want free test slots" >&2
    sleep 15
  done
}
take_slot() { take_slots 1; }

# Stops the slot's game and frees the slot.
release_slot() {
  SLOT="$1" "$LIB_DIR/stop.sh" >/dev/null 2>&1 || true
  rm -rf "$(slot_lock "$1")"
}

# Deploys this worktree to the slot, installs the autotest script from stdin, clears the old log and starts the game.
start_slot() {
  local slot="$1" script
  # Read the script first: the PC commands below also read stdin.
  script="$(cat)"
  SLOT="$slot" "$LIB_DIR/stop.sh" >/dev/null </dev/null
  SLOT="$slot" "$LIB_DIR/deploy.sh" </dev/null | tail -1
  (
    export SLOT="$slot"
    source "$LIB_DIR/pc.sh"
    pc "$PS_PATHS"'$in = [Console]::In.ReadToEnd(); [IO.File]::WriteAllText((Join-Path $modDir "autotest.txt"), $in); $l = Join-Path $modDir "csharp\hot_cargo_log.txt"; if (Test-Path $l) { Remove-Item $l }; "autotest script installed"' <<<"$script"
  )
  SLOT="$slot" "$LIB_DIR/run.sh" </dev/null
}

# Prints the slot's Hot Cargo log, and the line <<GAME EXITED>> when the slot's game no longer runs.
slot_log() {
  (
    export SLOT="$1"
    source "$LIB_DIR/pc.sh"
    pc "$PS_PATHS"'$l = Join-Path $modDir "csharp\hot_cargo_log.txt"; if (Test-Path $l) { Get-Content $l -Raw }; if (-not (Get-SlotProcess)) { "<<GAME EXITED>>" }'
  )
}

# Prints the tail of the slot's newest vanilla Cosmoteer log (rules errors and crashes show there).
slot_game_log() {
  (
    export SLOT="$1"
    source "$LIB_DIR/pc.sh"
    pc "$PS_PATHS"'$cl = Get-ChildItem (Join-Path $root "Logs") -Filter "*.txt" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1; if ($cl) { "=== " + $cl.Name; Get-Content $cl.FullName -Tail 150 }'
  ) 2>&1 || true
}

# True when a log (stdin) passed: it has AUTOTEST DONE and no AUTOTEST FAIL or ERROR line.
log_passed() {
  local log
  log="$(cat)"
  grep -q 'AUTOTEST DONE' <<<"$log" && ! grep -qE 'AUTOTEST FAIL| ERROR ' <<<"$log"
}

# Prints the lines of a log file that explain a failure, with a little context.
failure_lines() {
  grep -n -B2 -A4 -E 'AUTOTEST FAIL| ERROR |Exception|MP DESYNC|MPSTATE MISMATCH' "$1" | head -60 || true
  grep -q 'AUTOTEST DONE' "$1" || echo "(no AUTOTEST DONE: the game exited early, timed out, or the PC link failed)"
}
