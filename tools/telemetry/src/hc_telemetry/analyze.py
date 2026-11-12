"""Turns the events of a run into tables, series and findings. No rendering here (report.py renders)."""

from __future__ import annotations

import statistics
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from typing import Any

from .load import Run


@dataclass
class Table:
    title: str
    columns: list[str]
    rows: list[list]
    note: str = ""


@dataclass
class Series:
    """Points (x = game seconds of the run) for one line of a chart."""

    label: str
    x: list[float]
    y: list[float]


@dataclass
class RunReport:
    run: Run
    title: str
    facts: list[tuple[str, str]]
    tables: list[Table] = field(default_factory=list)
    findings: list[str] = field(default_factory=list)
    heat: list[Series] = field(default_factory=list)
    stage_marks: list[tuple[float, str]] = field(default_factory=list)
    sector_marks: list[tuple[float, str]] = field(default_factory=list)
    economy: list[Series] = field(default_factory=list)
    heat_sources: dict[str, float] = field(default_factory=dict)
    sector_stays: list[tuple[str, float]] = field(default_factory=list)
    ai_modules: dict[str, int] = field(default_factory=dict)
    perf: list[Series] = field(default_factory=list)


def fmt(v: float | None, digits: int = 1) -> str:
    if v is None:
        return "-"
    if isinstance(v, int) or float(v).is_integer() and abs(v) >= 100:
        return f"{int(v):,}"
    return f"{v:,.{digits}f}"


def mmss(seconds: float | None) -> str:
    if seconds is None:
        return "-"
    m, s = divmod(round(seconds), 60)
    return f"{m}:{s:02d}"


def result_of(run: Run) -> tuple[str, str]:
    for e in reversed(run.events):
        if e["e"] == "run.won":
            return "won", e.get("reason", "")
        if e["e"] == "run.lost":
            return "lost", e.get("reason", "")
    return "running", ""


def analyze(run: Run) -> RunReport:
    ev = run.events
    h = run.header
    result, reason = result_of(run)
    last = ev[-1]
    real_s = sum(s.end["real_s"] if s.end else s.events[-1]["t"] for s in run.sessions)
    facts = [
        ("Run", run.id),
        ("Sessions", f"{len(run.sessions)} ({', '.join('load' if s.header.get('loaded') else 'new' for s in run.sessions)})"),
        ("Result", f"{result}{': ' + reason if reason else ''}"),
        ("Game time", mmss(max(e["gs"] for e in ev))),
        ("Real time", mmss(real_s)),
        ("Sectors reached", str(max(e["sec"] for e in ev))),
        ("Peak tier", str(max(e["tier"] for e in ev))),
        ("Game / mod", f"{h.get('game_version')} / {h.get('mod_version', '')[:19]}"),
        ("Tunables", f"{h.get('tunables_hash')} (full {h.get('tunables_full_hash')})"),
        ("Difficulty", (f"combat {h.get('combat_difficulty')}, economy {h.get('econ_difficulty')}, crew {h.get('crew_scarcity')}, "
                       f"enemy scale {h.get('enemy_scale_setting', '-')} (x{h.get('enemy_scale_factor', '-')}), ironman {h.get('ironman', '-')}, "
                       f"pressure {h.get('pressure', '-')}")),
        ("Map, seed", f"{h.get('map')}, {h.get('seed')}"),
        ("Players", f"{h.get('players')}{' co-op' if h.get('mp') else ''}{' (autotest ' + h.get('label', '') + ')' if h.get('autotest') else ''}"),
        ("Events", f"{len(ev):,}"),
    ]
    rep = RunReport(run, f"{run.id} ({result}, {mmss(last['gs'])} game time)", facts)
    heat_section(rep)
    sector_section(rep)
    economy_section(rep)
    tech_section(rep)
    mission_section(rep)
    story_section(rep)
    combat_section(rep)
    ai_section(rep)
    perf_section(rep)
    errors_section(rep)
    return rep


# ---- Heat


def heat_section(rep: RunReport) -> None:
    ev = rep.run.events
    rep.heat = [Series("heat", [e["gs"] for e in ev], [e["heat"] for e in ev])]
    sources: dict[str, float] = defaultdict(float)
    counts: Counter[str] = Counter()
    for e in ev:
        if e["e"] == "heat.change":
            sources[e["src"]] += e["delta"]
            counts[e["src"]] += 1
        elif e["e"] == "heat.accrual":
            for key in ("time", "trail"):
                if key in e:
                    sources[key] += e[key]
                    counts[key] += 1
    rep.heat_sources = dict(sorted(sources.items(), key=lambda kv: -abs(kv[1])))
    gained = sum(v for v in sources.values() if v > 0)
    rep.tables.append(
        Table(
            "Heat by source",
            ["source", "heat", "share of gains", "events"],
            [[k, fmt(v), f"{100 * v / gained:.0f} %" if gained and v > 0 else "-", counts[k]] for k, v in rep.heat_sources.items()],
            "Heat clamps at 0 and 100, so 'heat' is the change that stuck, not the amount asked for. Time and trail are summed per window.",
        )
    )
    stages = [e for e in ev if e["e"] == "heat.stage"]
    rep.stage_marks = [(e["gs"], e["name"]) for e in stages]
    rep.tables.append(
        Table(
            "Stage starts",
            ["game time", "sector", "system", "stage", "after entry", "live hunters before"],
            [[mmss(e["gs"]), e["sec"], e["sys"], e["name"], mmss(e.get("since_entry_s")), e.get("live_hunters")] for e in stages],
        )
    )


# ---- Sectors


def sector_section(rep: RunReport) -> None:
    ev = rep.run.events
    enters = [e for e in ev if e["e"] == "system.enter"]
    first = ev[0]
    visits: list[dict[str, Any]] = [{"sys": first["sys"], "sec": first["sec"], "tier": first["tier"], "arch": first["arch"], "gs": first["gs"], "heat_in": first["heat"]}]
    for e in enters:
        visits[-1]["stay"] = e["gs"] - visits[-1]["gs"]
        visits[-1]["heat_out"] = e.get("heat_before")
        visits.append({"sys": e["sys"], "sec": e["sec"], "tier": e["tier"], "arch": e["arch"], "gs": e["gs"], "heat_in": e["heat"], "first": e.get("first_visit"),
                       "jump": e.get("jump_kind", "")})
    end = ev[-1]
    visits[-1]["stay"] = end["gs"] - visits[-1]["gs"]
    visits[-1]["heat_out"] = end["heat"]
    rep.sector_marks = [(v["gs"], f"S{v['sec']}") for v in visits[1:]]
    # Per visit counts, by the game time window of the visit.
    bounds = [v["gs"] for v in visits[1:]] + [float("inf")]
    def visit_of(e: dict) -> int:
        for i, b in enumerate(bounds):
            if e["gs"] < b:
                return i
        return len(visits) - 1
    per: dict[int, Counter] = defaultdict(Counter)
    for e in ev:
        i = visit_of(e)
        match e["e"]:
            case "heat.stage":
                per[i]["stages"] += 1
            case "combat.spawn" if e.get("hunter"):
                per[i]["hunters"] += 1
            case "combat.destroyed":
                per[i][f"destroyed_{e.get('side')}"] += 1
            case "mission.ended" if e.get("result") == "success":
                per[i]["missions_done"] += 1
            case "mission.accepted":
                per[i]["missions_accepted"] += 1
            case "econ.trade" if e.get("is_tech"):
                per[i]["tech_sold"] += e["amount"]
    rows = []
    for i, v in enumerate(visits):
        c = per[i]
        rows.append([
            v["sec"], v["sys"], v["arch"], v["tier"], v.get("jump", ""), mmss(v["gs"]), mmss(v.get("stay")), fmt(v["heat_in"]), fmt(v.get("heat_out")),
            c["stages"], c["missions_accepted"], c["missions_done"], c["hunters"], c["destroyed_hunter"], c["destroyed_hostile"], c["tech_sold"],
        ])
        rep.sector_stays.append((f"S{v['sec']} {v['sys']}", v.get("stay") or 0))
    rep.tables.append(
        Table(
            "Sectors",
            ["sector", "system", "type", "tier", "jump in", "entered", "stay", "heat in", "heat out", "stages", "missions taken", "missions done",
             "hunters", "hunters killed", "hostiles killed", "tech sold"],
            rows,
            "Stay is run game time in the system; it stops after a win. A load of an older save replays game time, so such a visit can show twice.",
        )
    )
    stays: list[float] = [v["stay"] for v in visits[:-1] if v.get("stay")]
    if stays:
        rep.findings.append(f"Mean stay per finished sector visit: {mmss(statistics.mean(stays))} (n={len(stays)}).")


# ---- Economy


def economy_section(rep: RunReport) -> None:
    ev = rep.run.events
    snaps = [e for e in ev if e["e"] == "snap"]
    if snaps:
        rep.economy = [
            Series("money (all players)", [e["gs"] for e in snaps], [sum(e["money"].values()) for e in snaps]),
            Series("fleet value", [e["gs"] for e in snaps], [e["fleet_value"] for e in snaps]),
            Series("tech value per unit", [e["gs"] for e in snaps], [e["tech_value"] for e in snaps]),
        ]
    money: dict[str, list[int]] = defaultdict(list)
    for e in ev:
        if e["e"] == "econ.money":
            money[e["src"]].append(e["amount"])
    rep.tables.append(
        Table(
            "Money by source",
            ["source", "net", "gains", "costs", "events"],
            [[k, fmt(sum(v)), fmt(sum(x for x in v if x > 0)), fmt(sum(x for x in v if x < 0)), len(v)]
             for k, v in sorted(money.items(), key=lambda kv: -abs(sum(kv[1])))],
            "'untracked' is a money change outside PlayerState.AddMoney (for example a tech unlock); the next second finds it.",
        )
    )
    fame: dict[str, int] = defaultdict(int)
    for e in ev:
        if e["e"] == "econ.fame":
            fame[e["src"]] += e["amount"]
    if fame:
        rep.tables.append(Table("Fame by source", ["source", "fame"], [[k, fmt(v)] for k, v in sorted(fame.items(), key=lambda kv: -kv[1])]))
    if snaps:
        rows = []
        for e in snaps[:: max(1, len(snaps) // 12)] + [snaps[-1]]:
            rows.append([mmss(e["gs"]), e["sec"], e["tier"], fmt(sum(e["money"].values())), fmt(e["fleet_value"]), fmt(e["fleet_firepower"]),
                         e["fleet_parts"], e["crew"], e["tech"], fmt(e["tech_value"]), fmt(e["notoriety"]), e["why"]])
        rep.tables.append(Table("Power and money over time (snapshots)", ["game time", "sector", "tier", "money", "fleet value", "firepower",
                                                                         "parts", "crew", "tech", "tech value", "notoriety", "why"], rows))


# ---- Tech


def tech_section(rep: RunReport) -> None:
    ev = rep.run.events
    changes = [e for e in ev if e["e"] == "tech.count" and e.get("from") is not None]
    lost: Counter[str] = Counter()
    gained: Counter[str] = Counter()
    for e in changes:
        (lost if e["delta"] < 0 else gained)[e["cause"]] += abs(e["delta"])
    rep.tables.append(
        Table("Tech count changes by cause", ["cause", "units lost", "units gained"],
              [[c, lost[c], gained[c]] for c in sorted(set(lost) | set(gained))],
              "Cause is the newest hint within 5 game seconds of the change: a sale, a Buyer delivery, vault damage, an event, a lost ship.")
    )
    sales = [e for e in ev if e["e"] == "econ.trade" and e.get("is_tech")]
    rep.tables.append(
        Table("Tech trades", ["game time", "sector", "tier", "trade", "units", "unit price", "total", "buyer", "heat"],
              [[mmss(e["gs"]), e["sec"], e["tier"], e["trade"], e["amount"], fmt(e.get("unit_price")), fmt(e.get("total")), e.get("buyer"), fmt(e["heat"])] for e in sales])
    )
    hits = [e for e in ev if e["e"] == "tech.vault_hit"]
    if hits:
        rep.findings.append(f"Vault hits: {len(hits)}, tech lost in them: {sum(e['tech_before'] - e['tech_after'] for e in hits)}.")
    values = [e for e in ev if e["e"] == "tech.value"]
    if values:
        rep.findings.append(f"Tech value rose {len(values)} times, from {fmt(values[0]['to'])} to {fmt(values[-1]['to'])} per unit.")


# ---- Missions


def mission_section(rep: RunReport) -> None:
    ev = rep.run.events
    offered = {e["mission"]: e for e in ev if e["e"] == "mission.offered"}
    accepted = {e["mission"]: e for e in ev if e["e"] == "mission.accepted"}
    ended = [e for e in ev if e["e"] == "mission.ended"]
    by_type: dict[str, Counter] = defaultdict(Counter)
    for e in offered.values():
        by_type[e["type"]]["offered"] += 1
    for e in accepted.values():
        by_type[e["type"]]["accepted"] += 1
    durations: dict[str, list[float]] = defaultdict(list)
    for e in ended:
        by_type[e["type"]][e["result"]] += 1
        if "duration_s" in e:
            durations[e["type"]].append(e["duration_s"])
    rows = []
    for t, c in sorted(by_type.items(), key=lambda kv: (-kv[1]["accepted"], -kv[1]["offered"])):
        d = durations.get(t)
        rows.append([t, c["offered"], c["accepted"], c["success"], c["fail"] + c["timeout"] + c["lost"], c["abandoned"], mmss(statistics.mean(d)) if d else "-"])
    rep.tables.append(Table("Missions by type", ["type", "offered", "accepted", "done", "failed", "abandoned", "mean time to end"], rows,
                            "Offered counts unique mission IDs seen in the station, system and galaxy lists."))
    fees = [a["terms"]["fee"] for a in accepted.values() if "terms" in a]
    sold_out = sum(1 for e in ended if e.get("sold_out"))
    if accepted:
        rep.findings.append(
            f"Missions: {len(offered)} offered, {len(accepted)} taken, {sum(1 for e in ended if e['result'] == 'success')} done; "
            f"contract fees paid {fmt(sum(fees))}, informant sell-outs {sold_out}."
        )


# ---- Events and chains


def story_section(rep: RunReport) -> None:
    ev = rep.run.events
    rows = []
    for e in ev:
        match e["e"]:
            case "event.shown":
                rows.append([mmss(e["gs"]), e["sec"], "page", e["event"], ""])
            case "event.result":
                rows.append([mmss(e["gs"]), e["sec"], "result", e.get("title", ""), "; ".join(e.get("lines", []))[:160]])
            case "chain.start" | "chain.stage":
                rows.append([mmss(e["gs"]), e["sec"], e["e"], f"{e['chain']} {e.get('from_stage', '')}->{e['stage']}", e.get("result", "")])
    rep.tables.append(Table("Events and chains", ["game time", "sector", "what", "id", "result"], rows))


# ---- Combat


def combat_section(rep: RunReport) -> None:
    ev = rep.run.events
    spawns = [e for e in ev if e["e"] == "combat.spawn"]
    destroyed = [e for e in ev if e["e"] == "combat.destroyed"]
    removed = [e for e in ev if e["e"] == "combat.removed"]
    side_spawn = Counter(e.get("side", "stasis") for e in spawns)
    side_dead = Counter(e["side"] for e in destroyed)
    rep.tables.append(Table("Ships by side", ["side", "spawned", "destroyed", "left the system"],
                            [[s, side_spawn[s], side_dead[s], sum(1 for e in removed if e.get("side") == s)] for s in sorted(set(side_spawn) | set(side_dead))]))
    # Hunter outcomes: each hunter that spawned, by its fate.
    hunters = {e["ship"]: e for e in spawns if e.get("hunter")}
    fate: dict[str, tuple[str, float]] = {}
    for e in destroyed:
        if e["ship"] in hunters:
            fate[e["ship"]] = ("destroyed", e["gs"] - hunters[e["ship"]]["gs"])
    for e in removed:
        if e["ship"] in hunters and e["ship"] not in fate:
            fate[e["ship"]] = (e.get("src", "removed"), e["gs"] - hunters[e["ship"]]["gs"])
    enters = [e["gs"] for e in ev if e["e"] == "system.enter"]
    for ship, s in hunters.items():
        if ship not in fate:
            left = next((g for g in enters if g > s["gs"]), None)
            fate[ship] = ("left behind (players jumped)", left - s["gs"]) if left is not None else ("alive at the end", ev[-1]["gs"] - s["gs"])
    outcome: dict[str, list[float]] = defaultdict(list)
    for kind, life in fate.values():
        outcome[kind].append(life)
    rep.tables.append(Table("Hunter outcomes", ["outcome", "hunters", "mean lifetime"],
                            [[k, len(v), mmss(statistics.mean(v))] for k, v in sorted(outcome.items(), key=lambda kv: -len(kv[1]))]))
    damage: dict[str, Counter] = defaultdict(Counter)
    for e in ev:
        if e["e"] == "combat.damage":
            for side in ("player", "hunter", "hostile", "neutral"):
                if side in e:
                    damage[side].update(e[side])
    if damage:
        rep.tables.append(Table("Damage by the side that took it", ["side", "damage", "repaired", "parts lost"],
                                [[s, fmt(c["damage"]), fmt(c["repaired"]), c["parts_lost"]] for s, c in damage.items()]))
    deaths = [e for e in ev if e["e"] == "combat.crew_died"]
    lost_ships = [e for e in destroyed if e["side"] == "player"]
    if hunters:
        killed = sum(1 for k, _ in fate.values() if k == "destroyed")
        rep.findings.append(f"Hunters: {len(hunters)} spawned, {killed} destroyed ({100 * killed / len(hunters):.0f} %), "
                            f"player ships lost {len(lost_ships)}, crew lost {len(deaths)}.")


# ---- AI


def ai_section(rep: RunReport) -> None:
    ev = rep.run.events
    switches = Counter(e["to"] for e in ev if e["e"] == "ai.switch")
    by_side: dict[str, Counter] = defaultdict(Counter)
    for e in ev:
        if e["e"] == "ai.switch":
            by_side[e["side"]][e["to"]] += 1
    snaps = [e for e in ev if e["e"] == "snap" and "ai" in e]
    sums: dict[str, dict] = {}
    # The sums count per session, so the last snapshot of each session holds that session's total.
    for s in rep.run.sessions:
        last = next((e for e in reversed(s.events) if e["e"] == "snap" and "ai" in e), None)
        if last:
            for k, v in last["ai"].items():
                if isinstance(v, dict):
                    acc = sums.setdefault(k, {"ticks": 0, "switches": 0})
                    acc["ticks"] += v.get("ticks", 0)
                    acc["switches"] += v.get("switches", 0)
                else:
                    sums[k] = {"ticks": sums.get(k, {}).get("ticks", 0) + v}
    rep.ai_modules = dict(switches.most_common())
    rows = [[k, v.get("ticks"), v.get("switches", "-"), switches.get(k, 0)] for k, v in sorted(sums.items(), key=lambda kv: -kv[1]["ticks"])]
    rep.tables.append(Table("AI modules (strategy wins per AI tick, tactics active ticks)", ["module", "ticks", "switches into", "switch events logged"], rows,
                            "Strategy modules: the module that drove the ship in an AI tick. 'tactic:' rows count AI ticks with that tactic active. "
                            "focus_evals / focus_boosted count target scores and the ones that focus fire raised."))
    side_rows = [[side, ", ".join(f"{m} {n}" for m, n in c.most_common(6))] for side, c in by_side.items()]
    rep.tables.append(Table("AI switches by side (most common targets)", ["side", "modules"], side_rows))
    _ = snaps


# ---- Performance


def perf_section(rep: RunReport) -> None:
    ev = rep.run.events
    perf = [e for e in ev if e["e"] == "perf"]
    if not perf:
        return
    x = [e["t"] for e in perf]
    rep.perf = [
        Series("telemetry % of real time", x, [e["tele_pct_wall"] for e in perf]),
        Series("frame p95 ms", x, [e["frame_p95_ms"] for e in perf]),
        Series("sim tick mean ms", x, [e["tick_mean_ms"] for e in perf]),
    ]
    def stat(key: str, f=statistics.median) -> str:
        return fmt(f([e[key] for e in perf]), 3)
    rows = [
        ["telemetry % of real time", stat("tele_pct_wall"), stat("tele_pct_wall", max)],
        ["telemetry ms per window", stat("tele_ms"), stat("tele_ms", max)],
        ["telemetry writer ms per window (background thread)", stat("tele_writer_ms"), stat("tele_writer_ms", max)],
        ["frame mean ms", stat("frame_mean_ms"), stat("frame_mean_ms", max)],
        ["frame p95 ms", stat("frame_p95_ms"), stat("frame_p95_ms", max)],
        ["frame max ms", stat("frame_max_ms"), stat("frame_max_ms", max)],
        ["frames over 33 ms", stat("frames_over_33ms"), stat("frames_over_33ms", max)],
        ["sim tick mean ms", stat("tick_mean_ms"), stat("tick_mean_ms", max)],
        ["sim tick max ms", stat("tick_max_ms"), stat("tick_max_ms", max)],
        ["GC pause ms", stat("gc_pause_ms"), stat("gc_pause_ms", max)],
        ["gen 2 collections", stat("gc2"), stat("gc2", max)],
        ["heap MB", stat("heap_mb"), stat("heap_mb", max)],
        ["events per window", stat("tele_events"), stat("tele_events", max)],
    ]
    ends = [s.end for s in rep.run.sessions if s.end]
    note = ""
    if ends:
        pct = [e.get("overhead_pct_wall") for e in ends if e.get("overhead_pct_wall") is not None]
        note = f"Whole sessions: telemetry {', '.join(f'{p:.3f} %' for p in pct)} of real time; dropped events {sum(e['dropped'] for e in ends)}."
        rep.findings.append(f"Telemetry overhead: median {stat('tele_pct_wall')} % of real time per {fmt(perf[0]['window_ms'] / 1000, 0)} s window, "
                            f"max {stat('tele_pct_wall', max)} %. {note}")
    if rep.run.header.get("autotest"):
        note += " Headless test games draw no frames, so frame times there are not play frame times."
    rep.tables.append(Table("Performance (per perf window)", ["measure", "median", "max"], rows, note.strip()))


def errors_section(rep: RunReport) -> None:
    errors = [e for e in rep.run.events if e["e"] == "log" and e.get("level") == "ERROR"]
    errors += [e for e in rep.run.events if e["e"] == "mp.desync"]
    if errors:
        rep.tables.append(Table("Errors and desyncs", ["game time", "event", "message"],
                                [[mmss(e["gs"]), e["e"], (e.get("msg") or str(e.get("input_tick", "")))[:200]] for e in errors]))
        rep.findings.append(f"{len(errors)} error log lines or desyncs (see 'Errors and desyncs').")


# ---- Across runs


def overview(reports: list[RunReport]) -> Table:
    rows = []
    for r in reports:
        ev = r.run.events
        result, reason = result_of(r.run)
        rows.append([r.run.id, r.run.header.get("wall_utc", "")[:16], r.run.header.get("label") or ("autotest" if r.run.header.get("autotest") else "play"), result, reason[:50],
                     mmss(max(e["gs"] for e in ev)), max(e["sec"] for e in ev), max(e["tier"] for e in ev),
                     sum(1 for e in ev if e["e"] == "heat.stage"), sum(1 for e in ev if e["e"] == "mission.ended" and e.get("result") == "success"),
                     sum(e["amount"] for e in ev if e["e"] == "econ.trade" and e.get("is_tech")), len(ev)])
    return Table("Runs", ["run", "started (UTC)", "label", "result", "reason", "game time", "sectors", "peak tier", "stages", "missions done",
                          "tech traded", "events"], rows)


def pooled_heat_sources(reports: list[RunReport]) -> Table:
    total: dict[str, float] = defaultdict(float)
    for r in reports:
        for k, v in r.heat_sources.items():
            total[k] += v
    gained = sum(v for v in total.values() if v > 0)
    return Table("Heat by source, all runs", ["source", "heat", "share of gains"],
                 [[k, fmt(v), f"{100 * v / gained:.0f} %" if gained and v > 0 else "-"] for k, v in sorted(total.items(), key=lambda kv: -abs(kv[1]))])
