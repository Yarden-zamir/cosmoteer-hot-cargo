#!/usr/bin/env bash
# Build a player release zip and publish it as a GitHub release.
# Usage: release.sh <version> [--prerelease]
# Needs: a clean tree, HEAD pushed to origin/main, and the full PC suite passed on HEAD (pass marker from
# scripts/full-suite.sh, which scripts/nightly.sh runs).
# Set FORCE=1 to skip the test marker check (not for normal use).
set -euo pipefail
cd "$(dirname "$0")/.."
STATE_DIR="${HOT_CARGO_STATE:-$(cd "$(dirname "$0")/../.." && pwd)/.state}"; mkdir -p "$STATE_DIR"
VERSION="${1:?usage: release.sh <version> [--prerelease]}"
PRE="${2:-}"
TAG="v$VERSION"
SHA="$(git rev-parse HEAD)"

[[ -z "$(git status --porcelain)" ]] || { echo "release: tree is not clean"; exit 1; }
git fetch -q origin main
[[ "$SHA" == "$(git rev-parse origin/main)" ]] || { echo "release: HEAD is not origin/main; push first"; exit 1; }
# A pass marker on HEAD, or on an ancestor when only docs (*.md, docs/) changed since then.
tested() {
  local marker sha
  for marker in "$STATE_DIR"/autotest_pass_*; do
    [[ -f "$marker" ]] || continue
    sha="${marker##*_}"
    git merge-base --is-ancestor "$sha" HEAD 2>/dev/null || continue
    if [[ -z "$(git diff --name-only "$sha" HEAD | grep -v -E '\.md$|^docs/' || true)" ]]; then return 0; fi
  done
  return 1
}
tested || [[ "${FORCE:-}" == 1 ]] || { echo "release: run the full PC suite on this commit first (scripts/full-suite.sh or scripts/nightly.sh)"; exit 1; }
git rev-parse -q --verify "refs/tags/$TAG" >/dev/null && { echo "release: tag $TAG exists"; exit 1; }
scripts/check-private.sh

STAGE="$(mktemp -d)"; trap 'rm -rf "$STAGE"' EXIT
ZIP="$(scripts/package.sh "$VERSION" "$STAGE")"

# Dev prereleases (CI, one for each push) are not part of the changelog of a tested release.
PREV="$(git describe --tags --abbrev=0 --exclude '*-dev.*' 2>/dev/null || true)"
NOTES="$STAGE/notes.md"
{
  echo "🤖 Hot Cargo $VERSION for Cosmoteer 0.30.5a (Windows)."
  echo
  echo "Install: download the zip, unzip it, double-click \`install.bat\`, then turn on Hot Cargo in the game's Mods menu. Details are in INSTALL.md inside the zip."
  echo
  echo "## Changes"
  if [[ -n "$PREV" ]]; then git log --no-merges --format='- %s' "$PREV..HEAD"; else git log --no-merges --format='- %s' HEAD | head -40; fi
} > "$NOTES"

git tag -a "$TAG" -m "Hot Cargo $VERSION"
git push -q origin "$TAG"
gh release create "$TAG" "$ZIP" --title "Hot Cargo $VERSION" --notes-file "$NOTES" ${PRE:+--prerelease}
