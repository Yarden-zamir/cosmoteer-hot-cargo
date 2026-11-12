#!/usr/bin/env bash
# Change tunables in a running Hot Cargo run on the PC (SLOT, default main = the player). Skill: hot-cargo-tune.
# Usage: scripts/tune.sh PATH=VALUE [PATH=VALUE ...]   for example Heat.HeatPerGameMinute=2 Hunters.Stages[2].ShipCount=4
#        scripts/tune.sh --reset PATH [PATH ...]       sets values back to their defaults
#        scripts/tune.sh --show                        prints tunables.rules (only the changed values)
#        scripts/tune.sh --defaults                    prints tunables.defaults.rules (every value, written at game start)
# PATH and VALUE use the format of the settings window: lists are comma separated ("1, 1.5, 2"), switches true/false,
# texts without quotes. All values of one call apply as one change, or none of them do: the mod validates them like a
# settings window edit, writes tunables.rules and, in co-op on the host, sends them to every machine (spec 853).
# The game must be in a run (a career game with the mod on). In co-op only the host's game takes changes.
set -euo pipefail
source "$(dirname "$0")/pc.sh"
[[ $# -ge 1 ]] || { sed -n 2,10p "$0"; exit 1; }

if [[ "$1" == --show || "$1" == --defaults ]]; then
  file=tunables.rules; [[ "$1" == --defaults ]] && file=tunables.defaults.rules
  pc "$PS_PATHS"'$f = Join-Path $modDir "'"$file"'"; if (Test-Path $f) { Get-Content $f -Raw } else { "(no '"$file"')" }' | tr -d '\r'
  exit 0
fi

REQUEST=""
if [[ "$1" == --reset ]]; then
  shift
  [[ $# -ge 1 ]] || { echo "tune: --reset needs at least one PATH" >&2; exit 1; }
  for path in "$@"; do REQUEST+="reset $path"$'\n'; done
else
  for item in "$@"; do
    path="${item%%=*}"
    value="${item#*=}"
    [[ "$item" == *=* && -n "$path" && "$path" != *" "* ]] || { echo "tune: expected PATH=VALUE, got '$item'" >&2; exit 1; }
    REQUEST+="set $path $value"$'\n'
  done
fi

# Writes tune.request in one move, so the mod never reads half a request, then waits for tune.result (the mod polls twice a second).
RESULT="$(pc "$PS_PATHS"'
$req = Join-Path $modDir "tune.request"; $res = Join-Path $modDir "tune.result"
Remove-Item $res -ErrorAction SilentlyContinue
[IO.File]::WriteAllText("$req.tmp", [Console]::In.ReadToEnd())
Move-Item "$req.tmp" $req -Force
for ($i = 0; $i -lt 40 -and -not (Test-Path $res); $i++) { Start-Sleep -Milliseconds 250 }
if (Test-Path $res) { Get-Content $res -Raw; Remove-Item $res }
else { Remove-Item $req -ErrorAction SilentlyContinue; "error no answer in 10 seconds: is the game of slot " + $slot + " in a Hot Cargo run, and not on a menu screen? Nothing changed." }
' <<<"$REQUEST" | tr -d '\r')"
echo "$RESULT"
[[ "$RESULT" == ok* ]]
