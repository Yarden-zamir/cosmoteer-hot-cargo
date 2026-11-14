#!/usr/bin/env bash
# Collects the test logs of a CI run for upload and checks that they hold no game data: only text logs (*.log, *.out,
# *.txt) of at most 20 MB each, with no binary content. Game files (rules, images, dlls) and the staged mod never go in.
# A colon in a name (mp:<session>) becomes "_": artifact names may not contain it.
# Usage: ci-check-logs.sh <logs folder> <upload folder>   Exit code 1 (and nothing copied) when a file breaks a rule.
set -euo pipefail
SRC="${1:?usage: ci-check-logs.sh <logs folder> <upload folder>}" DST="${2:?usage: ci-check-logs.sh <logs folder> <upload folder>}"
mkdir -p "$DST"
[[ -d "$SRC" ]] || { echo "ci-check-logs: no logs in $SRC"; exit 0; }
# The stage (the built mod) lives in the run folders; it is not a log.
mapfile -t FILES < <(find "$SRC" -type f -not -path '*/stage/*')
BAD=0
for f in "${FILES[@]}"; do
  case "$f" in *.log|*.out|*.txt) ;; *) echo "ci-check-logs: not a log: ${f#"$SRC"/}"; BAD=1; continue ;; esac
  if (( $(wc -c < "$f") > 20 * 1024 * 1024 )); then echo "ci-check-logs: over 20 MB: ${f#"$SRC"/}"; BAD=1; continue; fi
  if LC_ALL=C grep -q -P '\x00' "$f" 2>/dev/null; then echo "ci-check-logs: binary content: ${f#"$SRC"/}"; BAD=1; fi
done
[[ $BAD -eq 0 ]] || exit 1
for f in "${FILES[@]}"; do rel="${f#"$SRC"/}"; rel="${rel//:/_}"; mkdir -p "$DST/$(dirname "$rel")"; cp "$f" "$DST/$rel"; done
echo "ci-check-logs: ${#FILES[@]} log files checked"
