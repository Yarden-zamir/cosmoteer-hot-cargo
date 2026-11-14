#!/usr/bin/env bash
# Decide if a change needs the game tests (.github/workflows/check.yml, job build). Reads the changed paths, one per line,
# on stdin. Prints "true" or "false" and the reason. Exit code 0 in both cases.
# A change needs no game tests only when each path is a doc (*.md, docs/, spec/) or a script that the game tests never run.
# The scripts that the game tests run: start with the script names in check.yml, then add each tracked shell script
# whose file name a found script mentions on a line that is not a comment. Repeat until no script is added.
# Limits of this rule:
# - A file name match also matches other files with the same name (tools/headless-sim/run.sh adds scripts/run.sh). This
#   runs the game tests more often than necessary, never less.
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

count=0
while IFS= read -r path; do
  [[ -n "$path" ]] || continue
  count=$((count + 1))
  case "$path" in
    *.md | docs/* | spec/*) continue ;;
    scripts/*) mentioned "$path" || continue ;;
  esac
  echo "true ($path)"
  exit 0
done
(( count > 0 )) || { echo "true (no changed paths)"; exit 0; }
echo "false (only docs and scripts that the game tests do not run; the game tests run: ${SCRIPTS[*]})"
