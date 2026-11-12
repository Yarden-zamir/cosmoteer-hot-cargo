#!/usr/bin/env bash
# Build the mod on this Mac and install it on the PC, in the slot that SLOT selects (see pc.sh).
# - Stages the build with stage.sh, or uses the stage in HOT_CARGO_STAGE (autotest-all.sh stages once per suite).
# - The PC keeps one unpacked copy of each build in %USERPROFILE%\HotCargoSlots\_builds\<hash> (the 30 newest).
#   A build that the PC has already is not uploaded again; the slot gets a copy of the cached build.
# - Replaces the slot's mod folder with the build. Keeps telemetry and, in slot main, the player's tunables file.
# - Installs the startup hook loader into the game Bin folder (only when it changed) and points
#   Cosmoteer.runtimeconfig.json at it (backup kept as .hot_cargo_orig). Writes Bin/steam_appid.txt.
# - Test slots get their own settings.rules that turns the mod on.
# Autotest mode (AUTOTEST_SCRIPT=<file>, used by autotest-lib.sh start_slot): the same PC call also stops the test slot's
# game first, installs the file as autotest.txt and starts the game. It never stops the main slot: it fails if that game runs.
source "$(dirname "$0")/pc.sh"

STAGE="${HOT_CARGO_STAGE:-}"
if [[ -z "$STAGE" ]]; then
  STAGE="$(mktemp -d)"
  trap 'rm -rf "$STAGE"' EXIT
  "$REPO_ROOT/scripts/stage.sh" "$STAGE"
fi
HASH="$(cat "$STAGE/hash")"
[[ -n "$HASH" ]] || { echo "deploy: empty build hash in $STAGE" >&2; exit 1; }
AUTOTEST=""
if [[ -n "${AUTOTEST_SCRIPT:-}" ]]; then AUTOTEST="$(base64 < "$AUTOTEST_SCRIPT" | tr -d '\n')"; fi

PS_DEPLOY='
$ErrorActionPreference = "Stop"
if ($autotest -ne "" -and $slot -ne "main") {
  $running = Get-SlotProcess
  if ($running) { $running | Stop-Process -Force; $running | Wait-Process -Timeout 20 -ErrorAction SilentlyContinue }
  if (Test-Path $pidFile) { Remove-Item $pidFile -Force }
}
if (Get-SlotProcess) { throw "Cosmoteer is running in slot $slot. Stop it before deploy (scripts/stop.sh)." }

# Build cache. A complete build folder has the file "ok"; it is moved into place in one step.
$builds = Join-Path $env:USERPROFILE "HotCargoSlots\_builds"; $cache = Join-Path $builds $hash
$cacheState = "cached build " + $hash
if (-not (Test-Path (Join-Path $cache "ok"))) {
  New-Item -ItemType Directory -Force $builds | Out-Null
  $tmpDir = Join-Path $builds ("tmp-" + $slot + "-" + $PID); if (Test-Path $tmpDir) { Remove-Item $tmpDir -Recurse -Force }
  New-Item -ItemType Directory $tmpDir | Out-Null
  $tar = Join-Path $tmpDir "deploy.tar"
  $in = [Console]::OpenStandardInput(); $out = [IO.File]::Create($tar); $in.CopyTo($out); $out.Close()
  if ((Get-Item $tar).Length -eq 0) { Remove-Item $tmpDir -Recurse -Force; "NEED_UPLOAD"; return }
  tar.exe -xf $tar -C $tmpDir
  if ($LASTEXITCODE -ne 0) { throw "tar failed" }
  Remove-Item $tar
  New-Item -ItemType File (Join-Path $tmpDir "ok") | Out-Null
  if (Test-Path $cache) { if (Test-Path (Join-Path $cache "ok")) { Remove-Item $tmpDir -Recurse -Force } else { Remove-Item $cache -Recurse -Force } }
  # Another slot can upload the same build at the same time. Then its copy wins and this one is deleted.
  if (Test-Path $tmpDir) { try { Move-Item $tmpDir $cache } catch { Remove-Item $tmpDir -Recurse -Force } }
  if (-not (Test-Path (Join-Path $cache "ok"))) { throw "build $hash is not in the cache after the upload" }
  $cacheState = "uploaded build " + $hash
  Get-ChildItem $builds -Directory | Where-Object { $_.Name -notlike "tmp-*" } | Sort-Object LastWriteTime -Descending |
    Select-Object -Skip 30 | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}
(Get-Item $cache).LastWriteTime = Get-Date
New-Item -ItemType Directory -Force $mods | Out-Null

# Mod folder: replace everything but the live tunables, their backups and the telemetry files (TELEMETRY.md).
$keep = Join-Path $env:TEMP ("hot_cargo_tunables_keep_" + $slot + ".rules"); if (Test-Path $keep) { Remove-Item $keep -Force }
if (Test-Path (Join-Path $modDir "tunables.rules")) { Copy-Item (Join-Path $modDir "tunables.rules") $keep -Force }
if (Test-Path $modDir) { Get-ChildItem $modDir | Where-Object { $_.Name -notlike "tunables.rules*" -and $_.Name -ne "telemetry" } | Remove-Item -Recurse -Force }
Copy-Item (Join-Path $cache "hot_cargo") $mods -Recurse -Force
$new = Join-Path $modDir "tunables.rules.deploy"; $live = Join-Path $modDir "tunables.rules"
# The player edits tunables in the game (settings window) and by hand. Never overwrite them:
# in the player slot, install the repo copy only when it has no tunables file yet.
if (Test-Path $new) {
  # Test slots always start from the repo copy, so the settings of one test never leak into the next.
  if ($slot -eq "main" -and (Test-Path $keep)) { Copy-Item $keep $live -Force; Remove-Item $new -Force } else { Move-Item $new $live -Force }
}
if (Test-Path $keep) { Remove-Item $keep -Force }

# Startup hook loader. A running game locks it, and every worktree builds different bytes,
# so the loader is replaced only when src/HotCargo.Loader/VERSION changes. Bump VERSION when the loader code changes.
$loaderDir = Join-Path $game "Bin\HotCargoLoader"; New-Item -ItemType Directory -Force $loaderDir | Out-Null
$loaderDll = Join-Path $loaderDir "HotCargo.Loader.dll"; $newLoader = Join-Path $cache "loader\HotCargo.Loader.dll"
$verFile = Join-Path $loaderDir "VERSION"; $newVer = (Get-Content (Join-Path $cache "loader\VERSION") -Raw).Trim()
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
$jsonText = $json | ConvertTo-Json -Depth 10
# Parallel deploys write the same content. Skip the write when it is already there, so no two writers collide.
if (-not (Test-Path $rc) -or (Get-Content $rc -Raw) -ne $jsonText) { [IO.File]::WriteAllText($rc, $jsonText) }
# Without this file, Steamworks restarts the game through Steam when a script starts the exe directly.
[IO.File]::WriteAllText((Join-Path $game "Bin\steam_appid.txt"), "799600")

# Test slots: own settings with the mod on.
if ($slot -ne "main") {
  $entry = [char]34 + ($modDir -replace "\\", "/") + [char]34
  [IO.File]::WriteAllText((Join-Path $root "settings.rules"), "GameSettings`r`n{`r`n`tEnabledMods [$entry]`r`n`tAutoDisableMods = false`r`n`tShowNewsOnStartup = false`r`n`tShowTipsOnStartup = false`r`n`tIsFirstBoot = false`r`n}`r`n")
}
"deployed to $modDir (slot $slot, $cacheState)"
if ($autotest -ne "") {
  [IO.File]::WriteAllText((Join-Path $modDir "autotest.txt"), [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($autotest)))
  "autotest script installed"
'"$PS_START"'
}
'

# First call without the build. The PC answers NEED_UPLOAD when it does not have this build yet; then upload it.
deploy_call() { pc "$PS_PATHS"'$hash = "'"$HASH"'"; $autotest = "'"$AUTOTEST"'";'"$PS_DEPLOY"; }
OUT="$(deploy_call </dev/null)"
if [[ "$OUT" == *NEED_UPLOAD* ]]; then
  OUT="$(COPYFILE_DISABLE=1 tar -cf - -C "$STAGE" hot_cargo loader | deploy_call)"
fi
[[ "$OUT" == *"deployed to"* ]] || { echo "$OUT"; echo "deploy: the PC did not confirm the deploy" >&2; exit 1; }
echo "$OUT"
