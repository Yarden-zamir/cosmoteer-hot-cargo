#!/usr/bin/env bash
# The game tests that the merge queue runs for pull requests (.github/workflows/check.yml, job build).
# Usage: ci-test-set.sh <pr number> ...             the union of the sets of these pull requests
#        ci-test-set.sh --group <base sha> <head sha> the pull requests of a merge queue group (merge commits "<title> (#N)")
#        ci-test-set.sh --all                       every CI game test (manual and scheduled full runs)
# Needs gh with a token (GH_TOKEN) and GITHUB_REPOSITORY. Prints the reasons, then two lines for the workflow:
#   TESTS <names>   the test names for scripts/ci-sim-tests.sh ("mp:<session>" for co-op), empty for none
#   SHARDS <n>      the number of shard jobs for these tests (0 for none)
#   FULL yes|no     yes when the set is every CI game test (the full-suite check)
# Exit code 1 when a pull request text has an error (an unknown test, "none" without a reason).
#
# The set of one pull request:
# - Default by the kind of change (scripts/ci-change-kind.sh): none: no tests. harness: every test. game: the affected
#   tests (scripts/autotest-affected.sh, runner sim) and the baseline below.
# - Then its "### Game tests" block in the pull request text changes the set, one line each: "+ <test>" adds, "- <test>"
#   removes, "all" takes every test, "none: <reason>" takes no test. A co-op session can go with or without "mp:".
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/autotest-lib.sh

# The baseline: fast tests that every pull request with game changes runs, so that a change outside the mapped areas
# still meets the main flow of a run (heat, stages, save and load; the tech vaults and their sale).
BASELINE=(core tech-start)
# One shard job per this much test weight; a co-op session weighs as many single-player tests (2 sim slots for its
# whole real-time run). At most MAX_SHARDS jobs: two merge queue groups must fit in the ~20 parallel jobs.
SHARD_WEIGHT=24 SESSION_WEIGHT=6 MAX_SHARDS=6

REPO="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY must be set}"
fail() { echo "ci-test-set: $*" >&2; exit 1; }

# Every test that CI can run (not "# runner: pc"), co-op sessions as mp:<name>.
ALL_TESTS=()
for n in $(coop_sessions); do grep -qx '# runner: pc' "tests/autotest-mp/$n.txt" || ALL_TESTS+=("mp:$n"); done
for f in tests/autotest/*.txt; do grep -qx '# runner: pc' "$f" || ALL_TESTS+=("$(basename "$f" .txt)"); done

# A test name from a pull request text: the CI name, or an error that names the problem.
test_name() {
  local n="${1#mp:}"
  if [[ -f "tests/autotest-mp/$n.txt" && "$n" != client ]]; then f="tests/autotest-mp/$n.txt" n="mp:$n"
  elif [[ -f "tests/autotest/$n.txt" ]]; then f="tests/autotest/$n.txt"
  else fail "PR #$PR: no test '$1' (tests/autotest/<name>.txt or a co-op session in tests/autotest-mp/)"; fi
  grep -qx '# runner: pc' "$f" && fail "PR #$PR: '$1' runs only on the PC (# runner: pc), not in CI"
  echo "$n"
}

declare -A SET=()
pr_set() {
  PR="$1"
  local files kind block line name
  files="$(mktemp)"
  gh api "repos/$REPO/pulls/$PR/files" --paginate --jq '.[] | .filename, (.previous_filename // empty)' > "$files"
  kind="$(scripts/ci-change-kind.sh < "$files")"
  declare -A mine=()
  case "${kind%% *}" in
    none) ;;
    harness) for t in "${ALL_TESTS[@]}"; do mine[$t]=1; done ;;
    game)
      for t in $(CHANGED_FILE="$files" LIST_ONLY=1 RUNNER=sim scripts/autotest-affected.sh | tail -1 | sed 's/^== affected (runner sim): //'); do
        [[ "$t" == none ]] || mine[$t]=1
      done
      for t in "${BASELINE[@]}"; do mine[$t]=1; done ;;
  esac
  rm -f "$files"
  # The block: from the "### Game tests" heading to the next heading. HTML comments (the template's examples) do not count.
  block="$(gh api "repos/$REPO/pulls/$PR" --jq '.body // ""' | tr -d '\r' \
    | awk '/^###[[:space:]]+Game tests[[:space:]]*$/ { on = 1; next } on && /^#/ { exit } on' \
    | awk '/<!--/ { c = 1 } !c { print } /-->/ { c = 0 }')"
  local none="" plus=0
  while IFS= read -r line; do
    line="$(echo "$line" | sed 's/^[[:space:]]*//; s/[[:space:]]*$//')"
    [[ -n "$line" ]] || continue
    case "$line" in
      "+ "*) name="$(test_name "${line#+ }")"; mine[$name]=1; plus=1 ;;
      "- "*) name="$(test_name "${line#- }")"; unset "mine[$name]" ;;
      all) for t in "${ALL_TESTS[@]}"; do mine[$t]=1; done ;;
      none:*) [[ -n "$(echo "${line#none:}" | tr -d '[:space:]')" ]] || fail "PR #$PR: 'none:' needs a reason after the colon"; none="${line#none:}" ;;
      none) fail "PR #$PR: 'none' needs a reason: write 'none: <why no game test can break>'" ;;
      *) fail "PR #$PR: the Game tests block has a line that is not '+ <test>', '- <test>', 'all' or 'none: <reason>': $line" ;;
    esac
  done <<<"$block"
  if [[ -n "$none" ]]; then
    (( plus == 0 )) || fail "PR #$PR: 'none' and '+' lines together; keep one of them"
    mine=()
    echo "== PR #$PR: none (${none# })"
  else
    echo "== PR #$PR: ${kind}; ${#mine[@]} tests: ${!mine[*]}"
  fi
  for t in "${!mine[@]}"; do SET[$t]=1; done
}

PRS=()
case "${1:-}" in
  --all) for t in "${ALL_TESTS[@]}"; do SET[$t]=1; done; echo "== every CI game test" ;;
  --group)
    base="${2:?--group needs a base and a head sha}" head="${3:?--group needs a base and a head sha}"
    # One merge commit per PR: "<title> (#N)" (the repo titles merges with the PR title), or "Merge pull request #N ..." (older).
    mapfile -t PRS < <(gh api "repos/$REPO/compare/$base...$head" --jq '.commits[].commit.message | split("\n")[0]' \
      | sed -n -e 's/^Merge pull request #\([0-9]*\) .*/\1/p' -e 's/.* (#\([0-9]*\))$/\1/p' | sort -un)
    (( ${#PRS[@]} > 0 )) || fail "no '<title> (#N)' or 'Merge pull request #N' commit between $base and $head"
    echo "== merge queue group: PR ${PRS[*]}" ;;
  "") fail "usage: ci-test-set.sh <pr number> ... | --group <base> <head> | --all" ;;
  *) PRS=("$@") ;;
esac
for pr in "${PRS[@]}"; do pr_set "$pr"; done

# In the order of ALL_TESTS, so that the shard split stays the same for the same set.
TESTS=() weight=0
for t in "${ALL_TESTS[@]}"; do
  [[ -n "${SET[$t]:-}" ]] || continue
  TESTS+=("$t")
  if [[ "$t" == mp:* ]]; then weight=$((weight + SESSION_WEIGHT)); else weight=$((weight + 1)); fi
done
shards=$(( (weight + SHARD_WEIGHT - 1) / SHARD_WEIGHT ))
(( shards <= MAX_SHARDS )) || shards=$MAX_SHARDS
echo "== ${#TESTS[@]} of ${#ALL_TESTS[@]} tests, $shards shard jobs"
echo "TESTS ${TESTS[*]}"
echo "SHARDS $shards"
if (( ${#TESTS[@]} == ${#ALL_TESTS[@]} )); then echo "FULL yes"; else echo "FULL no"; fi
