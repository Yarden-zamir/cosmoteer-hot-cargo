#!/usr/bin/env bash
# Run a command detached from this shell: own session (setsid) under nohup, output in <container>/.state/suites/.
# A killed caller, watcher or agent shell does not stop it. Prints the log path. Wait with: scripts/suite-wait.sh <log>
# The log starts with "== suite pid <pid>" and ends with "== suite exit <code>".
# Usage: detach.sh <name> <command> [args...]
set -euo pipefail
NAME="${1:?usage: detach.sh <name> <command> [args...]}"
shift
[[ $# -gt 0 ]] || { echo "usage: detach.sh <name> <command> [args...]" >&2; exit 1; }
STATE_DIR="${HOT_CARGO_STATE:-$(cd "$(dirname "$0")/../.." && pwd)/.state}"
mkdir -p "$STATE_DIR/suites"
LOG="$STATE_DIR/suites/$NAME-$(date +%Y%m%d-%H%M%S)-$$.log"
# perl is part of macOS; it gives setsid without extra tools. bash -c then runs as the session leader.
nohup perl -MPOSIX -e 'POSIX::setsid() or die "setsid: $!"; exec @ARGV or die "exec: $!"' \
  bash -c 'echo "== suite pid $$"; "$@"; echo "== suite exit $?"' suite "$@" > "$LOG" 2>&1 < /dev/null &
echo "$LOG"
