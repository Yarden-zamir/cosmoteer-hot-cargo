#!/usr/bin/env bash
# Build the player zip: hot-cargo-<version>.zip with the mod, the loader, install.bat, uninstall.bat and INSTALL.md.
# Usage: package.sh <version> <output folder>. Prints the zip path.
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${1:?usage: package.sh <version> <output folder>}"
DEST="${2:?usage: package.sh <version> <output folder>}"
dotnet build src/HotCargo/HotCargo.csproj -c Release -nologo -v quiet >&2
dotnet build src/HotCargo.Loader/HotCargo.Loader.csproj -c Release -nologo -v quiet >&2
BUILD=src/HotCargo/bin/Release/net10.0-windows
STAGE="$(mktemp -d)"; trap 'rm -rf "$STAGE"' EXIT
OUT="$STAGE/hot-cargo-$VERSION"
scripts/assemble-mod.sh "$OUT/hot_cargo"
# Portable in-place edit: BSD sed (Mac) and GNU sed (CI) take different -i arguments.
sed "s/^Version = .*/Version = $VERSION/" "$OUT/hot_cargo/mod.rules" > "$STAGE/mod.rules" && mv "$STAGE/mod.rules" "$OUT/hot_cargo/mod.rules"
mkdir -p "$OUT/hot_cargo/csharp" "$OUT/HotCargoLoader"
cp "$BUILD/HotCargo.dll" "$BUILD/HotCargo.pdb" "$BUILD/HotCargo.xml" "$BUILD/0Harmony.dll" "$OUT/hot_cargo/csharp/"
cp src/HotCargo.Loader/bin/Release/net10.0/HotCargo.Loader.dll src/HotCargo.Loader/VERSION "$OUT/HotCargoLoader/"
cp release/install.ps1 release/install-paths.ps1 release/uninstall.ps1 release/install.bat release/uninstall.bat release/INSTALL.md "$OUT/"
mkdir -p "$DEST"
ZIP="$(cd "$DEST" && pwd)/hot-cargo-$VERSION.zip"
rm -f "$ZIP"
(cd "$STAGE" && COPYFILE_DISABLE=1 zip -qr -X "$ZIP" "hot-cargo-$VERSION")
echo "$ZIP"
