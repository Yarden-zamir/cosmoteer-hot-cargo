#!/usr/bin/env bash
# Build the mod on this Mac and install it on the PC.
# - Copies mod/ (rules content) and the C# build into <Cosmoteer Mods>/hot_cargo.
# - Keeps a backup of the PC tunables file when it differs from the repo copy.
# - Adds the .NET startup hook to Cosmoteer.runtimeconfig.json (backup kept as .hot_cargo_orig).
source "$(dirname "$0")/pc.sh"

dotnet build "$REPO_ROOT/src/HotCargo/HotCargo.csproj" -c Release -nologo -v quiet
BUILD="$REPO_ROOT/src/HotCargo/bin/Release/net10.0-windows"

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$STAGE/hot_cargo/csharp"
cp -R "$REPO_ROOT/mod/." "$STAGE/hot_cargo/"
cp "$BUILD/HotCargo.dll" "$BUILD/HotCargo.pdb" "$BUILD/0Harmony.dll" "$STAGE/hot_cargo/csharp/"
# The tunables file is staged under a temporary name. The PC side decides if it overwrites the live copy.
if [[ -f "$STAGE/hot_cargo/tunables.rules" ]]; then mv "$STAGE/hot_cargo/tunables.rules" "$STAGE/hot_cargo/tunables.rules.deploy"; fi

COPYFILE_DISABLE=1 tar -cf - -C "$STAGE" hot_cargo | pc "$PS_PATHS"'
$ErrorActionPreference = "Stop"
if (Get-Process Cosmoteer -ErrorAction SilentlyContinue) { throw "Cosmoteer is running. Close it before deploy." }
$tmp = Join-Path $env:TEMP "hot_cargo_deploy.tar"
$in = [Console]::OpenStandardInput(); $out = [IO.File]::Create($tmp); $in.CopyTo($out); $out.Close()
$keep = Join-Path $env:TEMP "hot_cargo_tunables_keep.rules"
if (Test-Path (Join-Path $modDir "tunables.rules")) { Copy-Item (Join-Path $modDir "tunables.rules") $keep -Force }
if (Test-Path (Join-Path $modDir "csharp")) { Remove-Item (Join-Path $modDir "csharp") -Recurse -Force }
tar.exe -xf $tmp -C $mods
if ($LASTEXITCODE -ne 0) { throw "tar failed" }
$new = Join-Path $modDir "tunables.rules.deploy"
$live = Join-Path $modDir "tunables.rules"
if (Test-Path $new) {
  if ((Test-Path $keep) -and ((Get-FileHash $keep).Hash -ne (Get-FileHash $new).Hash)) {
    $bak = Join-Path $modDir ("tunables.rules.bak-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
    Copy-Item $keep $bak; "tunables on PC differed from repo; backup: $bak"
  }
  Move-Item $new $live -Force
}
$rc = Join-Path $game "Bin\Cosmoteer.runtimeconfig.json"
$orig = "$rc.hot_cargo_orig"
if (-not (Test-Path $orig)) { Copy-Item $rc $orig }
$hook = Join-Path $modDir "csharp\HotCargo.dll"
$json = Get-Content $orig -Raw | ConvertFrom-Json
$json.runtimeOptions.configProperties | Add-Member -NotePropertyName STARTUP_HOOKS -NotePropertyValue $hook -Force
[IO.File]::WriteAllText($rc, ($json | ConvertTo-Json -Depth 10))
# Without this file, Steamworks restarts the game through Steam when scripts/run.sh starts the exe directly.
[IO.File]::WriteAllText((Join-Path $game "Bin\steam_appid.txt"), "799600")
"deployed to $modDir"
'
