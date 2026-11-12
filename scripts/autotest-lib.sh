#!/usr/bin/env bash
# Shared parts of autotest.sh and autotest-mp.sh. Source it; it does not run anything.
# Slots t1..tN (N = HOT_CARGO_SLOTS, default 8) are headless test instances on the PC (see pc.sh).
LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$LIB_DIR/.." && pwd)"
SLOTS="${HOT_CARGO_SLOTS:-8}"
# Full logs of every run stay here, one folder per worktree, so a later run in the same slot cannot overwrite them.
# Logs, flaky list and pass markers live in <container>/.state (next to .bare and .game), not in /tmp, so they survive a restart.
STATE_DIR="${HOT_CARGO_STATE:-$(cd "$REPO_ROOT/.." && pwd)/.state}"
LOG_DIR="${HOT_CARGO_LOG_DIR:-$STATE_DIR/logs/$(basename "$REPO_ROOT")}"
mkdir -p "$LOG_DIR"

slot_lock() { echo "/tmp/hot_cargo_slot_${1#t}.lock"; }

# While the player's own game runs (slot main), tests use at most HOT_CARGO_SLOTS_WHILE_PLAYING slots (default 3), so
# the player's game keeps its frame rate. The check is cached for 60 s in a shared file, so it costs one PC call a minute.
PLAYING_CACHE=/tmp/hot_cargo_player_playing
usable_slots() {
  local now age
  now="$(date +%s)"
  age=$(( now - $(stat -f %m "$PLAYING_CACHE" 2>/dev/null || echo 0) ))
  if [[ $age -gt 60 ]]; then
    if [[ -n "$(SLOT=main bash -c 'source "$0/pc.sh"; pc "$PS_PATHS"Get-SlotProcess' "$LIB_DIR" 2>/dev/null)" ]]; then echo yes > "$PLAYING_CACHE"; else echo no > "$PLAYING_CACHE"; fi
  fi
  if [[ "$(cat "$PLAYING_CACHE" 2>/dev/null)" == yes ]]; then echo "${HOT_CARGO_SLOTS_WHILE_PLAYING:-3}"; else echo "$SLOTS"; fi
}

# Waits for one free test slot, locks it for this shell process and prints its name.
# A lock whose owner process is gone is stale and is taken over.
take_one_slot() {
  local i lock limit busy
  while :; do
    limit="$(usable_slots)"
    busy="$(ls -d /tmp/hot_cargo_slot_*.lock 2>/dev/null | wc -l | tr -d ' ')"
    if [[ $busy -ge $limit ]]; then sleep 5; continue; fi
    for ((i = 1; i <= SLOTS; i++)); do
      lock="$(slot_lock "t$i")"
      if mkdir "$lock" 2>/dev/null; then echo $$ > "$lock/pid"; echo "$REPO_ROOT" > "$lock/owner"; echo "t$i"; return; fi
      if [[ -f "$lock/pid" ]] && ! kill -0 "$(cat "$lock/pid")" 2>/dev/null; then rm -rf "$lock"; fi
    done
    sleep 5
  done
}

# Takes N slots and prints their names. Two-slot takers (co-op tests) queue on one global lock and then hold each slot
# as it frees up. Only one taker at a time holds some slots while it waits for more, so they cannot deadlock, and
# single-slot tests that grab every freed slot cannot starve a co-op test.
take_slots() {
  local want="${1:-1}" got=() pair=/tmp/hot_cargo_slot_pair.lock
  if [[ "$want" -eq 1 ]]; then take_one_slot; return; fi
  until mkdir "$pair" 2>/dev/null; do
    if [[ -f "$pair/pid" ]] && ! kill -0 "$(cat "$pair/pid")" 2>/dev/null; then rm -rf "$pair"; continue; fi
    sleep 5
  done
  echo $$ > "$pair/pid"
  while [[ ${#got[@]} -lt "$want" ]]; do got+=("$(take_one_slot)"); done
  rm -rf "$pair"
  echo "${got[*]}"
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
