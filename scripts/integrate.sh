#!/usr/bin/env bash
# Merge a feature branch into main and ship it, stopping at the first failure:
# merge -> build -> unit tests -> private-data check -> affected autotests -> push -> install into the player's game.
# Usage: integrate.sh <branch> [--no-autotest]
#        integrate.sh --continue [--no-autotest]   after you resolved merge conflicts (staged them, no commit needed)
# Run it in the main worktree. Nothing is pushed unless every step passed. On a failure the merge commit stays local:
# fix it with a new commit and run "integrate.sh --continue", or undo it with "git reset --hard origin/main".
set -euo pipefail
cd "$(dirname "$0")/.."
[[ "$(git branch --show-current)" == main ]] || { echo "integrate: run it on main"; exit 1; }
ARG="${1:?usage: integrate.sh <branch>|--continue [--no-autotest]}"
AUTOTEST=yes
[[ "${2:-}" == --no-autotest ]] && AUTOTEST=no
step() { echo; echo "== integrate: $*"; }

if [[ "$ARG" == --continue ]]; then
  if git rev-parse -q --verify MERGE_HEAD >/dev/null; then
    [[ -z "$(git diff --name-only --diff-filter=U)" ]] || { echo "integrate: unresolved conflicts:"; git diff --name-only --diff-filter=U; exit 1; }
    git commit -q --no-edit
  fi
else
  [[ -z "$(git status --porcelain --untracked-files=no)" ]] || { echo "integrate: main has uncommitted changes"; exit 1; }
  step "update main"
  git pull -q --ff-only origin main
  step "merge $ARG"
  if ! git merge --no-ff "$ARG" -m "Merge branch '$ARG'"; then
    echo "integrate: conflicts. Resolve them, git add the files, then run: scripts/integrate.sh --continue"
    git diff --name-only --diff-filter=U
    exit 1
  fi
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
if [[ "$AUTOTEST" == yes ]]; then
  step "affected autotests"
  COMMITTED_ONLY=1 scripts/autotest-affected.sh origin/main || { echo "integrate: autotests failed; nothing pushed"; exit 1; }
fi
step "push"
git push -q origin main
git log --oneline -1
step "install into the player's game"
scripts/install-player.sh || echo "integrate: install failed (pushed anyway)"
