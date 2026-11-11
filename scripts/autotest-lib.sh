#!/usr/bin/env bash
# Shared parts of autotest.sh and autotest-mp.sh. Source it; it does not run anything.
# Slots t1..tN (N = HOT_CARGO_SLOTS, default 4) are headless test instances on the PC (see pc.sh).
LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$LIB_DIR/.." && pwd)"
SLOTS="${HOT_CARGO_SLOTS:-4}"

slot_lock() { echo "/tmp/hot_cargo_slot_${1#t}.lock"; }

# Waits for a free test slot, locks it for this shell process and prints its name.
# A lock whose owner process is gone is stale and is taken over.
take_slot() {
  local i lock
  while :; do
    for ((i = 1; i <= SLOTS; i++)); do
      lock="$(slot_lock "t$i")"
      if mkdir "$lock" 2>/dev/null; then echo $$ > "$lock/pid"; echo "$REPO_ROOT" > "$lock/owner"; echo "t$i"; return; fi
      if [[ -f "$lock/pid" ]] && ! kill -0 "$(cat "$lock/pid")" 2>/dev/null; then rm -rf "$lock"; fi
    done
    echo "all $SLOTS test slots busy; waiting" >&2
    sleep 15
  done
}

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

# True when a log (stdin) passed: it has AUTOTEST DONE and no AUTOTEST FAIL or ERROR line.
log_passed() {
  local log
  log="$(cat)"
  grep -q 'AUTOTEST DONE' <<<"$log" && ! grep -qE 'AUTOTEST FAIL| ERROR ' <<<"$log"
}
