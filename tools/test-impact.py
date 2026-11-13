# /// script
# requires-python = ">=3.12"
# ///
"""Test impact map: which game test runs which Hot Cargo methods, and which tests a change needs.

  uv run tools/test-impact.py build <coverage dir> <map.json> <sha>
      Reads <test>.xml files (Cobertura, from scripts/autotest-mac.sh with HOT_CARGO_COVERAGE_DIR) and writes the map.
  uv run tools/test-impact.py select <map.json> [head ref]
      Prints the tests that run a changed method, one per line, then lines "FALLBACK <path>: <reason>" for changes that the
      map cannot attribute. The caller (scripts/autotest-affected.sh) adds tests for those with its folder rule.

Selection: the diff runs from the map's commit to head (committed and uncommitted changes), so its old-side line numbers
match the map. A changed line belongs to the smallest method whose lines contain it (a lambda is its own method); a pure
insertion counts for the method around it. A C# change outside every method (a field, an attribute, a new method between
two others, a new file) and a method that no test ran are FALLBACK. Only src/HotCargo/**/*.cs is mapped; the caller
handles every other path (rules, data, scripts).
Limit: the map is as old as its last refresh (scripts/coverage-map.sh, nightly). A test that started to run a method since
then is missed until the next refresh; the nightly PC full suite still runs it.
"""
import json
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path


def rel(path: str) -> str | None:
    i = path.replace("\\", "/").find("src/HotCargo/")
    return path.replace("\\", "/")[i:] if i >= 0 else None


def build(cov_dir: Path, out: Path, sha: str) -> None:
    methods: dict[tuple, int] = {}
    table: list[dict] = []
    tests: dict[str, list[int]] = {}
    for xml in sorted(cov_dir.glob("*.xml")):
        hit: set[int] = set()
        for cls in ET.parse(xml).iter("class"):
            f = rel(cls.get("filename", ""))
            if f is None:
                continue
            for m in cls.iter("method"):
                lines = [(int(l.get("number")), int(l.get("hits", "0"))) for l in m.iter("line")]
                if not lines:
                    continue
                key = (f, cls.get("name"), m.get("name"), m.get("signature"), min(n for n, _ in lines))
                if key not in methods:
                    methods[key] = len(table)
                    table.append({"file": f, "name": f"{cls.get('name')}.{m.get('name')}",
                                  "lines": [min(n for n, _ in lines), max(n for n, _ in lines)]})
                if any(h > 0 for _, h in lines):
                    hit.add(methods[key])
        if hit:
            tests[xml.stem] = sorted(hit)
    out.write_text(json.dumps({"sha": sha, "methods": table, "tests": tests}))
    print(f"map: {len(tests)} tests, {len(table)} methods, {len({m['file'] for m in table})} files, at {sha[:9]}")


def changed_lines(base: str, head: str | None, path: str) -> list[tuple[int, int]]:
    """Old-side line ranges of the changes to one file. A pure insertion after line n is (n, n)."""
    args = ["git", "diff", "-U0", base] + ([head] if head else []) + ["--", path]
    out = subprocess.run(args, capture_output=True, text=True, check=True).stdout
    ranges = []
    for m in re.finditer(r"^@@ -(\d+)(?:,(\d+))? ", out, re.M):
        start, count = int(m.group(1)), int(m.group(2) or "1")
        ranges.append((start, start + count - 1) if count > 0 else (start, start))
    return ranges


def select(map_path: Path, head: str | None) -> None:
    data = json.loads(map_path.read_text())
    base = data["sha"]
    by_file: dict[str, list[int]] = defaultdict(list)
    for i, m in enumerate(data["methods"]):
        by_file[m["file"]].append(i)
    tests_of: dict[int, set[str]] = defaultdict(set)
    for t, ids in data["tests"].items():
        for i in ids:
            tests_of[i].add(t)
    args = ["git", "diff", "--name-only", base] + ([head] if head else [])
    files = subprocess.run(args, capture_output=True, text=True, check=True).stdout.split()
    if not head:
        files += subprocess.run(["git", "ls-files", "--others", "--exclude-standard"], capture_output=True, text=True).stdout.split()
    selected: set[str] = set()
    fallbacks: list[str] = []
    for f in sorted(set(files)):
        if not (f.startswith("src/HotCargo/") and f.endswith(".cs")):
            continue
        if f not in by_file:
            fallbacks.append(f"{f}: no test runs this file, or it is new")
            continue
        for lo, hi in changed_lines(base, head, f):
            hits: set[int] = set()
            for line in range(lo, hi + 1):
                inner = [i for i in by_file[f] if data["methods"][i]["lines"][0] <= line <= data["methods"][i]["lines"][1]]
                if inner:
                    hits.add(min(inner, key=lambda i: data["methods"][i]["lines"][1] - data["methods"][i]["lines"][0]))
            if not hits:
                fallbacks.append(f"{f}:{lo}: the change is outside every method")
                continue
            runners = set().union(*(tests_of[i] for i in hits))
            if not runners:
                fallbacks.append(f"{f}:{lo}: no test runs {', '.join(sorted(data['methods'][i]['name'] for i in hits))}")
            selected |= runners
    for t in sorted(selected):
        print(t)
    for fb in fallbacks:
        print(f"FALLBACK {fb}")


if __name__ == "__main__":
    if len(sys.argv) >= 5 and sys.argv[1] == "build":
        build(Path(sys.argv[2]), Path(sys.argv[3]), sys.argv[4])
    elif len(sys.argv) >= 3 and sys.argv[1] == "select":
        select(Path(sys.argv[2]), sys.argv[3] if len(sys.argv) > 3 else None)
    else:
        sys.exit(__doc__)
