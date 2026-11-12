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
    perf = [[e["tele_pct_wall"] for e in s.events if e["e"] == "perf"] for r in reports for s in r.run.sessions]
    pct = [p for windows in perf for p in windows]
    # The first window of a session holds the one-time cost: the header with every tunable, and the first run of each code path.
    steady = [p for windows in perf for p in windows[1:]]
    if pct:
        out.append(f"Telemetry overhead over {len(pct)} perf windows: median {statistics.median(pct):.3f} %, max {max(pct):.3f} % of real time; "
                   f"without the first window of each session: median {statistics.median(steady or [0]):.3f} %, max {max(steady or [0]):.3f} %.")
    results = [a.result_of(r.run)[0] for r in reports]
    out.append(f"Results: {results.count('won')} won, {results.count('lost')} lost, {results.count('running')} not finished.")
    events = [e for r in reports for e in r.run.events]
    hunters = {e["ship"] for e in events if e["e"] == "combat.spawn" and e.get("hunter")}
    if hunters:
        killed = {e["ship"] for e in events if e["e"] == "combat.destroyed"} & hunters
        fled = {e["ship"] for e in events if e["e"] == "combat.removed" and e.get("src") == "flee_jump"} & hunters
        out.append(f"Hunters: {len(hunters)} spawned, {len(killed)} destroyed, {len(fled)} jumped out; the rest were alive when the players left or the run ended.")
    # A jump empties the count until the ships arrive; that drop comes back, so it is no loss.
    lost = [e for e in events if e["e"] == "tech.count" and e.get("from") is not None and e["delta"] < 0 and e["cause"] != "jump"]
    if lost:
        by_cause: dict[str, int] = {}
        for e in lost:
            by_cause[e["cause"]] = by_cause.get(e["cause"], 0) - e["delta"]
        out.append("Tech units lost by cause: " + ", ".join(f"{k} {v}" for k, v in sorted(by_cause.items(), key=lambda kv: -kv[1])) + ".")
    first_stage = [e["since_entry_s"] for e in events if e["e"] == "heat.stage" and e.get("stage") == 0 and "since_entry_s" in e]
    if first_stage:
        out.append(f"First stage (Warning) after system entry: median {a.mmss(statistics.median(first_stage))} game time (n={len(first_stage)}).")
    lost_ships = a.player_ships_lost(events)
    if lost_ships:
        out.append(f"Player ships lost: {len(lost_ships)}, in sectors {sorted({e['sec'] for e in lost_ships})}.")
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
