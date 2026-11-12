#!/usr/bin/env bash
# Change tunables while the game runs: edits <slot Mods>/hot_cargo/tunables.rules on the PC (SLOT, default main = the player).
# Usage: scripts/tune.sh Section.Field=value [Section.Field=value ...]
#        scripts/tune.sh --show                 prints the file
#        scripts/tune.sh --reset Section.Field  removes a value, so the default applies again
# In single player the mod reloads the file on the next frame (spec 50 to 53). TUNABLES.md says when each value takes
# effect (now, next spawn, next system, new runs). Bad values are rejected and the old ones stay; the overlay shows why.
# Limit: plain Section.Field values only (no arrays or nested sections). Use the in-game settings window for those.
# Co-op: the mod ignores file changes during a co-op run (spec 60); the host changes values in the settings window.
set -euo pipefail
source "$(dirname "$0")/pc.sh"
[[ $# -ge 1 ]] || { sed -n 2,9p "$0"; exit 1; }

read_file() { pc "$PS_PATHS"'$f = Join-Path $modDir "tunables.rules"; if (Test-Path $f) { Get-Content $f -Raw }'; }
if [[ "$1" == --show ]]; then read_file; exit 0; fi

CURRENT="$(read_file | tr -d '\r')"
NEW="$(CURRENT="$CURRENT" uv run - "$@" <<'EOF'
# Sets or removes Section.Field lines in an ObjectText file of "Section { Field = value }" blocks.
import os, re, sys
text = os.environ["CURRENT"]
args = sys.argv[1:]
reset = args[:1] == ["--reset"]
items = args[1:] if reset else args
for item in items:
    path, _, value = item.partition("=")
    section, _, field = path.strip().partition(".")
    if not section or not field or "." in field or "[" in field or (not reset and not value):
        sys.exit(f"tune: expected Section.Field=value, got {item!r}")
    block = re.search(r"(?ms)^" + re.escape(section) + r"\s*\{(.*?)^\}", text)
    line = f"\t{field} = {value.strip()}\n"
    if block:
        body = block.group(1)
        field_re = re.compile(r"(?m)^\s*" + re.escape(field) + r"\s*=.*\n?")
        body = field_re.sub("", body) if reset else (field_re.sub(line, body, count=1) if field_re.search(body) else body.rstrip("\n") + "\n" + line)
        text = text[:block.start(1)] + body + text[block.end(1):]
    elif not reset:
        text = text.rstrip("\n") + f"\n{section}\n{{\n{line}}}\n"
print(text, end="")
EOF
)"
pc "$PS_PATHS"'$f = Join-Path $modDir "tunables.rules"; [IO.File]::WriteAllText($f, [Console]::In.ReadToEnd()); "tunables.rules written (slot " + $slot + ")"' <<<"$NEW"
