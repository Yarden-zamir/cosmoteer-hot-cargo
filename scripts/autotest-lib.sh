#!/usr/bin/env bash
# Shared parts of autotest.sh and autotest-mp.sh. Source it; it does not run anything.
# Slots t1..tN (N = HOT_CARGO_SLOTS, default 6) are headless test instances on the PC (see pc.sh).
LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$LIB_DIR/.." && pwd)"
# Shared slot settings for every worktree: <container>/.state/slots.env (for example HOT_CARGO_SLOTS=6), read before each
# slot is taken, so a change applies to all agents at once without a merge. Environment variables set before the run win.
SLOTS_ENV="$(cd "$REPO_ROOT/.." && pwd)/.state/slots.env"
load_slot_settings() { if [[ -f "$SLOTS_ENV" ]]; then set -a; source "$SLOTS_ENV"; set +a; fi; SLOTS="${HOT_CARGO_SLOTS:-6}"; }
load_slot_settings
# Full logs of every run stay here, one folder per worktree, so a later run in the same slot cannot overwrite them.
# Logs, flaky list and pass markers live in <container>/.state (next to .bare and .game), not in /tmp, so they survive a restart.
STATE_DIR="${HOT_CARGO_STATE:-$(cd "$REPO_ROOT/.." && pwd)/.state}"
LOG_DIR="${HOT_CARGO_LOG_DIR:-$STATE_DIR/logs/$(basename "$REPO_ROOT")}"
mkdir -p "$LOG_DIR"

# Modification time of a file in seconds: GNU stat (Linux, CI) first, then BSD stat (macOS).
mtime() { stat -c %Y "$1" 2>/dev/null || stat -f %m "$1" 2>/dev/null; }

# True when a lock folder's owner process is gone. A lock with no pid yet is being created, so it is stale only when it
# is older than 60 s (its owner died between mkdir and the pid write; otherwise it would block a slot forever).
lock_is_stale() {
  local pid; pid="$(cat "$1/pid" 2>/dev/null)"
  if [[ -n "$pid" ]]; then ! kill -0 "$pid" 2>/dev/null; return; fi
  [[ -d "$1" ]] && (( $(date +%s) - $(mtime "$1" || echo 0) > 60 ))
}
# Writes this shell's pid into a lock folder in one rename, so a reader never sees an empty pid file.
lock_write_pid() { echo $$ > "$1/pid.tmp" && mv -f "$1/pid.tmp" "$1/pid"; }

# Folder of the PC slot locks. Only scripts/test-slot-locks.sh changes it, so its dummy takers never touch live locks.
LOCK_DIR="${HOT_CARGO_LOCK_DIR:-/tmp}"
slot_lock() { echo "$LOCK_DIR/hot_cargo_slot_${1#t}.lock"; }

# Removes the lock folders in "$@" whose owner is gone. One cleaner at a time (a mutex folder): two cleaners that judge the
# same old lock stale could otherwise remove the new lock that a third taker made in between. A cleaner holds the mutex for
# a moment only, so a mutex older than 30 s is left by a killed cleaner and is removed.
# Limit: two takers that both find such a dead mutex can still race on its removal; it needs a killed cleaner first.
clean_stale_locks() {
  local mutex="$LOCK_DIR/hot_cargo_lock_cleanup.lock" l
  if ! mkdir "$mutex" 2>/dev/null; then
    if (( $(date +%s) - $(mtime "$mutex" || date +%s) > 30 )); then rm -rf "$mutex"; fi
    return 0
  fi
  for l in "$@"; do if [[ -d "$l" ]] && lock_is_stale "$l"; then rm -rf "$l"; fi; done
  rm -rf "$mutex"
}

# Takes a lock folder for this shell: mkdir, the owner, then the pid. It counts only when the pid landed and reads back as
# this shell's pid; otherwise another taker's cleanup removed the folder in between, and the lock is not ours.
try_lock() {
  mkdir "$1" 2>/dev/null || return 1
  { echo "$REPO_ROOT" > "$1/owner" && lock_write_pid "$1" && [[ "$(cat "$1/pid" 2>/dev/null)" == "$$" ]]; } 2>/dev/null
}

# While the player's own game runs (slot main), tests use at most HOT_CARGO_SLOTS_WHILE_PLAYING slots (default 4), so
# the player's game keeps its frame rate. The check is cached for 60 s in a shared file, so it costs one PC call a minute.
PLAYING_CACHE=/tmp/hot_cargo_player_playing
usable_slots() {
  local now age
  now="$(date +%s)"
  age=$(( now - $(mtime "$PLAYING_CACHE" || echo 0) ))
  if [[ $age -gt 60 ]]; then
    # A failed PC call counts as playing, so a broken link never lets tests load the player's game.
    local out
    if out="$(SLOT=main bash -c 'source "$0/pc.sh"; pc "$PS_PATHS"'"'"'if (Get-SlotProcess) { "running" } else { "closed" }'"'"'' "$LIB_DIR" 2>/dev/null)" && [[ "$out" == *closed* ]]; then echo no > "$PLAYING_CACHE"; else echo yes > "$PLAYING_CACHE"; fi
  fi
  load_slot_settings
  if [[ "$(cat "$PLAYING_CACHE" 2>/dev/null)" == yes ]]; then echo "${HOT_CARGO_SLOTS_WHILE_PLAYING:-4}"; else echo "$SLOTS"; fi
}

# Waits for one free test slot, locks it for this shell process and prints its name.
# A lock whose owner process is gone is stale and is taken over.
take_one_slot() {
  local i lock limit busy deadline="${1:-}"
  while :; do
    if [[ -n "$deadline" && $(date +%s) -ge $deadline ]]; then return 1; fi
    limit="$(usable_slots)"
    # Off by default: HOT_CARGO_INTEGRATOR_RESERVE slots stay free for the main worktree, if contention returns.
    if [[ "$(basename "$REPO_ROOT")" != main && ${HOT_CARGO_INTEGRATOR_RESERVE:-0} -gt 0 && $limit -gt ${HOT_CARGO_INTEGRATOR_RESERVE:-0} ]]; then limit=$((limit - HOT_CARGO_INTEGRATOR_RESERVE)); fi
    # Off by default: with HOT_CARGO_SLOTS_PER_AGENT > 0, an agent worktree holds at most that many slots, so one agent's big batch
    # cannot starve the others. The main worktree (the integrator) has no cap.
    if [[ "$(basename "$REPO_ROOT")" != main && ${HOT_CARGO_SLOTS_PER_AGENT:-0} -gt 0 ]]; then
      local mine=0 l
      for l in "$LOCK_DIR"/hot_cargo_slot_[0-9]*.lock; do [[ "$(cat "$l/owner" 2>/dev/null)" == "$REPO_ROOT" ]] && mine=$((mine + 1)); done
      if [[ $mine -ge ${HOT_CARGO_SLOTS_PER_AGENT:-0} ]]; then sleep 5; continue; fi
    fi
    clean_stale_locks "$LOCK_DIR"/hot_cargo_slot_[0-9]*.lock
    # Only the numbered slot locks count; the co-op pair lock is a queue, not a slot.
    busy="$(ls -d "$LOCK_DIR"/hot_cargo_slot_[0-9]*.lock 2>/dev/null | wc -l | tr -d ' ')"
    if [[ $busy -ge $limit ]]; then sleep 2; continue; fi
    # Only slot numbers up to the limit, so mkdir enforces the limit: two takers that both counted one free slot cannot both
    # take a slot. The busy count above covers higher slots that are still held after the limit went down.
    for ((i = 1; i <= limit && i <= SLOTS; i++)); do
      lock="$(slot_lock "t$i")"
      if try_lock "$lock"; then echo "t$i"; return; fi
    done
    sleep 2
  done
}

# Takes N slots and prints their names: all N or none. Two-slot takers (co-op tests) queue on one global lock and then
# hold each slot as it frees up, so single-slot tests that grab every freed slot cannot starve a co-op test. Only one taker
# at a time holds some slots while it waits for more, so they cannot deadlock.
# The queue lock and the slots it collects are held at most HOT_CARGO_PAIR_WAIT seconds (default 600) in total, counted
# from taking the queue lock. Then the taker gives everything back and lines up again, so it never blocks every co-op test.
# HOT_CARGO_PAIR_POLL and HOT_CARGO_PAIR_RETRY (seconds, default 5 and 30) only shorten scripts/test-slot-locks.sh.
take_slots() {
  local want="${1:-1}" got=() pair="$LOCK_DIR/hot_cargo_slot_pair.lock" s deadline
  if [[ "$want" -eq 1 ]]; then take_one_slot; return; fi
  while :; do
    until try_lock "$pair"; do
      clean_stale_locks "$pair"
      sleep "${HOT_CARGO_PAIR_POLL:-5}"
    done
    deadline=$(( $(date +%s) + ${HOT_CARGO_PAIR_WAIT:-600} ))
    got=()
    while [[ ${#got[@]} -lt "$want" ]] && s="$(take_one_slot "$deadline")"; do got+=("$s"); done
    if [[ ${#got[@]} -eq "$want" ]]; then rm -rf "$pair"; echo "${got[*]}"; return; fi
    for s in "${got[@]}"; do rm -rf "$(slot_lock "$s")"; done
    rm -rf "$pair"
    echo "co-op test: no $want slots within ${HOT_CARGO_PAIR_WAIT:-600} s; lining up again" >&2
    sleep "${HOT_CARGO_PAIR_RETRY:-30}"
  done
}
take_slot() { take_slots 1; }

# Stops the slot's game and frees the slot.
release_slot() {
  SLOT="$1" "$LIB_DIR/stop.sh" >/dev/null 2>&1 || true
  rm -rf "$(slot_lock "$1")"
}

# Deploys this worktree to the slot, installs the autotest script from stdin and starts the game, in one PC call
# (deploy.sh autotest mode). A test slot's running game is stopped first; the main slot fails if its game runs.
start_slot() {
  local slot="$1" script rc=0
  script="$(mktemp)"
  cat > "$script"
  AUTOTEST_SCRIPT="$script" SLOT="$slot" "$LIB_DIR/deploy.sh" </dev/null || rc=$?
  rm -f "$script"
  return $rc
}

# T3 refuses PC calls from an agent thread that has no active turn (the agent ended its turn while its tests run).
# Every later call fails the same way, so a run that sees this text stops at once (exit code 3, no retry).
LINK_REFUSED_TEXT="no longer owns an active thread run"

# Prints one line about the slot's game: "running=0|1 started=0|1 done=0|1 match=0|1".
# started: the log has a script line. done: the log has AUTOTEST DONE. match: the log has the text in $2 (optional).
# A small PC call, so polls can be frequent. When the PC call fails, it prints the error text in place of the line.
slot_status() {
  (
    export SLOT="$1"
    source "$LIB_DIR/pc.sh"
    pc "$PS_PATHS"'$l = Join-Path $modDir "csharp\hot_cargo_log.txt"; $t = if (Test-Path $l) { [string](Get-Content $l -Raw) } else { "" }
$m = "'"${2:-}"'"
"running=" + [int][bool](Get-SlotProcess) + " started=" + [int]$t.Contains("AUTOTEST line") + " done=" + [int]$t.Contains("AUTOTEST DONE") + " match=" + [int]($m -ne "" -and $t.Contains($m))'
  ) 2>&1 || true
}

# Waits until the slot's game exits or its log has AUTOTEST DONE for two polls in a row (the game then had time to
# exit by itself), or until $2 seconds pass. Polls every AUTOTEST_POLL seconds (default 3).
# Stops early when the PC refuses calls (sets LINK_REFUSED=yes) or after 5 failed status calls in a row.
# Sets STARTED_AT (SECONDS when the first script line showed, empty if never) and ENDED_AT.
wait_slot_end() {
  local slot="$1" timeout="$2" from=$SECONDS status dones=0 fails=0
  STARTED_AT="" LINK_REFUSED=no
  while (( SECONDS - from < timeout )); do
    sleep "${AUTOTEST_POLL:-3}"
    status="$(slot_status "$slot")"
    if [[ "$status" == *"$LINK_REFUSED_TEXT"* ]]; then LINK_REFUSED=yes; break; fi
    if [[ "$status" != *running=* ]]; then fails=$((fails + 1)); [[ $fails -ge 5 ]] && break; continue; fi
    fails=0
    if [[ -z "$STARTED_AT" && "$status" == *started=1* ]]; then STARTED_AT=$SECONDS; fi
    if [[ "$status" == *running=0* ]]; then break; fi
    if [[ "$status" == *done=1* ]]; then dones=$((dones + 1)); [[ $dones -ge 2 ]] && break; fi
  done
  ENDED_AT=$SECONDS
}

# Test slots only: stops the slot's game and prints its Hot Cargo log, then the line <<<VANILLA LOG>>> and the tail of
# its newest vanilla log, in one PC call. Fails when the PC call fails (then the game may still run).
slot_finish() {
  [[ "$1" != main ]] || { echo "slot_finish: never for the main slot" >&2; return 1; }
  (
    export SLOT="$1"
    source "$LIB_DIR/pc.sh"
    pc "$PS_PATHS"'$p = Get-SlotProcess; if ($p) { $p | Stop-Process -Force; $p | Wait-Process -Timeout 20 -ErrorAction SilentlyContinue }; if (Test-Path $pidFile) { Remove-Item $pidFile -Force }
$l = Join-Path $modDir "csharp\hot_cargo_log.txt"; if (Test-Path $l) { Get-Content $l -Raw }
"<<<VANILLA LOG>>>"
$cl = Get-ChildItem (Join-Path $root "Logs") -Filter "*.txt" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1; if ($cl) { "=== " + $cl.Name; Get-Content $cl.FullName -Tail 150 }'
  )
}

# Prints the slot's Hot Cargo log, and the line <<GAME EXITED>> when the slot's game no longer runs.
slot_log() {
  (
    export SLOT="$1"
    source "$LIB_DIR/pc.sh"
    pc "$PS_PATHS"'$l = Join-Path $modDir "csharp\hot_cargo_log.txt"; if (Test-Path $l) { Get-Content $l -Raw }; if (-not (Get-SlotProcess)) { "<<GAME EXITED>>" }'
  )
}

# The two scripts of a co-op session (any runner: PC slots, later the headless sim). Args: <session file> <port>.
# Host: the session file without its "client:" lines. Client: those lines (settings before the join), then client.txt.
# "{PORT}" becomes the port. The first line names the run (telename <session>.host / .client); a pass needs it in the log.
coop_host_script() { echo "telename $(basename "$1" .txt).host"; grep -v '^client:' "$1" | sed "s/{PORT}/$2/g"; }
coop_client_script() {
  echo "telename $(basename "$1" .txt).client"; sed -n 's/^client: *//p' "$1"; sed "s/{PORT}/$2/g" "$(dirname "$1")/client.txt"
}

# Prints the names of the co-op sessions: every tests/autotest-mp/*.txt except client.txt (the generic client).
coop_sessions() { local f; for f in "$REPO_ROOT"/tests/autotest-mp/*.txt; do [[ "$(basename "$f")" == client.txt ]] || basename "$f" .txt; done; }

# True when a log (stdin) passed: it has AUTOTEST DONE and no AUTOTEST FAIL or ERROR line.
log_passed() {
  local log
  log="$(cat)"
  grep -q 'AUTOTEST DONE' <<<"$log" && ! grep -qE 'AUTOTEST FAIL| ERROR ' <<<"$log"
}

# Prints the lines of a log file that explain a failure, with a little context.
failure_lines() {
  grep -n -B2 -A4 -E 'AUTOTEST FAIL| ERROR |Exception|MP DESYNC|MPSTATE MISMATCH' "$1" | head -60 || true
  grep -q 'AUTOTEST DONE' "$1" || { echo "(no AUTOTEST DONE: the game exited early, timed out, or the PC link failed; first log lines:)"; grep -v "^$" "$1" | head -6; }
}
