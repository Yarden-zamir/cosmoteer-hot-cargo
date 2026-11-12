#!/usr/bin/env bash
# Build the mod on this Mac and install it on the PC, in the slot that SLOT selects (see pc.sh).
# - Assembles mod/ (rules content) and copies the C# build into <slot Mods>/hot_cargo.
# - Keeps the slot's tunables file as it is (the player edits it in game); installs the repo copy only when none exists.
# - Installs the startup hook loader into the game Bin folder (only when it changed) and points
#   Cosmoteer.runtimeconfig.json at it (backup kept as .hot_cargo_orig). Writes Bin/steam_appid.txt.
# - Test slots get their own settings.rules that turns the mod on.
source "$(dirname "$0")/pc.sh"

dotnet build "$REPO_ROOT/src/HotCargo/HotCargo.csproj" -c Release -nologo -v quiet
dotnet build "$REPO_ROOT/src/HotCargo.Loader/HotCargo.Loader.csproj" -c Release -nologo -v quiet
BUILD="$REPO_ROOT/src/HotCargo/bin/Release/net10.0-windows"
LOADER="$REPO_ROOT/src/HotCargo.Loader/bin/Release/net10.0/HotCargo.Loader.dll"

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
"$REPO_ROOT/scripts/assemble-mod.sh" "$STAGE/hot_cargo"
# A dev build carries its git version (for example 0.4.0-3-g1a2b3c4), so the title screen shows which build runs.
# package.sh sets the release version the same way.
DEV_VERSION="$(git -C "$REPO_ROOT" describe --tags --always --dirty)"
sed -i '' "s/^Version = .*/Version = ${DEV_VERSION#v}/" "$STAGE/hot_cargo/mod.rules"
mkdir -p "$STAGE/hot_cargo/csharp" "$STAGE/loader"
cp "$BUILD/HotCargo.dll" "$BUILD/HotCargo.pdb" "$BUILD/HotCargo.xml" "$BUILD/0Harmony.dll" "$STAGE/hot_cargo/csharp/"
cp "$LOADER" "$REPO_ROOT/src/HotCargo.Loader/VERSION" "$STAGE/loader/"
# The tunables file is staged under a temporary name. The PC side decides if it overwrites the live copy.
if [[ -f "$STAGE/hot_cargo/tunables.rules" ]]; then mv "$STAGE/hot_cargo/tunables.rules" "$STAGE/hot_cargo/tunables.rules.deploy"; fi

COPYFILE_DISABLE=1 tar -cf - -C "$STAGE" hot_cargo loader | pc "$PS_PATHS"'
$ErrorActionPreference = "Stop"
if (Get-SlotProcess) { throw "Cosmoteer is running in slot $slot. Stop it before deploy (scripts/stop.sh)." }
New-Item -ItemType Directory -Force $mods | Out-Null
$tmpDir = Join-Path $env:TEMP ("hot_cargo_deploy_" + $slot); if (Test-Path $tmpDir) { Remove-Item $tmpDir -Recurse -Force }
New-Item -ItemType Directory $tmpDir | Out-Null
$tar = Join-Path $tmpDir "deploy.tar"
$in = [Console]::OpenStandardInput(); $out = [IO.File]::Create($tar); $in.CopyTo($out); $out.Close()
tar.exe -xf $tar -C $tmpDir
if ($LASTEXITCODE -ne 0) { throw "tar failed" }

# Mod folder: replace everything but the live tunables and their backups.
$keep = Join-Path $tmpDir "tunables_keep.rules"
if (Test-Path (Join-Path $modDir "tunables.rules")) { Copy-Item (Join-Path $modDir "tunables.rules") $keep -Force }
if (Test-Path $modDir) { Get-ChildItem $modDir | Where-Object { $_.Name -notlike "tunables.rules*" } | Remove-Item -Recurse -Force }
Copy-Item (Join-Path $tmpDir "hot_cargo") $mods -Recurse -Force
$new = Join-Path $modDir "tunables.rules.deploy"; $live = Join-Path $modDir "tunables.rules"
# The player edits tunables in the game (settings window) and by hand. Never overwrite them:
# in the player slot, install the repo copy only when it has no tunables file yet.
if (Test-Path $new) {
  # Test slots always start from the repo copy, so the settings of one test never leak into the next.
  if ($slot -eq "main" -and (Test-Path $keep)) { Copy-Item $keep $live -Force; Remove-Item $new -Force } else { Move-Item $new $live -Force }
}

# Startup hook loader. A running game locks it, and every worktree builds different bytes,
# so the loader is replaced only when src/HotCargo.Loader/VERSION changes. Bump VERSION when the loader code changes.
$loaderDir = Join-Path $game "Bin\HotCargoLoader"; New-Item -ItemType Directory -Force $loaderDir | Out-Null
$loaderDll = Join-Path $loaderDir "HotCargo.Loader.dll"; $newLoader = Join-Path $tmpDir "loader\HotCargo.Loader.dll"
$verFile = Join-Path $loaderDir "VERSION"; $newVer = (Get-Content (Join-Path $tmpDir "loader\VERSION") -Raw).Trim()
$oldVer = if (Test-Path $verFile) { (Get-Content $verFile -Raw).Trim() } else { "" }
if (-not (Test-Path $loaderDll) -or $oldVer -ne $newVer) {
  try { Copy-Item $newLoader $loaderDll -Force; [IO.File]::WriteAllText($verFile, $newVer) }
  catch {
    # Version 1 was installed before VERSION files existed. Same code, so keep it.
    if ($oldVer -eq "" -and $newVer -eq "1") { "loader in use; keeping the installed copy (same code)"; [IO.File]::WriteAllText($verFile, $newVer) }
    else { throw "HotCargo.Loader.dll version $newVer must replace $oldVer, but a running game uses it. Stop all Cosmoteer instances once, then deploy again." }
  }
}
[IO.File]::WriteAllText((Join-Path $loaderDir "hot_cargo_default.txt"), (Join-Path $mainMods "hot_cargo\csharp\HotCargo.dll"))
$rc = Join-Path $game "Bin\Cosmoteer.runtimeconfig.json"; $orig = "$rc.hot_cargo_orig"
if (-not (Test-Path $orig)) { Copy-Item $rc $orig }
$json = Get-Content $orig -Raw | ConvertFrom-Json
$json.runtimeOptions.configProperties | Add-Member -NotePropertyName STARTUP_HOOKS -NotePropertyValue $loaderDll -Force
[IO.File]::WriteAllText($rc, ($json | ConvertTo-Json -Depth 10))
# Without this file, Steamworks restarts the game through Steam when a script starts the exe directly.
[IO.File]::WriteAllText((Join-Path $game "Bin\steam_appid.txt"), "799600")

# Test slots: own settings with the mod on.
if ($slot -ne "main") {
  $entry = [char]34 + ($modDir -replace "\\", "/") + [char]34
  [IO.File]::WriteAllText((Join-Path $root "settings.rules"), "GameSettings`r`n{`r`n`tEnabledMods [$entry]`r`n`tAutoDisableMods = false`r`n`tShowNewsOnStartup = false`r`n`tShowTipsOnStartup = false`r`n`tIsFirstBoot = false`r`n}`r`n")
}
Remove-Item $tmpDir -Recurse -Force
"deployed to $modDir (slot $slot)"
'
