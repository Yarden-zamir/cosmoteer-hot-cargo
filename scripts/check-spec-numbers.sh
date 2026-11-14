#!/usr/bin/env bash
# Fail when two spec files share a section number (the file name prefix, for example "42-") or two requirement lines in
# spec/*.md share a number ("1004. "). Parallel branches often pick the same next numbers; in the merge queue this check
# catches a clash between two pull requests. Runs in CI (.github/workflows/check.yml, job private-data) and in integrate.sh.
set -euo pipefail
cd "$(dirname "$0")/.."
rc=0
dup_sections="$(ls spec | cut -d- -f1 | sort | uniq -d | tr '\n' ' ')"
if [[ -n "$dup_sections" ]]; then echo "check-spec-numbers: two spec files share the section number: $dup_sections"; rc=1; fi
dup_reqs="$(cat spec/*.md | grep -oE '^[0-9]+\. ' | sort | uniq -d | tr -d '. ' | tr '\n' ' ' || true)"
if [[ -n "$dup_reqs" ]]; then
  echo "check-spec-numbers: requirement numbers used twice: $dup_reqs"
  for n in $dup_reqs; do grep -n "^$n\. " spec/*.md | cut -c1-120; done
  rc=1
fi
(( rc == 0 )) && echo "check-spec-numbers: OK"
exit "$rc"
