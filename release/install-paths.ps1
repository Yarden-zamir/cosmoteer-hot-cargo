# Shared by install.ps1 and uninstall.ps1. Resolves $GameDir and $ModsDir when the caller left them empty.
function Find-GameDir {
    $steam = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { throw "Steam not found. Run install.ps1 -GameDir <Cosmoteer folder>." }
    $libraries = @($steam)
    $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
        foreach ($line in Get-Content $vdf) {
            $parts = $line.Trim().Split('"')
            if ($parts.Length -ge 4 -and $parts[1] -eq "path") { $libraries += $parts[3].Replace("\\", "\") }
        }
    }
    foreach ($lib in $libraries) {
        $dir = Join-Path $lib "steamapps\common\Cosmoteer"
        if (Test-Path (Join-Path $dir "Bin\Cosmoteer.exe")) { return $dir }
    }
    throw "Cosmoteer not found in the Steam libraries. Run install.ps1 -GameDir <Cosmoteer folder>."
}

function Find-ModsDir {
    $root = Join-Path $env:USERPROFILE "Saved Games\Cosmoteer"
    $saveFolder = Get-ChildItem $root -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName "settings.rules") } |
        Sort-Object { (Get-Item (Join-Path $_.FullName "settings.rules")).LastWriteTime } -Descending |
        Select-Object -First 1
    if (-not $saveFolder) { throw "No Cosmoteer save folder found in $root. Start Cosmoteer once, or run install.ps1 -ModsDir <Mods folder>." }
    return (Join-Path $saveFolder.FullName "Mods")
}

if (-not $GameDir) { $GameDir = Find-GameDir }
if (-not $ModsDir) { $ModsDir = Find-ModsDir }
