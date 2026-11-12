import json
from pathlib import Path

from hc_telemetry.analyze import analyze
from hc_telemetry.load import load

CTX = {"t": 0, "ss": 0, "sec": 1, "sys": "A", "tier": 1, "arch": "basic", "heat": 0, "tech": 8}


def ev(name: str, gs: float, **fields) -> dict:
    return {"e": name, **CTX, "gs": gs, **fields}


def write(path: Path, events: list[dict], broken_tail: bool = False) -> None:
    text = "\n".join(json.dumps(e) for e in events) + "\n"
    path.write_text(text + ('{"e": "trunc' if broken_tail else ""))


def test_sessions_join_runs_and_parts_and_a_cut_last_line(tmp_path: Path) -> None:
    head = {"run": "r1", "session": "s1", "wall_utc": "2026-01-01T00:00"}
    write(tmp_path / "r1__s1.jsonl", [ev("session.start", 0, **head), ev("heat.change", 1, src="fame", delta=5)])
    write(tmp_path / "r1__s1.part2.jsonl", [{"e": "file.part", "part": 2, "run": "r1", "session": "s1"}, ev("run.won", 9, reason="sold")], broken_tail=True)
    write(tmp_path / "r1__s2.jsonl", [ev("session.start", 9, run="r1", session="s2", wall_utc="2026-01-01T01:00", loaded=True)])
    runs = load(tmp_path)
    assert [r.id for r in runs] == ["r1"]
    assert [s.id for s in runs[0].sessions] == ["s1", "s2"]
    assert [e["e"] for e in runs[0].sessions[0].events] == ["session.start", "heat.change", "run.won"]


def test_hunter_fates_and_heat_sources(tmp_path: Path) -> None:
    hunter = {"hunter": True, "side": "hunter"}
    write(tmp_path / "r__s.jsonl", [
        ev("session.start", 0, run="r", session="s", wall_utc="x"),
        ev("heat.accrual", 5, time=2.0, trail=1.0),
        ev("heat.change", 6, src="fame", delta=10.0),
        ev("combat.spawn", 10, ship="h1", **hunter),
        ev("combat.spawn", 10, ship="h2", **hunter),
        ev("combat.spawn", 10, ship="h3", **hunter),
        ev("combat.destroyed", 40, ship="h1", side="hunter"),
        ev("combat.removed", 50, ship="h2", side="hunter", src="flee_jump"),
        ev("system.enter", 70, sys="B", sec=2),
    ])
    rep = analyze(load(tmp_path)[0])
    assert rep.heat_sources == {"fame": 10.0, "time": 2.0, "trail": 1.0}
    outcomes = {row[0]: row[1] for row in next(t for t in rep.tables if t.title == "Hunter outcomes").rows}
    assert outcomes == {"destroyed": 1, "flee_jump": 1, "left behind (players jumped)": 1}
