# /// script
# requires-python = ">=3.12"
# ///
"""Co-op determinism lint for the lines that a change adds (CLAUDE.md, "Co-op determinism").

Usage: uv run tools/determinism-lint.py [base ref]   (default origin/main; checks committed and uncommitted changes)
Exit code 1 when an added line breaks a rule. A line may pass with the comment "// determinism: <reason>" on it or on the
line above (for example a sim value that only feeds a log line).

Rules for sim code (every src/HotCargo/**/*.cs file except UI files, Dev/ and Telemetry/):
- clock: DateTime.Now/UtcNow/Today, Stopwatch, Environment.TickCount, App.Clock (use sim time: sim.Tick, State.GameSeconds).
- random: System.Random, new Random(, Random.Shared, Guid.NewGuid (use sim.Rand or a FastRand seeded from sim data).
- local: LocalPlayerIndex/ID/Money/Ships, IsLocalPlayerShip (each machine has a different local player).
Rule for UI files (Ui/, Ux/, a name ending in Ui.cs, or a name with Panel, Window, Overlay, Gui, Hud, Dialog, Screen, Tooltip, Menu):
- ui-mutation: a direct sim change (AddHeat, ForceNextStage, AddMoney, Vaults.Add/Remove, an assignment to State.<field>).
  A UI change goes through an MPInput (NetManager.EnqueueInput).
Limit: grep level. It sees only the added line, not where the method runs; the co-op state hash (tests/autotest-mp) catches
what it misses. Revisit with a Roslyn analyzer if it lets real desyncs through or flags too much.
"""
import re
import subprocess
import sys

UI_PATH = re.compile(r"(^|/)(Ui|Ux)/|Ui\.cs$|Panel|Window|Overlay|Gui|Hud|Dialog|Screen|Tooltip|Menu")
EXEMPT = re.compile(r"^src/HotCargo/(Dev|Telemetry)/")
SIM_RULES = {
    "clock": re.compile(r"DateTime\.(Now|UtcNow|Today)|\bStopwatch\b|Environment\.TickCount|\bApp\.Clock\b"),
    "random": re.compile(r"\bSystem\.Random\b|\bnew Random\(|\bRandom\.Shared\b|Guid\.NewGuid"),
    "local": re.compile(r"\bLocalPlayer(Index|ID|Money|Ships)\b|\bIsLocalPlayerShip\b"),
}
UI_RULES = {
    "ui-mutation": re.compile(r"\.AddHeat\(|\.ForceNextStage\(|\.AddMoney\(|\bVaults\.(Add|Remove)\(|\bState\.\w+(\.\w+)*\s*([+\-*/]?=)(?!=)"),
}
MARK = "// determinism:"


def main() -> int:
    base = sys.argv[1] if len(sys.argv) > 1 else "origin/main"
    merge_base = subprocess.run(["git", "merge-base", base, "HEAD"], capture_output=True, text=True, check=True).stdout.strip()
    diff = subprocess.run(["git", "diff", "-U1", merge_base, "--", "src/HotCargo"], capture_output=True, text=True, check=True).stdout
    problems = []
    path, line_no, prev = None, 0, ""
    for raw in diff.splitlines():
        if raw.startswith("+++ "):
            path = raw[6:] if raw.startswith("+++ b/") else None
            continue
        if raw.startswith("@@"):
            line_no = int(re.match(r"@@ -\S+ \+(\d+)", raw).group(1)) - 1
            prev = ""
            continue
        if path is None or not path.endswith(".cs") or EXEMPT.search(path) or raw.startswith("-"):
            continue
        line_no += 1
        text = raw[1:]
        if raw.startswith("+") and MARK not in text and MARK not in prev:
            code = text.split("//")[0]
            rules = UI_RULES if UI_PATH.search(path) else SIM_RULES
            for rule, pattern in rules.items():
                if pattern.search(code):
                    problems.append(f"{path}:{line_no}: {rule}: {text.strip()}")
        prev = text
    for p in problems:
        print(p)
    if problems:
        print(f"== determinism lint: {len(problems)} added line(s) break a rule. Fix them, or add '{MARK} <reason>' (tools/determinism-lint.py).")
        return 1
    print("== determinism lint: ok")
    return 0


if __name__ == "__main__":
    sys.exit(main())
