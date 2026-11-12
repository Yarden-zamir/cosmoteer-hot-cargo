#!/usr/bin/env bash
# Build the mod on this Mac and install it on the PC, in the slot that SLOT selects (see pc.sh).
# - Stages the build with stage.sh, or uses the stage in HOT_CARGO_STAGE (autotest-all.sh stages once per suite).
# - The PC keeps one unpacked copy of each build in %USERPROFILE%\HotCargoSlots\_builds\<hash> (the 30 newest).
#   A build that the PC has already is not uploaded again; the slot gets a copy of the cached build.
# - scripts/slot-install.ps1 (shipped in the build) does the install: it replaces the slot's mod folder, keeps telemetry
#   and, in slot main, the player's tunables file, installs the startup hook loader and the test slot settings.
# Autotest mode (AUTOTEST_SCRIPT=<file>, used by autotest-lib.sh start_slot): the same PC call also stops the test slot's
# game first, installs the file as autotest.txt and starts the game. It never stops the main slot: it fails if that game runs.
source "$(dirname "$0")/pc.sh"

STAGE="${HOT_CARGO_STAGE:-}"
OWN_STAGE=""
if [[ -z "$STAGE" ]]; then
  STAGE="$(mktemp -d)"; OWN_STAGE="$STAGE"
  "$REPO_ROOT/scripts/stage.sh" "$STAGE" || { rm -rf "$STAGE"; exit 1; }
fi
HASH="$(cat "$STAGE/hash")"
[[ -n "$HASH" ]] || { echo "deploy: empty build hash in $STAGE" >&2; exit 1; }
SEND_DIR="$(mktemp -d)"
trap 'rm -rf "$SEND_DIR"; if [[ -n "$OWN_STAGE" ]]; then rm -rf "$OWN_STAGE"; fi' EXIT
# stdin of the PC call: a tar with autotest.txt (autotest mode only) and, on the second call, the build.
if [[ -n "${AUTOTEST_SCRIPT:-}" ]]; then cp "$AUTOTEST_SCRIPT" "$SEND_DIR/autotest.txt"; else touch "$SEND_DIR/none"; fi
send() { COPYFILE_DISABLE=1 tar -cf - -C "$SEND_DIR" . "$@"; }

# The PC side: unpack stdin, move a new build into the cache (a complete build folder has the file "ok" and moves into
# place in one step), then run the build's slot-install.ps1. Kept short: the T3 link allows 8192 characters.
PS_BOOT='$ErrorActionPreference = "Stop"
$builds = Join-Path $env:USERPROFILE "HotCargoSlots\_builds"; $cache = Join-Path $builds $hash; $cacheState = "cached build " + $hash
$tmpDir = Join-Path $builds ("tmp-" + $slot + "-" + $PID); if (Test-Path $tmpDir) { Remove-Item $tmpDir -Recurse -Force }
New-Item -ItemType Directory -Force $tmpDir | Out-Null
$tar = Join-Path $tmpDir "in.tar"; $in = [Console]::OpenStandardInput(); $out = [IO.File]::Create($tar); $in.CopyTo($out); $out.Close()
tar.exe -xf $tar -C $tmpDir; if ($LASTEXITCODE -ne 0) { throw "tar failed" }
$autotestFile = Join-Path $tmpDir "autotest.txt"; if (-not (Test-Path $autotestFile)) { $autotestFile = $null }
$new = Join-Path $tmpDir "build"
if (-not (Test-Path (Join-Path $cache "ok"))) {
  if (-not (Test-Path $new)) { Remove-Item $tmpDir -Recurse -Force; "NEED_UPLOAD"; return }
  New-Item -ItemType File (Join-Path $new "ok") | Out-Null
  if ((Test-Path $cache) -and -not (Test-Path (Join-Path $cache "ok"))) { Remove-Item $cache -Recurse -Force }
  # Another slot can upload the same build at the same time. Then its copy wins.
  try { Move-Item $new $cache } catch { }
  if (-not (Test-Path (Join-Path $cache "ok"))) { throw "build $hash is not in the cache after the upload" }
  $cacheState = "uploaded build " + $hash
  Get-ChildItem $builds -Directory | Where-Object { $_.Name -notlike "tmp-*" } | Sort-Object LastWriteTime -Descending | Select-Object -Skip 30 | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}
(Get-Item $cache).LastWriteTime = Get-Date
$startGame = {'"$PS_START"'}
try { . ([scriptblock]::Create((Get-Content (Join-Path $cache "slot-install.ps1") -Raw))) } finally { Remove-Item $tmpDir -Recurse -Force -ErrorAction SilentlyContinue }
'

# First call without the build. The PC answers NEED_UPLOAD when it does not have this build yet; then upload it.
deploy_call() { pc "$PS_PATHS"'$hash = "'"$HASH"'";'"$PS_BOOT"; }
OUT="$(send | deploy_call)"
if [[ "$OUT" == *NEED_UPLOAD* ]]; then OUT="$(send -C "$STAGE" build | deploy_call)"; fi
[[ "$OUT" == *"deployed to"* ]] || { echo "$OUT"; echo "deploy: the PC did not confirm the deploy" >&2; exit 1; }
echo "$OUT"
