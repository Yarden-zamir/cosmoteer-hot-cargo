#!/usr/bin/env bash
# Coverage of one Mac sim run, for the test impact map (tools/test-impact.py). Source it; scripts/autotest-mac.sh uses it
# when HOT_CARGO_COVERAGE_DIR is set.
# Each run gets its own collector session (dotnet-coverage, pinned in dotnet-tools.json) and its own copy of the stage with
# a statically instrumented HotCargo.dll. Dynamic instrumentation does not start in the sim host ("Profiler was not
# initialized"), and instrumenting needs the game assemblies next to the dll (../.game/Bin).
COVERAGE_SETTINGS="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/coverage.runsettings.xml"

# coverage_begin <stage> <run name>: starts a session and prints "<session id> <instrumented stage>".
coverage_begin() {
  local stage="$1" name="$2" sid dir ins f
  sid="hc-cov-$$-$RANDOM"
  dir="$(mktemp -d)"
  cp -R "$stage" "$dir/stage"
  ins="$dir/ins"; mkdir "$ins"
  for f in "$REPO_ROOT"/../.game/Bin/*.dll; do ln -s "$f" "$ins/"; done
  cp "$dir/stage/build/hot_cargo/csharp/"{HotCargo.dll,HotCargo.pdb,0Harmony.dll} "$ins/"
  dotnet dotnet-coverage collect --session-id "$sid" --server-mode --background -f cobertura -o "$dir/final.xml" -s "$COVERAGE_SETTINGS" --nologo > "$dir/server.txt" 2>&1
  dotnet dotnet-coverage instrument --session-id "$sid" -s "$COVERAGE_SETTINGS" --nologo "$ins/HotCargo.dll" > "$dir/instrument.txt" 2>&1 \
    || { echo "coverage: instrument failed for $name: $(tail -1 "$dir/instrument.txt")" >&2; return 1; }
  cp "$ins/HotCargo.dll" "$ins/HotCargo.pdb" "$dir/stage/build/hot_cargo/csharp/"
  echo "$sid $dir/stage"
}

# coverage_end <session id> <instrumented stage> <out xml>: writes the run's coverage and stops the session.
coverage_end() {
  dotnet dotnet-coverage snapshot --nologo -o "$3" "$1" > /dev/null 2>&1 || echo "coverage: no snapshot for $3" >&2
  dotnet dotnet-coverage shutdown --nologo "$1" > /dev/null 2>&1 || true
  rm -rf "$(dirname "$2")"
}
