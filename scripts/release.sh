#!/usr/bin/env bash
# Build a player release zip and publish it as a GitHub release.
# Usage: release.sh <version> [--commit <sha>]
# A release is a full release and becomes the latest one. Only the dev builds (dev-release.yml) are prereleases.
# Releases a tested commit, not whatever main is now, so releases need no merge freeze: by default the newest commit on
# origin/main whose required checks (branch protection of main, for example build, private-data and game-tests) and the
# full-suite check (every game test) all passed in CI. --commit picks one; it must be on origin/main and green. The zip is built in a scratch worktree there.
# Set FORCE=1 to skip the check (not for normal use).
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${1:?usage: release.sh <version> [--commit <sha>]}"; shift
WANT=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --commit) WANT="${2:?--commit needs a sha}"; shift ;;
    *) echo "release: unknown argument $1"; exit 1 ;;
  esac
  shift
done
TAG="v$VERSION"
git fetch -q origin main
# True when every required check of main has a successful check run on the commit.
REPO="$(gh repo view --json nameWithOwner -q .nameWithOwner)"
mapfile -t REQUIRED < <(gh api "repos/$REPO/branches/main/protection/required_status_checks" -q '.contexts[]')
[[ ${#REQUIRED[@]} -gt 0 ]] || { echo "release: main has no required checks to go by"; exit 1; }
# A release also needs every game test: the merge queue runs only the tests of its pull requests, so the commit must have
# a green full-suite check (a manual run, gh workflow run check.yml --ref main, or the scheduled run of check.yml).
REQUIRED+=("full-suite")
tested() {
  local passed c
  passed="$(gh api "repos/$REPO/commits/$1/check-runs?per_page=100" -q '.check_runs[] | select(.conclusion == "success") | .name')"
  for c in "${REQUIRED[@]}"; do grep -qx "$c" <<<"$passed" || return 1; done
}
if [[ -n "$WANT" ]]; then
  SHA="$(git rev-parse "$WANT^{commit}")"
  git merge-base --is-ancestor "$SHA" origin/main || { echo "release: $WANT is not on origin/main"; exit 1; }
  tested "$SHA" || [[ "${FORCE:-}" == 1 ]] || { echo "release: $WANT does not pass every required check (${REQUIRED[*]})"; exit 1; }
else
  SHA=""
  for c in $(git rev-list --first-parent -50 origin/main); do if tested "$c"; then SHA="$c"; break; fi; done
  [[ -n "$SHA" ]] || { echo "release: no commit among the last 50 on origin/main passes every required check (${REQUIRED[*]})."
    echo "release: for a full suite on the newest main commit, run: gh workflow run check.yml --ref main"; exit 1; }
fi
echo "release: $TAG from ${SHA:0:9} ($(git log -1 --format=%s "$SHA"))"
git rev-parse -q --verify "refs/tags/$TAG" >/dev/null && { echo "release: tag $TAG exists"; exit 1; }

# Build in a scratch worktree at the released commit, next to the others, so ../.game resolves as in every worktree.
STAGE="$(mktemp -d)" WT="$(cd .. && pwd)/release-$TAG"
[[ -e "$WT" ]] && { echo "release: $WT exists; remove it first (git worktree remove $WT)"; exit 1; }
git worktree add -q --detach "$WT" "$SHA"
trap 'rm -rf "$STAGE"; git worktree remove --force "$WT" 2>/dev/null || true' EXIT
(cd "$WT" && scripts/check-private.sh)
ZIP="$(cd "$WT" && scripts/package.sh "$VERSION" "$STAGE")"

# Dev prereleases (CI, one for each push) are not part of the changelog of a tested release.
PREV="$(git describe --tags --abbrev=0 --exclude '*-dev.*' "$SHA" 2>/dev/null || true)"
NOTES="$STAGE/notes.md"
GAME_VERSION="$(scripts/game-version.sh "$WT")"
{
  echo "🤖 Hot Cargo $VERSION for Cosmoteer $GAME_VERSION (Windows)."
  echo
  echo "Install: download the zip, unzip it, double-click \`install.bat\`, then turn on Hot Cargo in the game's Mods menu. Details are in INSTALL.md inside the zip."
  echo
  echo "## Changes"
  if [[ -n "$PREV" ]]; then git log --no-merges --format='- %s' "$PREV..$SHA"; else git log --no-merges --format='- %s' "$SHA" | head -40; fi
} > "$NOTES"

git tag -a "$TAG" -m "Hot Cargo $VERSION" "$SHA"
git push -q origin "$TAG"
gh release create "$TAG" "$ZIP" --title "Hot Cargo $VERSION" --notes-file "$NOTES" --latest
