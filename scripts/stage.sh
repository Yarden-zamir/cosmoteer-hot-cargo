#!/usr/bin/env bash
# Build the mod on this Mac and stage it in a folder: hot_cargo/ (the mod folder) and loader/ (startup hook loader).
# Writes <dir>/hash: a hash of the mod folder and the loader VERSION. deploy.sh uploads a build to the PC only when the
# PC has no build with that hash. The loader DLL is not in the hash: the PC replaces it only when VERSION changes.
# Usage: stage.sh <dir>   (the folder is replaced)
set -euo pipefail
OUT="${1:?usage: stage.sh <dir>}"
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

# Parallel autotests from one worktree build into the same output folder. A per-worktree lock covers the build and the
# copy into the stage, so no stage gets a half-written DLL. A lock whose owner process is gone is stale.
BUILD_LOCK="/tmp/hot_cargo_build_$(printf %s "$REPO_ROOT" | shasum | cut -c1-12).lock"
trap 'if [[ "$(cat "$BUILD_LOCK/pid" 2>/dev/null)" == "$$" ]]; then rm -rf "$BUILD_LOCK"; fi' EXIT
until mkdir "$BUILD_LOCK" 2>/dev/null; do
  if [[ -f "$BUILD_LOCK/pid" ]] && ! kill -0 "$(cat "$BUILD_LOCK/pid")" 2>/dev/null; then rm -rf "$BUILD_LOCK"; continue; fi
  sleep 1
done
echo $$ > "$BUILD_LOCK/pid"

dotnet build "$REPO_ROOT/src/HotCargo/HotCargo.csproj" -c Release -nologo -v quiet
dotnet build "$REPO_ROOT/src/HotCargo.Loader/HotCargo.Loader.csproj" -c Release -nologo -v quiet
BUILD="$REPO_ROOT/src/HotCargo/bin/Release/net10.0-windows"
LOADER="$REPO_ROOT/src/HotCargo.Loader/bin/Release/net10.0/HotCargo.Loader.dll"
rm -rf "$OUT"
"$REPO_ROOT/scripts/assemble-mod.sh" "$OUT/hot_cargo"
# A dev build carries its git version (for example 0.4.0-3-g1a2b3c4), so the title screen shows which build runs.
# package.sh sets the release version the same way.
DEV_VERSION="$(git -C "$REPO_ROOT" describe --tags --always --dirty)"
sed -i '' "s/^Version = .*/Version = ${DEV_VERSION#v}/" "$OUT/hot_cargo/mod.rules"
mkdir -p "$OUT/hot_cargo/csharp" "$OUT/loader"
cp "$BUILD/HotCargo.dll" "$BUILD/HotCargo.pdb" "$BUILD/HotCargo.xml" "$BUILD/0Harmony.dll" "$OUT/hot_cargo/csharp/"
cp "$LOADER" "$REPO_ROOT/src/HotCargo.Loader/VERSION" "$OUT/loader/"
# The tunables file is staged under a temporary name. The PC side decides if it overwrites the live copy.
if [[ -f "$OUT/hot_cargo/tunables.rules" ]]; then mv "$OUT/hot_cargo/tunables.rules" "$OUT/hot_cargo/tunables.rules.deploy"; fi
rm -rf "$BUILD_LOCK"

# Hash of file names and contents. The name is part of each shasum line, so a renamed file changes the hash.
(cd "$OUT" && { find hot_cargo -type f -print0; printf 'loader/VERSION\0'; } | LC_ALL=C sort -z | xargs -0 shasum -a 256) \
  | shasum -a 256 | cut -c1-16 > "$OUT/hash"
