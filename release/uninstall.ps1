# Removes Hot Cargo: restores Cosmoteer.runtimeconfig.json, removes the loader and the hot_cargo mod folder.
# Your tunables.rules is deleted with the mod folder; copy it first if you want to keep it.
param([string]$GameDir = "", [string]$ModsDir = "")
$ErrorActionPreference = "Stop"
. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "install-paths.ps1")
if (Get-Process Cosmoteer -ErrorAction SilentlyContinue) { throw "Close Cosmoteer first." }
$bin = Join-Path $GameDir "Bin"
$rc = Join-Path $bin "Cosmoteer.runtimeconfig.json"
if (Test-Path "$rc.hot_cargo_orig") { Copy-Item "$rc.hot_cargo_orig" $rc -Force; Remove-Item "$rc.hot_cargo_orig" }
if (Test-Path (Join-Path $bin "HotCargoLoader")) { Remove-Item (Join-Path $bin "HotCargoLoader") -Recurse -Force }
if (Test-Path (Join-Path $ModsDir "hot_cargo")) { Remove-Item (Join-Path $ModsDir "hot_cargo") -Recurse -Force }
Write-Host "Hot Cargo is removed. Cosmoteer is back to normal."
