#!/usr/bin/env bash
# Merge gate helpers: the flake quarantine and the suite time budget. Source it after autotest-lib.sh (needs STATE_DIR).

# --- Flake quarantine ---------------------------------------------------------------------------------------------------
# <container>/.state/quarantine.tsv, one line per test: name, runner (pc, mac or any), since, clean nightly runs, last
# failure log. A test that failed and then passed on retry goes in. A quarantined test still runs, but its failure does not
# block a merge (integrate.sh reports it). scripts/nightly.sh runs each one HOT_CARGO_QUARANTINE_RUNS times (default 3);
# after HOT_CARGO_QUARANTINE_NIGHTS clean nights in a row (default 3) it leaves the quarantine.
QUARANTINE="$STATE_DIR/quarantine.tsv"
QUARANTINE_LOCK="$STATE_DIR/quarantine.lock"

# Runs "$@" while it holds the quarantine lock (a mkdir lock; a lock older than 30 s is left by a killed writer).
with_quarantine_lock() {
  local rc
  until mkdir "$QUARANTINE_LOCK" 2>/dev/null; do
    if (( $(date +%s) - $(stat -f %m "$QUARANTINE_LOCK" 2>/dev/null || date +%s) > 30 )); then rm -rf "$QUARANTINE_LOCK"; fi
    sleep 0.2
  done
  "$@"; rc=$?
  rm -rf "$QUARANTINE_LOCK"
  return $rc
}

_quarantine_put() {
  local name="$1" runner="$2" log="$3" tmp
  tmp="$(mktemp)"
  touch "$QUARANTINE"
  awk -F'\t' -v n="$name" -v r="$runner" '!($1 == n && $2 == r)' "$QUARANTINE" > "$tmp"
  printf '%s\t%s\t%s\t0\t%s\n' "$name" "$runner" "$(date +%F)" "$log" >> "$tmp"
  mv "$tmp" "$QUARANTINE"
}
# quarantine_add <test> <pc|mac|any> <log of the failed run>: adds the test, or starts its clean count again.
quarantine_add() { with_quarantine_lock _quarantine_put "$@"; echo "== quarantine: $1 ($2), last failure $3" >&2; }

# is_quarantined <test> <pc|mac>: true when the test is in the quarantine for that runner or for any runner.
is_quarantined() { [[ -f "$QUARANTINE" ]] && awk -F'\t' -v n="$1" -v r="$2" '$1 == n && ($2 == r || $2 == "any") { f = 1 } END { exit !f }' "$QUARANTINE"; }

_quarantine_night() {
  local name="$1" runner="$2" clean="$3" tmp
  tmp="$(mktemp)"
  awk -F'\t' -v OFS='\t' -v n="$name" -v r="$runner" -v c="$clean" -v max="${HOT_CARGO_QUARANTINE_NIGHTS:-3}" '
    $1 == n && $2 == r { $4 = (c == "yes" ? $4 + 1 : 0); if ($4 >= max) { print "released " n " (" r ")" > "/dev/stderr"; next } }
    { print }' "$QUARANTINE" > "$tmp"
  mv "$tmp" "$QUARANTINE"
}
# quarantine_night <test> <runner> <yes|no>: counts one nightly result (yes: every run passed). Releases the test at the limit.
quarantine_night() { with_quarantine_lock _quarantine_night "$@"; }

# --- Suite time budget --------------------------------------------------------------------------------------------------
# No suite may take more than HOT_CARGO_SUITE_BUDGET seconds of wall time (default 600). Tests with the line
# "# budget: exempt" (the real playthrough) are not in the default suite lists and run on their own.
SUITE_BUDGET="${HOT_CARGO_SUITE_BUDGET:-600}"

# budget_exempt <test file>: true for a test with the line "# budget: exempt".
budget_exempt() { grep -qx '# budget: exempt' "$1"; }

# check_budget <suite label> <wall seconds> <test name>...: fails loudly when the suite took longer than the budget, unless
# every test in it is exempt.
check_budget() {
  local label="$1" wall="$2" t f all_exempt=yes; shift 2
  for t in "$@"; do
    if [[ "$t" == mp:* ]]; then f="tests/autotest-mp/${t#mp:}.txt"; else f="tests/autotest/$t.txt"; fi
    [[ -f "$f" ]] && budget_exempt "$f" || all_exempt=no
  done
  if (( wall > SUITE_BUDGET )) && [[ "$all_exempt" == no ]]; then
    echo "!! ===================================================================================="
    echo "!! BUDGET EXCEEDED: $label took $wall s of wall time; the budget is $SUITE_BUDGET s (HOT_CARGO_SUITE_BUDGET)."
    echo "!! Shorten or split the slowest tests (see the list above), or move checks into unit tests."
    echo "!! ===================================================================================="
    return 1
  fi
  echo "== budget: $label took $wall s of $SUITE_BUDGET s"
}

# slowest_tests <file>...: prints the 10 slowest tests from "== timing <name> ... total=<s>" lines in the files.
slowest_tests() {
  echo "== slowest tests (s):"
  cat "$@" 2>/dev/null | sed -n 's/^== timing \([^ ]*\).*total=\([0-9]*\).*/\2 \1/p' | sort -rn | head -10 | awk '{ printf "   %5d  %s\n", $1, $2 }'
}
