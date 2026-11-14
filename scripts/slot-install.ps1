# Installs a cached build into a slot. scripts/deploy.sh runs it on the PC: it ships inside the build
# (%USERPROFILE%\HotCargoSlots\_builds\<hash>\slot-install.ps1), so the command over the T3 link stays short (8192 limit).
# deploy.sh sets before it: the PS_PATHS variables and Get-SlotProcess (scripts/pc.sh), $cache (the build folder),
# $cacheState (text for the output), $autotestFile (path of the autotest script, or $null), $startGame (PS_START) and
# $minFreeGB (free memory that a test slot needs before its game starts).
$ErrorActionPreference = "Stop"

# Writes a text file that every slot shares (the game install folder). Parallel deploys write the same text, and a running
# game can hold the file open. So it skips the write when the text is already there, and an IO error with the same text in
# the file counts as success. Other IO errors retry for up to 10 s.
function Write-SharedText([string]$path, [string]$text) {
  for ($try = 1; ; $try++) {
    $current = $null
    # .NET errors arrive wrapped (MethodInvocationException), so the checks look at the base exception.
    if (Test-Path $path) { try { $current = [IO.File]::ReadAllText($path) } catch { if ($_.Exception.GetBaseException() -isnot [IO.IOException]) { throw } } }
    if ($current -ceq $text) { return }
    try { [IO.File]::WriteAllText($path, $text); return }
    catch {
      if ($_.Exception.GetBaseException() -isnot [IO.IOException]) { throw }
      if ($try -ge 10) { throw "cannot write the shared file ${path}: $($_.Exception.GetBaseException().Message)" }
      Start-Sleep -Seconds 1
    }
  }
}
if ($autotestFile -and $slot -ne "main") {
  # Get-SlotProcess finds the slot's games by their command line (see pc.sh), also after a killed run.
  $running = @(Get-SlotProcess | Where-Object { $_ })
  if ($running.Count -gt 0) { $running | Stop-Process -Force; $running | Wait-Process -Timeout 20 -ErrorAction SilentlyContinue }
  if (Test-Path $pidFile) { Remove-Item $pidFile -Force }
}
if (Get-SlotProcess) { throw "Cosmoteer is running in slot $slot. Stop it before deploy (scripts/stop.sh)." }

New-Item -ItemType Directory -Force $mods | Out-Null

# Mod folder: replace everything but the live tunables, their backups and the telemetry files (TELEMETRY.md).
$keep = Join-Path $env:TEMP ("hot_cargo_tunables_keep_" + $slot + ".rules"); if (Test-Path $keep) { Remove-Item $keep -Force }
if (Test-Path (Join-Path $modDir "tunables.rules")) { Copy-Item (Join-Path $modDir "tunables.rules") $keep -Force }
# A game that just exited can hold its files for a moment, so the removal tries again for up to 10 s.
for ($try = 1; Test-Path $modDir; $try++) {
  try { Get-ChildItem $modDir | Where-Object { $_.Name -notlike "tunables.rules*" -and $_.Name -ne "telemetry" } | Remove-Item -Recurse -Force; break }
  catch { if ($try -ge 10) { throw }; Start-Sleep -Seconds 1 }
}
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
  try { Copy-Item $newLoader $loaderDll -Force; Write-SharedText $verFile $newVer }
  catch {
    # Version 1 was installed before VERSION files existed. Same code, so keep it.
    if ($oldVer -eq "" -and $newVer -eq "1") { "loader in use; keeping the installed copy (same code)"; Write-SharedText $verFile $newVer }
    else { throw "HotCargo.Loader.dll version $newVer must replace $oldVer, but a running game uses it. Stop all Cosmoteer instances once, then deploy again." }
  }
}
Write-SharedText (Join-Path $loaderDir "hot_cargo_default.txt") (Join-Path $mainMods "hot_cargo\csharp\HotCargo.dll")
$rc = Join-Path $game "Bin\Cosmoteer.runtimeconfig.json"; $orig = "$rc.hot_cargo_orig"
# A game update replaces the runtimeconfig, and the new file has no hook. Then the new file is the original, so the hook
# never brings back the settings of an older game version.
$liveHook = (Get-Content $rc -Raw | ConvertFrom-Json).runtimeOptions.configProperties.STARTUP_HOOKS
if (-not (Test-Path $orig) -or -not $liveHook) { Copy-Item $rc $orig -Force }
$json = Get-Content $orig -Raw | ConvertFrom-Json
$json.runtimeOptions.configProperties | Add-Member -NotePropertyName STARTUP_HOOKS -NotePropertyValue $loaderDll -Force
$jsonText = $json | ConvertTo-Json -Depth 10
Write-SharedText $rc $jsonText
# Without this file, Steamworks restarts the game through Steam when a script starts the exe directly.
Write-SharedText (Join-Path $game "Bin\steam_appid.txt") "799600"

# Test slots: own settings with the mod on.
if ($slot -ne "main") {
  $entry = [char]34 + ($modDir -replace "\\", "/") + [char]34
  [IO.File]::WriteAllText((Join-Path $root "settings.rules"), "GameSettings`r`n{`r`n`tEnabledMods [$entry]`r`n`tAutoDisableMods = false`r`n`tShowNewsOnStartup = false`r`n`tShowTipsOnStartup = false`r`n`tIsFirstBoot = false`r`n}`r`n")
}
"deployed to $modDir (slot $slot, $cacheState)"
if ($autotestFile) {
  Copy-Item $autotestFile (Join-Path $modDir "autotest.txt") -Force
  "autotest script installed"
  # A headless game needs about 2.5 GB. Too many games at once make the PC page, and tests turn flaky.
  # Test slots wait up to 120 s for $minFreeGB of free memory (HOT_CARGO_MIN_FREE_GB, 0 turns the check off).
  if ($slot -ne "main" -and $minFreeGB -gt 0) {
    $deadline = (Get-Date).AddSeconds(120)
    while (($free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB) -lt $minFreeGB) {
      if ((Get-Date) -gt $deadline) { throw ("not enough free memory to start slot {0}: {1:N1} GB free, {2} GB needed" -f $slot, $free, $minFreeGB) }
      Start-Sleep -Seconds 5
    }
  }
  . $startGame
}
