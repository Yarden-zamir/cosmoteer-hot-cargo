"""hc-telemetry: summarize Hot Cargo telemetry runs into report.md and report.html."""

from __future__ import annotations

import argparse
import os
import statistics
from pathlib import Path

from . import analyze as a
from .load import load
from .report import bar_chart, html_report, markdown

# tools/telemetry/src/hc_telemetry/cli.py -> the repo worktree is 4 folders up; telemetry lives next to the worktrees.
DEFAULT_DIR = Path(os.environ.get("HOT_CARGO_TELEMETRY_DIR", Path(__file__).resolve().parents[4] / ".." / ".telemetry")).resolve()


def conclusions(reports: list[a.RunReport]) -> list[str]:
    """Findings that need more than one run, or that are worth reading first."""
    out = []
    pooled = a.pooled_heat_sources(reports)
    if pooled.rows:
        top = ", ".join(f"{r[0]} {r[2]}" for r in pooled.rows[:4] if r[2] != "-")
        out.append(f"Heat gains by source over {len(reports)} runs: {top}.")
    pct = [e["tele_pct_wall"] for r in reports for e in r.run.events if e["e"] == "perf"]
    if pct:
        out.append(f"Telemetry overhead over {len(pct)} perf windows: median {statistics.median(pct):.3f} %, "
                   f"95th percentile {sorted(pct)[int(0.95 * (len(pct) - 1))]:.3f} %, max {max(pct):.3f} % of real time.")
    results = [a.result_of(r.run)[0] for r in reports]
    out.append(f"Results: {results.count('won')} won, {results.count('lost')} lost, {results.count('running')} not finished.")
    return out


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--dir", type=Path, default=DEFAULT_DIR, help=f"telemetry folder (default {DEFAULT_DIR})")
    p.add_argument("--out", type=Path, help="output folder (default <dir>/report)")
    p.add_argument("--run", action="append", default=[], help="only this run ID (repeatable)")
    p.add_argument("--latest", type=int, help="only the newest N runs")
    p.add_argument("--no-autotest", action="store_true", help="leave out runs that an autotest made")
    p.add_argument("--since", help="only runs whose first session started at or after this UTC time (ISO, for example 2026-11-12T09:00)")
    p.add_argument("--min-game-seconds", type=float, default=0, help="leave out runs shorter than this")
    args = p.parse_args()

    runs = load(args.dir)
    if args.run:
        runs = [r for r in runs if r.id in args.run]
    if args.no_autotest:
        runs = [r for r in runs if not r.header.get("autotest")]
    if args.since:
        runs = [r for r in runs if r.header.get("wall_utc", "") >= args.since]
    runs = [r for r in runs if max(e["gs"] for e in r.events) >= args.min_game_seconds]
    if args.latest:
        runs = runs[-args.latest :]
    assert runs, f"no telemetry runs in {args.dir} (pull them with scripts/pull-telemetry.sh)"

    reports = [a.analyze(r) for r in runs]
    overview = [a.overview(reports), a.pooled_heat_sources(reports)]
    found = conclusions(reports)
    out = args.out or args.dir / "report"
    out.mkdir(parents=True, exist_ok=True)
    (out / "report.md").write_text(markdown(reports, overview, found), encoding="utf-8")
    pooled = {row[0]: float(row[1].replace(",", "")) for row in overview[1].rows}
    charts = [c for c in [bar_chart("Heat by source, all runs", pooled)] if c]
    (out / "report.html").write_text(html_report(reports, overview, found, charts), encoding="utf-8")
    print(f"{len(reports)} run(s) -> {out / 'report.md'} and {out / 'report.html'}")
