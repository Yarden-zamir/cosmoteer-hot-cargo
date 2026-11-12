#!/usr/bin/env bash
# Merge one or more feature branches into main and ship them together, stopping at the first failure:
# merge each -> build -> unit tests -> private-data check -> one autotest suite -> push -> install into the player's game.
# Usage: integrate.sh [--detach] [--no-autotest] [--full] <branch | PR number>...
#        integrate.sh [--detach] [--no-autotest] [--full] --continue   after you resolved merge conflicts or fixed a
#        failure with a new commit (merges the rest of the batch first)
# --detach      run in the background, safe from a killed shell; wait with scripts/suite-wait.sh <log> (see detach.sh).
# --no-autotest skip the autotests.  --full  run the full suite, not only the tests that the batch affects.
# Run it in the main worktree. Nothing is pushed unless every step passed. On a failure the merge commits stay local:
# fix them with a new commit and run "integrate.sh --continue", or undo them with "git reset --hard origin/main".
# When autotests fail, it names the branches of the batch whose areas own each failed test (autotest-affected.sh rule).
set -euo pipefail
cd "$(dirname "$0")/.."
ARGS=() AUTOTEST=yes FULL=no CONTINUE=no DETACH=no
for a in "$@"; do
  case "$a" in
    --no-autotest) AUTOTEST=no ;;
    --full) FULL=yes ;;
    --continue) CONTINUE=yes ;;
    --detach) DETACH=yes ;;
    -*) echo "integrate: unknown option $a"; exit 1 ;;
    *) ARGS+=("$a") ;;
  esac
done
if [[ "$DETACH" == yes ]]; then
  rest=(); for a in "$@"; do [[ "$a" != --detach ]] && rest+=("$a"); done
  LOG="$(scripts/detach.sh integrate scripts/integrate.sh "${rest[@]}")"
  echo "== integrate runs detached. Log: $LOG"
  echo "== wait for it: scripts/suite-wait.sh $LOG"
  exit 0
fi
[[ "$(git branch --show-current)" == main ]] || { echo "integrate: run it on main"; exit 1; }
[[ "$CONTINUE" == yes || ${#ARGS[@]} -gt 0 ]] || { echo "usage: integrate.sh [--detach] [--no-autotest] [--full] <branch|PR>... | --continue"; exit 1; }
[[ "$CONTINUE" == no || ${#ARGS[@]} -eq 0 ]] || { echo "integrate: --continue takes no branches"; exit 1; }
STATE_DIR="${HOT_CARGO_STATE:-$(cd .. && pwd)/.state}"; mkdir -p "$STATE_DIR"
# The refs of the batch, and the refs that are not merged yet (both one per line).
BATCH_FILE="$STATE_DIR/integrate-batch" PENDING_FILE="$STATE_DIR/integrate-pending"
step() { echo; echo "== integrate: $*"; }

if [[ "$CONTINUE" == yes ]]; then
  if git rev-parse -q --verify MERGE_HEAD >/dev/null; then
    [[ -z "$(git diff --name-only --diff-filter=U)" ]] || { echo "integrate: unresolved conflicts:"; git diff --name-only --diff-filter=U; exit 1; }
    git commit -q --no-edit
  fi
else
  [[ -z "$(git status --porcelain --untracked-files=no)" ]] || { echo "integrate: main has uncommitted changes"; exit 1; }
  step "update main"
  git pull -q --ff-only origin main
  : > "$BATCH_FILE"
  for a in "${ARGS[@]}"; do
    # A PR number merges the PR's branch from origin. Merging it into main marks the PR as merged on GitHub.
    if [[ "$a" != *[!0-9]* ]]; then
      head="$(gh pr view "$a" --json headRefName --jq .headRefName)"
      [[ -n "$head" ]] || { echo "integrate: PR $a has no head branch"; exit 1; }
      git fetch -q origin "$head"
      a="origin/$head"
    fi
    git rev-parse -q --verify "$a^{commit}" >/dev/null || { echo "integrate: unknown branch $a"; exit 1; }
    echo "$a" >> "$BATCH_FILE"
  done
  cp "$BATCH_FILE" "$PENDING_FILE"
fi
BATCH=()
[[ -f "$BATCH_FILE" ]] && while IFS= read -r b; do [[ -n "$b" ]] && BATCH+=("$b"); done < "$BATCH_FILE"

if [[ -s "$PENDING_FILE" ]]; then
  PENDING=(); while IFS= read -r b; do [[ -n "$b" ]] && PENDING+=("$b"); done < "$PENDING_FILE"
  for i in "${!PENDING[@]}"; do
    b="${PENDING[$i]}"
    step "merge $b"
    if ! git merge --no-ff "$b" -m "Merge branch '$b'"; then
      printf '%s\n' "${PENDING[@]:$((i + 1))}" > "$PENDING_FILE"
      echo "integrate: conflicts. Resolve them, git add the files, then run: scripts/integrate.sh --continue"
      git diff --name-only --diff-filter=U
      exit 1
    fi
  done
  rm -f "$PENDING_FILE"
fi
# Spec section numbers must stay unique (parallel branches often pick the same next number).
DUP="$(ls spec | cut -d- -f1 | sort | uniq -d || true)"
[[ -z "$DUP" ]] || { echo "integrate: two spec files share the section number $DUP"; exit 1; }
DUPREQ="$(cat spec/*.md | grep -oE '^[0-9]+\. ' | sort | uniq -d | tr -d '. ' | tr '\n' ' ' || true)"
[[ -z "$DUPREQ" ]] || { echo "integrate: duplicate requirement numbers: $DUPREQ"; exit 1; }

step "build"
dotnet build src/HotCargo/HotCargo.csproj -c Release -nologo -v quiet > /tmp/hot_cargo_integrate_build.log 2>&1 \
  || { grep -E " error " /tmp/hot_cargo_integrate_build.log | sort -u | head -20; echo "integrate: build failed"; exit 1; }
step "unit tests"
(cd tests/HotCargo.Tests && dotnet run) > /tmp/hot_cargo_integrate_unit.log 2>&1 \
  || { tail -30 /tmp/hot_cargo_integrate_unit.log; echo "integrate: unit tests failed"; exit 1; }
grep -a "Total:" /tmp/hot_cargo_integrate_unit.log | tail -1
step "private-data check"
scripts/check-private.sh

# Prints, for each failed test, the branches of the batch whose changes select that test.
blame() {
  local failed="$1" t b list owners
  declare -A lists
  for b in "${BATCH[@]}"; do lists["$b"]="$(LIST_ONLY=1 HEAD_REF="$b" scripts/autotest-affected.sh origin/main 2>/dev/null | tail -1)"; done
  for t in $failed; do
    owners=""
    for b in "${BATCH[@]}"; do
      list="${lists[$b]}"
      if [[ "$list" == *"full suite"* ]]; then owners+=" $b(shared code)"
      elif [[ " ${list#== affected: } " == *" $t "* ]]; then owners+=" $b"; fi
    done
    echo "integrate: $t failed; batch branches that touch it:${owners:- none (core test or flaky)}"
  done
}
if [[ "$AUTOTEST" == yes ]]; then
  TEST_OUT="$STATE_DIR/integrate-autotest.out" rc=0
  if [[ "$FULL" == yes ]]; then
    step "full autotest suite"
    scripts/autotest-all.sh 400 2>&1 | tee "$TEST_OUT" || rc=$?
  else
    step "affected autotests"
    COMMITTED_ONLY=1 scripts/autotest-affected.sh origin/main 2>&1 | tee "$TEST_OUT" || rc=$?
  fi
  if [[ $rc -ne 0 ]]; then
    FAILED="$(grep '^== failed: ' "$TEST_OUT" | tail -1 || true)"
    FAILED="${FAILED#== failed: }"; FAILED="${FAILED% (*}"
    if [[ ${#BATCH[@]} -gt 1 && -n "$FAILED" ]]; then blame "$FAILED"; fi
    echo "integrate: autotests failed; nothing pushed"
    exit 1
  fi
fi
step "push"
git push -q origin main
git log --oneline -1
rm -f "$BATCH_FILE"
step "install into the player's game"
scripts/install-player.sh || echo "integrate: install failed (pushed anyway)"
