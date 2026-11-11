# Hot Cargo installer for Cosmoteer (Windows).
# - Copies the hot_cargo mod folder into your Cosmoteer Mods folder (your tunables.rules is kept).
# - Copies HotCargo.Loader.dll into <Cosmoteer>\Bin\HotCargoLoader.
# - Adds a .NET startup hook to <Cosmoteer>\Bin\Cosmoteer.runtimeconfig.json (backup: .hot_cargo_orig).
# Run it again after a Cosmoteer update or a Steam "verify files", because those restore the original runtimeconfig.
param(
    [string]$GameDir = "",   # Cosmoteer install folder. Found through Steam when empty.
    [string]$ModsDir = ""    # Cosmoteer Mods folder. Found in Saved Games when empty.
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

. (Join-Path $here "install-paths.ps1")
$bin = Join-Path $GameDir "Bin"
if (-not (Test-Path (Join-Path $bin "Cosmoteer.runtimeconfig.json"))) { throw "Not a Cosmoteer folder: $GameDir" }
if (Get-Process Cosmoteer -ErrorAction SilentlyContinue) { throw "Close Cosmoteer first." }

# Mod folder. Keep the player's tunables.
$modDir = Join-Path $ModsDir "hot_cargo"
New-Item -ItemType Directory -Force $ModsDir | Out-Null
$keep = $null
if (Test-Path (Join-Path $modDir "tunables.rules")) { $keep = Get-Content (Join-Path $modDir "tunables.rules") -Raw }
if (Test-Path $modDir) { Remove-Item $modDir -Recurse -Force }
Copy-Item (Join-Path $here "hot_cargo") $ModsDir -Recurse
if ($keep -ne $null) { [IO.File]::WriteAllText((Join-Path $modDir "tunables.rules"), $keep) }

# Loader and startup hook.
$loaderDir = Join-Path $bin "HotCargoLoader"
New-Item -ItemType Directory -Force $loaderDir | Out-Null
Copy-Item (Join-Path $here "HotCargoLoader\*") $loaderDir -Force
[IO.File]::WriteAllText((Join-Path $loaderDir "hot_cargo_default.txt"), (Join-Path $modDir "csharp\HotCargo.dll"))
$rc = Join-Path $bin "Cosmoteer.runtimeconfig.json"
$orig = "$rc.hot_cargo_orig"
if (-not (Test-Path $orig)) { Copy-Item $rc $orig }
$json = Get-Content $orig -Raw | ConvertFrom-Json
$json.runtimeOptions.configProperties | Add-Member -NotePropertyName STARTUP_HOOKS -NotePropertyValue (Join-Path $loaderDir "HotCargo.Loader.dll") -Force
[IO.File]::WriteAllText($rc, ($json | ConvertTo-Json -Depth 10))

Write-Host ""
Write-Host "Hot Cargo is installed."
Write-Host "  Mod:  $modDir"
Write-Host "  Game: $GameDir"
Write-Host ""
Write-Host "Next: start Cosmoteer, open Mods, turn on 'Hot Cargo', restart the game when it asks, then start a new career."
