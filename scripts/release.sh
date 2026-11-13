#!/usr/bin/env bash
# Build a player release zip and publish it as a GitHub release.
# Usage: release.sh <version> [--prerelease] [--commit <sha>]
# Releases a tested commit, not whatever main is now, so releases need no merge freeze: by default the newest commit on
# origin/main with a pass marker (the full PC suite passed on it; a commit with only docs changes since such a marker counts
# too). --commit picks one; it must be on origin/main and tested. The zip is built in a scratch worktree at that commit.
# Set FORCE=1 to skip the test marker check (not for normal use).
set -euo pipefail
cd "$(dirname "$0")/.."
STATE_DIR="${HOT_CARGO_STATE:-$(cd "$(dirname "$0")/../.." && pwd)/.state}"; mkdir -p "$STATE_DIR"
VERSION="${1:?usage: release.sh <version> [--prerelease] [--commit <sha>]}"; shift
PRE="" WANT=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --prerelease) PRE=yes ;;
    --commit) WANT="${2:?--commit needs a sha}"; shift ;;
    *) echo "release: unknown argument $1"; exit 1 ;;
  esac
  shift
done
TAG="v$VERSION"
git fetch -q origin main
# True when the commit has a pass marker, or an ancestor with one and only docs (*.md, docs/) changed since.
tested() {
  local marker sha
  for marker in "$STATE_DIR"/autotest_pass_*; do
    [[ -f "$marker" ]] || continue
    sha="${marker##*_}"
    git merge-base --is-ancestor "$sha" "$1" 2>/dev/null || continue
    if [[ -z "$(git diff --name-only "$sha" "$1" | grep -v -E '\.md$|^docs/' || true)" ]]; then return 0; fi
  done
  return 1
}
if [[ -n "$WANT" ]]; then
  SHA="$(git rev-parse "$WANT^{commit}")"
  git merge-base --is-ancestor "$SHA" origin/main || { echo "release: $WANT is not on origin/main"; exit 1; }
  tested "$SHA" || [[ "${FORCE:-}" == 1 ]] || { echo "release: $WANT has no full PC suite pass (scripts/full-suite.sh)"; exit 1; }
else
  SHA=""
  for c in $(git rev-list --first-parent -200 origin/main); do if tested "$c"; then SHA="$c"; break; fi; done
  [[ -n "$SHA" ]] || { echo "release: no tested commit among the last 200 on origin/main; run the full PC suite first"; exit 1; }
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
{
  echo "🤖 Hot Cargo $VERSION for Cosmoteer 0.30.5a (Windows)."
  echo
  echo "Install: download the zip, unzip it, double-click \`install.bat\`, then turn on Hot Cargo in the game's Mods menu. Details are in INSTALL.md inside the zip."
  echo
  echo "## Changes"
  if [[ -n "$PREV" ]]; then git log --no-merges --format='- %s' "$PREV..$SHA"; else git log --no-merges --format='- %s' "$SHA" | head -40; fi
} > "$NOTES"

git tag -a "$TAG" -m "Hot Cargo $VERSION" "$SHA"
git push -q origin "$TAG"
gh release create "$TAG" "$ZIP" --title "Hot Cargo $VERSION" --notes-file "$NOTES" ${PRE:+--prerelease}
