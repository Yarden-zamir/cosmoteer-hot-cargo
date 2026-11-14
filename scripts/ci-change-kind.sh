#!/usr/bin/env bash
# The kind of a change, for the game test set of a pull request (scripts/ci-test-set.sh). Reads the changed paths, one per
# line, on stdin. Prints one of these, then the reason:
#   none     every path is a doc (*.md, docs/, spec/) or a script that the game tests never run: no game tests by default.
#   harness  a path is check.yml, the headless sim host (tools/headless-sim/) or a script that the game tests run: every
#            game test, because a change there can break any test.
#   game     anything else: the affected tests (scripts/autotest-affected.sh).
# The scripts that the game tests run: start with the script names in check.yml, then add each tracked shell script
# whose file name a found script mentions on a line that is not a comment. Repeat until no script is added.
# Limits of this rule:
# - A file name match also matches other files with the same name (tools/headless-sim/run.sh adds scripts/run.sh). This
#   runs more tests than necessary, never fewer.
# - A script that builds a script path from variables (for example "$dir/$name.sh") is not found. If a script starts to
#   do that, change this rule.
set -euo pipefail
cd "$(dirname "$0")/.."

code_lines() { grep -v '^[[:space:]]*#' "$@" || true; }
mentioned() { [[ "$TEXT" == *"$(basename "$1")"* ]]; }

TEXT="$(code_lines .github/workflows/check.yml)"
SCRIPTS=()
mapfile -t ALL < <(git ls-files '*.sh' '*.ps1')
added=1
while (( added )); do
  added=0
  for f in "${ALL[@]}"; do
    [[ " ${SCRIPTS[*]} " == *" $f "* ]] && continue
    if mentioned "$f"; then SCRIPTS+=("$f"); TEXT+=$'\n'"$(code_lines "$f")"; added=1; fi
  done
done

game="" count=0
while IFS= read -r path; do
  [[ -n "$path" ]] || continue
  count=$((count + 1))
  case "$path" in
    *.md | docs/* | spec/*) continue ;;
    .github/workflows/check.yml | tools/headless-sim/*) echo "harness ($path)"; exit 0 ;;
    scripts/*) if mentioned "$path"; then echo "harness ($path)"; exit 0; fi; continue ;;
  esac
  [[ -n "$game" ]] || game="$path"
done
(( count > 0 )) || { echo "harness (no changed paths)"; exit 0; }
if [[ -n "$game" ]]; then echo "game ($game)"; exit 0; fi
echo "none (only docs and scripts that the game tests do not run)"
