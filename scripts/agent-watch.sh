#!/usr/bin/env bash
# Coordinator's agent watch: one compact line per agent worktree, plus the slot and main-slot locks.
# Columns: minutes since the last commit, ahead/pushed, uncommitted files, running autotest.sh processes, held PC test
# slots, and how many of the 5 newest logs show a refused PC link (the agent's turn was not active).
# Usage: agent-watch.sh   (from any worktree; it reads the container folder that holds .bare)
set -uo pipefail
cd "$(dirname "$0")/../.."
now=$(date +%s)
echo "== $(date +%H:%M)  slots held: $(ls -d /tmp/hot_cargo_slot_[0-9]*.lock 2>/dev/null | wc -l | tr -d ' ')  main-slot lock: $([ -d /tmp/hot_cargo_main_slot.lock ] && basename "$(lsof -a -d cwd -p "$(cat /tmp/hot_cargo_main_slot.lock/pid 2>/dev/null)" -Fn 2>/dev/null | grep '^n' | cut -c2-)" || echo free)"
declare -A tests=()
for p in $(pgrep -f 'scripts/autotest.sh' 2>/dev/null); do
  w=$(lsof -a -d cwd -p "$p" -Fn 2>/dev/null | grep '^n' | cut -c2- | xargs basename 2>/dev/null)
  [[ -n "$w" ]] && tests[$w]=$(( ${tests[$w]:-0} + 1 ))
done
declare -A held=()
for l in /tmp/hot_cargo_slot_[0-9]*.lock; do
  [[ -d "$l" ]] || continue
  w=$(basename "$(cat "$l/owner" 2>/dev/null)" 2>/dev/null); [[ -n "$w" ]] && held[$w]=$(( ${held[$w]:-0} + 1 ))
done
for w in $(git --git-dir .bare worktree list --porcelain | awk '/^worktree /{print $2}' | xargs -n1 basename); do
  [[ "$w" == .bare || "$w" == play ]] && continue
  [[ -d "$w" ]] || continue
  last=$(git -C "$w" log -1 --format=%ct 2>/dev/null || echo "$now")
  pushed=$(git -C "$w" status -sb 2>/dev/null | head -1 | grep -o 'ahead [0-9]*' || true)
  dirty=$(git -C "$w" status --porcelain 2>/dev/null | wc -l | tr -d ' ')
  refused=$(ls -t "$(pwd)/.state/logs/$w"/*.log 2>/dev/null | head -5 | xargs grep -l "no longer owns an active thread run\|LINK REFUSED" 2>/dev/null | wc -l | tr -d ' ')
  printf "  %-16s commit %4dm ago  %-9s dirty %-3s tests %-3s slots %-2s refused-logs %s\n" "$w" $(( (now - last) / 60 )) "${pushed:-pushed}" "$dirty" "${tests[$w]:-0}" "${held[$w]:-0}" "$refused"
done
