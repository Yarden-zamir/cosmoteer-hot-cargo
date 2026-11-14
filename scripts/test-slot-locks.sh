#!/usr/bin/env bash
# Tests the PC slot takers of scripts/autotest-lib.sh with dummy takers on this Mac: no PC, no game, an own lock folder
# (HOT_CARGO_LOCK_DIR), so live test runs are not touched. Each dummy taker is its own process, like a real test run.
# Usage: test-slot-locks.sh            runs every scenario; exit code 0 when all checks hold. TEST_SLOTS: slot count (default 6).
# Checks: no slot has two holders, at most LIMIT slots are taken, every taker finishes, stale locks of dead owners are
# cleaned without removing new locks, and a co-op taker holds the pair lock at most HOT_CARGO_PAIR_WAIT (+ a poll) seconds.
set -uo pipefail
cd "$(dirname "$0")/.."
LIMIT=4

# A dummy taker (internal mode): waits <delay> seconds (default 0), takes <want> slots, logs "take <slot> <pid>" while it holds them, waits <hold> seconds,
# logs "free <slot> <pid>" and frees them. The log lines are written while the lock is held, so their order is the lock order.
if [[ "${1:-}" == taker ]]; then
  source scripts/autotest-lib.sh
  # Fixed settings: the shared .state/slots.env must not change the test.
  SLOTS="${TEST_SLOTS:-6}" HOT_CARGO_SLOTS_PER_AGENT=0 HOT_CARGO_INTEGRATOR_RESERVE=0
  usable_slots() { echo "$LIMIT"; }
  sleep "${4:-0}"
  slots="$(take_slots "$2")" || exit 1
  for s in $slots; do echo "take $s $$" >> "$HOT_CARGO_LOCK_DIR/events.log"; done
  sleep "$3"
  for s in $slots; do echo "free $s $$" >> "$HOT_CARGO_LOCK_DIR/events.log"; rm -rf "$(slot_lock "$s")"; done
  echo "done $$ want=$2" >> "$HOT_CARGO_LOCK_DIR/events.log"
  exit 0
fi

FAILED=0
check() { if eval "$2"; then echo "  ok   $1"; else echo "  FAIL $1"; FAILED=1; fi; }

# Samples the lock folder every 0.1 s: the most slot locks with a live owner at once, and the longest hold of one pair lock
# (same pid and mtime).
monitor() {
  local dir="$1" max=0 n pair key first=0 longest=0 last="" now l pid
  while [[ -d "$dir" && ! -e "$dir/stop" ]]; do
    n=0
    for l in "$dir"/hot_cargo_slot_[0-9]*.lock; do pid="$(cat "$l/pid" 2>/dev/null)"; [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null && n=$((n + 1)); done
    (( n > max )) && max=$n
    pair="$dir/hot_cargo_slot_pair.lock"
    now=$EPOCHREALTIME
    if key="$(cat "$pair/pid" 2>/dev/null)-$(mtime "$pair")" && [[ -d "$pair" && "$key" != -* ]]; then
      if [[ "$key" != "$last" ]]; then last=$key; first=$now; fi
      longest=$(awk -v a="$longest" -v b="$now" -v c="$first" 'BEGIN { d = b - c; print (d > a ? d : a) }')
    else
      last=""
    fi
    echo "$max $longest" > "$dir/monitor.txt"
    sleep 0.1
  done
}

# Replays the event log: a "take" of a slot that another taker holds is a double holder.
double_holders() {
  awk '$1 == "take" { if (h[$2] != "" && h[$2] != $3) bad++; h[$2] = $3 } $1 == "free" { if (h[$2] == $3) h[$2] = "" } END { print bad + 0 }' "$1"
}

# Runs one scenario: <name> <timeout> then taker specs "<want>:<hold>"; extra setup comes from the SETUP function.
scenario() {
  local name="$1" timeout="$2"; shift 2
  local dir pids=() spec start takers=$#
  dir="$(mktemp -d)"
  echo "== $name"
  export HOT_CARGO_LOCK_DIR="$dir"
  : > "$dir/events.log"
  if declare -F setup_scenario >/dev/null; then setup_scenario "$dir"; fi
  monitor "$dir" & local mon=$!
  start=$SECONDS
  # A taker spec is "<want>:<hold>[:<delay>]".
  for spec in "$@"; do IFS=: read -r want hold delay <<<"$spec"; "$0" taker "$want" "$hold" "${delay:-0}" 2>> "$dir/takers.err" & pids+=($!); done
  local finished=yes
  for p in "${pids[@]}"; do
    while kill -0 "$p" 2>/dev/null; do
      if (( SECONDS - start > timeout )); then finished=no; kill "$p" 2>/dev/null; fi
      sleep 0.2
    done
  done
  touch "$dir/stop"; wait "$mon" 2>/dev/null
  read -r MAX LONGEST < "$dir/monitor.txt"
  DONE=$(grep -c '^done ' "$dir/events.log"); DOUBLE=$(double_holders "$dir/events.log")
  GIVEBACKS=$(grep -c 'lining up again' "$dir/takers.err" 2>/dev/null || true)
  check "every taker finished within $timeout s ($DONE of $takers done)" '[[ $finished == yes && $DONE -eq $takers ]]'
  check "no slot had two holders ($DOUBLE)" '[[ $DOUBLE -eq 0 ]]'
  check "at most $LIMIT slots taken at once (max $MAX)" '[[ $MAX -le $LIMIT ]]'
  SCENARIO_DIR="$dir"
}

# 1. Stale locks: every slot and the pair lock start with a dead owner. All takers start at once and clean them together.
setup_scenario() {
  local i dead
  bash -c 'exit 0' & dead=$!; wait "$dead"
  for i in 1 2 3 4 5 6 pair; do mkdir "$1/hot_cargo_slot_$i.lock"; echo "$dead" > "$1/hot_cargo_slot_$i.lock/pid"; done
}
scenario "stale locks of dead owners, 12 single and 3 co-op takers at once" 90 \
  1:1 1:1 1:1 1:1 1:1 1:1 1:1 1:1 1:1 1:1 1:1 1:1 2:1 2:1 2:1
unset -f setup_scenario
rm -rf "$SCENARIO_DIR"

# 2. Contention: many single takers with different hold times and 4 co-op takers.
export HOT_CARGO_PAIR_POLL=1 HOT_CARGO_PAIR_RETRY=1
scenario "16 single and 4 co-op takers" 120 \
  1:1 1:2 1:3 1:1 1:2 1:3 1:1 1:2 1:3 1:1 1:2 1:3 1:1 1:2 1:3 1:1 2:2 2:2 2:2 2:2
rm -rf "$SCENARIO_DIR"

# 3. Give-back: 3 of the 4 slots stay taken for 20 s. A co-op taker gets 1 slot, then must give the pair lock and its slot
#    back after HOT_CARGO_PAIR_WAIT (4 s) instead of holding them until a second slot frees.
export HOT_CARGO_PAIR_WAIT=4
scenario "co-op taker gives back the pair lock after HOT_CARGO_PAIR_WAIT=4 s" 60 1:20 1:20 1:20 2:1:2
check "the pair lock was held at most 4 s + 3 s of polls (longest $LONGEST s)" 'awk -v l="$LONGEST" "BEGIN { exit !(l <= 7) }"'
check "the co-op taker gave back and lined up again ($GIVEBACKS times)" '[[ $GIVEBACKS -ge 1 ]]'
rm -rf "$SCENARIO_DIR"

# 4. The case of a 34 min hold: every slot stays taken for 20 s, so the co-op taker waits for its first slot. It must give
#    the pair lock back after HOT_CARGO_PAIR_WAIT too, not hold it until a slot frees.
scenario "co-op taker waiting for its first slot gives back after 4 s" 60 1:20 1:20 1:20 1:20 2:1:2
check "the pair lock was held at most 4 s + 3 s of polls (longest $LONGEST s)" 'awk -v l="$LONGEST" "BEGIN { exit !(l <= 7) }"'
rm -rf "$SCENARIO_DIR"

if [[ $FAILED -eq 0 ]]; then echo "== slot lock tests passed"; else echo "== slot lock tests FAILED"; fi
exit $FAILED
