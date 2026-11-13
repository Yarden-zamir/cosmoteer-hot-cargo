# /// script
# requires-python = ">=3.12"
# ///
"""Audit of the game tests from their history: cost, failures and catches.

Usage: uv run tools/test-audit.py [--md]   (run from the repo root; reads ../.state/logs, ../.state/flaky.log and git log)

For each game test (tests/autotest/*.txt, co-op sessions as mp:<session>):
- runs and median cost in seconds: PC = deploy + start + script + teardown (slot wait excluded), Mac = total.
- failures: "game" (CHECK FAILED, AUTOTEST FAIL or an ERROR line with AUTOTEST DONE), "infra" (no AUTOTEST DONE: link, deploy,
  timeout, crash). A FLAKY entry in flaky.log is a failure that passed on retry.
- catches: "fix" commits on origin/main whose message names the test and that change game code (src/HotCargo or mod/, not
  Dev/), except the commit that added the test. "test fixes": commits that name it and change only tests or scripts.
Limit: the logs cover only the days since .state/logs exists, and a catch needs the test name in the commit message.
"""
import re
import statistics
import subprocess
import sys
from collections import defaultdict
from pathlib import Path

ROOT = Path.cwd()
LOGS = ROOT.parent / ".state" / "logs"
TESTS = sorted(p.stem for p in (ROOT / "tests" / "autotest").glob("*.txt"))
SESSIONS = sorted(f"mp:{p.stem}" for p in (ROOT / "tests" / "autotest-mp").glob("*.txt") if p.stem != "client")
ALL = TESTS + SESSIONS

cost = defaultdict(list)
cost_by = defaultdict(lambda: {"pc": [], "mac": []})
runs = defaultdict(lambda: {"pc": 0, "mac": 0})
fails = defaultdict(lambda: {"game": 0, "infra": 0})


def log_name(path: Path) -> str | None:
    stem = path.name.split(".")[0]
    if stem.startswith("mp-"):
        return f"mp:{stem[3:]}"
    return stem if stem in TESTS else None


for log in LOGS.glob("*/*.log"):
    name = log_name(log)
    if name not in ALL:
        continue
    text = log.read_text(errors="ignore")
    mac = log.name.endswith(".mac.log")
    if name.startswith("mp:"):
        m = re.search(r"== timing mp-\S+(?: \(mac\))? .*?total=(\d+)", text)
        w = re.search(r"wait=(\d+)", m.group(0)) if m else None
        if m:
            c = int(m.group(1)) - (int(w.group(1)) if w else 0)
            cost[name].append(c)
            cost_by[name]["mac" if mac else "pc"].append(c)
        result = re.search(r"== result: (PASS|FAIL.*)", text)
        if not result:
            continue
        runs[name]["mac" if mac else "pc"] += 1
        r = result.group(1)
        if r != "PASS":
            fails[name]["infra" if ("join failed" in r or "PC link" in r or "no host log" in r) else "game"] += 1
        continue
    if mac:
        m = re.search(r"== timing \S+ total=(\d+)", text)
    else:
        m = re.search(r"== timing \S+ wait=\d+ deploy=(\d+) start=(\d+) script=(\d+) teardown=(\d+)", text)
    if m:
        c = sum(int(x) for x in m.groups())
        cost[name].append(c)
        cost_by[name]["mac" if mac else "pc"].append(c)
    runs[name]["mac" if mac else "pc"] += 1
    done = "AUTOTEST DONE" in text
    game = "CHECK FAILED" in text or "AUTOTEST FAIL" in text or (done and " ERROR " in text)
    if game:
        fails[name]["game"] += 1
    elif not done:
        fails[name]["infra"] += 1

flaky = defaultdict(int)
flaky_log = ROOT.parent / ".state" / "flaky.log"
if flaky_log.exists():
    for line in flaky_log.read_text().splitlines():
        parts = line.split()
        if len(parts) >= 3:
            n = parts[2]
            n = f"mp:{n[3:]}" if n.startswith("mp-") else n
            flaky[n] += 1

# Commits that name a test: game-code changes vs test-only changes.
git = subprocess.run(["git", "log", "origin/main", "--no-merges", "--format=@@%h %s%n%b", "--name-only"],
                     capture_output=True, text=True, check=True).stdout
# The commit that added each test file: its game code is the feature, not a catch.
added = defaultdict(set)
for name in ALL:
    path = f"tests/autotest-mp/{name[3:]}.txt" if name.startswith("mp:") else f"tests/autotest/{name}.txt"
    out = subprocess.run(["git", "log", "origin/main", "--diff-filter=A", "--format=%h", "--", path], capture_output=True, text=True).stdout.split()
    added[name].update(out)
catches = defaultdict(set)
testfix = defaultdict(set)
for block in git.split("@@")[1:]:
    head, _, rest = block.partition("\n")
    sha = head.split()[0]
    lines = rest.splitlines()
    files = [l for l in lines if l.startswith(("src/", "mod/", "tests/", "scripts/", "tools/"))]
    msg = head + "\n" + "\n".join(l for l in lines if l not in files)
    game_code = any((f.startswith("src/HotCargo/") and "/Dev/" not in f and "Autotest" not in f) or f.startswith("mod/") for f in files)
    is_fix = head.split(" ", 1)[1].startswith("fix")
    for name in ALL:
        bare = name[3:] if name.startswith("mp:") else name
        if sha in added.get(name, ()):
            continue
        if re.search(rf"(?<![\w-]){re.escape(bare)}(?![\w-])", msg) or f"tests/autotest/{bare}.txt" in files:
            if game_code and is_fix:
                catches[name].add(sha)
            elif not game_code:
                testfix[name].add(sha)


def med(xs):
    return int(statistics.median(xs)) if xs else 0


rows = []
for name in ALL:
    rows.append({
        "test": name, "runs": runs[name]["pc"] + runs[name]["mac"], "pc": runs[name]["pc"], "mac": runs[name]["mac"],
        "cost": med(cost[name]), "cpc": med(cost_by[name]["pc"]), "cmac": med(cost_by[name]["mac"]), "game": fails[name]["game"], "infra": fails[name]["infra"], "flaky": flaky[name],
        "catches": len(catches[name]), "testfix": len(testfix[name]),
    })
rows.sort(key=lambda r: -max(r["cpc"], r["cmac"]))
if "--md" in sys.argv:
    print("| Test | Runs PC/Mac | Median s PC/Mac | Game fails | Infra fails | Flaky | Catches | Test-only commits |")
    print("|---|---|---|---|---|---|---|---|")
    for r in rows:
        print(f"| {r['test']} | {r['pc']}/{r['mac']} | {r['cpc']}/{r['cmac']} | {r['game']} | {r['infra']} | {r['flaky']} | {r['catches']} | {r['testfix']} |")
else:
    print(f"{'test':28} {'pc/mac':>9} {'s_pc':>5} {'s_mac':>5} {'game':>5} {'infra':>5} {'flaky':>5} {'catch':>5} {'tfix':>5}")
    for r in rows:
        print(f"{r['test']:28} {r['pc']:>4}/{r['mac']:<4} {r['cpc']:>5} {r['cmac']:>5} {r['game']:>5} {r['infra']:>5} {r['flaky']:>5} {r['catches']:>5} {r['testfix']:>5}")
    print(f"== {len(rows)} game tests; median seconds summed: PC {sum(r['cpc'] for r in rows)}, Mac {sum(r['cmac'] for r in rows)}")
