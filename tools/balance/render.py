"""Renders docs/balance.html from tools/balance/model.json. Self-contained: inline SVG charts, no external files.

Usage: uv run --no-project tools/balance/render.py
"""

import html
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
M = json.loads((ROOT / "tools/balance/model.json").read_text())
B, A = M["before"]["sectors"], M["after"]["sectors"]
SECTORS = [f"S{r['sector']}·T{r['tier']}" for r in A]


def bars(title, labels, series, unit="", lines=(), log=False, note=""):
    """Grouped or stacked bar chart. series: list of (name, css var, values, stack group)."""
    w, pad_l, pad_t = 640, 64, 16
    pad_b = 90 if len(labels) > 9 else 40
    h = 220 + pad_b
    groups = sorted({s[3] for s in series}, key=[s[3] for s in series].index)
    totals = [[sum(s[2][i] for s in series if s[3] == g) for g in groups] for i in range(len(labels))]
    top = max([max(t) for t in totals] + [v for v, _ in lines]) * 1.1 or 1
    plot_w, plot_h = w - pad_l - 10, h - pad_b - pad_t
    slot = plot_w / len(labels)
    bw = slot * 0.8 / len(groups)
    out = [f'<figure><figcaption>{html.escape(title)}</figcaption><svg viewBox="0 0 {w} {h}" role="img" aria-label="{html.escape(title)}">']
    for k in range(5):
        v = top * k / 4
        y = pad_t + plot_h - plot_h * k / 4
        out.append(f'<line x1="{pad_l}" x2="{w - 10}" y1="{y:.1f}" y2="{y:.1f}" class="grid"/>'
                   f'<text x="{pad_l - 6}" y="{y + 4:.1f}" class="axis" text-anchor="end">{fmt(v)}{unit}</text>')
    for i, label in enumerate(labels):
        for gi, g in enumerate(groups):
            base = 0.0
            x = pad_l + i * slot + slot * 0.1 + gi * bw
            for name, color, values, group in series:
                if group != g:
                    continue
                v = values[i]
                bh = plot_h * v / top
                y = pad_t + plot_h - plot_h * (base + v) / top
                out.append(f'<rect x="{x:.1f}" y="{y:.1f}" width="{bw - 2:.1f}" height="{max(bh, 0):.1f}" style="fill:var({color})"><title>{html.escape(name)} {label}: {fmt(v)}{unit}</title></rect>')
                base += v
        lx = pad_l + i * slot + slot / 2
        if len(labels) > 9:  # many labels: rotate them so they do not overlap
            out.append(f'<text x="{lx:.1f}" y="{h - pad_b + 10}" class="axis" text-anchor="end" transform="rotate(-35 {lx:.1f} {h - pad_b + 10})">{html.escape(label)}</text>')
        else:
            out.append(f'<text x="{lx:.1f}" y="{h - pad_b + 16}" class="axis" text-anchor="middle">{html.escape(label)}</text>')
    for v, name in lines:
        y = pad_t + plot_h - plot_h * v / top
        out.append(f'<line x1="{pad_l}" x2="{w - 10}" y1="{y:.1f}" y2="{y:.1f}" class="mark"/><text x="{w - 12}" y="{y - 4:.1f}" class="axis" text-anchor="end">{html.escape(name)}</text>')
    out.append("</svg>")
    out.append('<div class="legend">' + "".join(f'<span><i style="background:var({c})"></i>{html.escape(n)}</span>' for n, c, _, _ in series) + "</div>")
    if note:
        out.append(f'<p class="note">{note}</p>')
    out.append("</figure>")
    return "".join(out)


def fmt(v):
    if abs(v) >= 1e6:
        return f"{v / 1e6:.1f}M"
    if abs(v) >= 1e3:
        return f"{v / 1e3:.0f}k"
    return f"{v:.1f}" if v and abs(v) < 10 and v != int(v) else f"{v:.0f}"


def col(rows, key):
    return [r[key] for r in rows]


meas = M["measured"]
sys_keys = sorted(meas["board"])
fuel_before = [sum(max(0, x) for x in meas["stationFuel"][k]) for k in sys_keys]
fuel_after = [sum(max(0, x) for x in meas["stationFuelAfter"][k]) for k in sys_keys]
sys_labels = [f"S{meas['systems'][k]['sector']}·T{meas['systems'][k]['tier']}" for k in sys_keys]
hunters = meas["hunters"]
htiers = sorted(hunters, key=int)
types = sorted(meas["missionTypes"].items(), key=lambda kv: -kv[1]["fame"])

charts = [
    ("money", "Money", [
        bars("Income per sector (6 missions + 3 hunter wrecks) and the next fleet step", SECTORS, [
            ("before (RewardScale 2)", "--c3", col(B, "income"), "b"), ("salvage", "--c4", col(B, "salvage"), "b"),
            ("after (RewardScale 1)", "--c1", col(A, "income"), "a"), ("salvage ", "--c4", col(A, "salvage"), "a"),
            ("fleet value target", "--c5", [r["fleetTarget"] for r in A], "t"),
        ], note="Fleet target: the ship value maximum of the next sector's tier + 1. After the change, a sector pays about twice the target step."),
        bars("Bank at the end of each sector (after buying the fleet target)", SECTORS, [
            ("before", "--c3", col(B, "bank"), "b"), ("after", "--c1", col(A, "bank"), "a")]),
        bars("One tech unit, in typical missions of the sector", SECTORS, [
            ("before", "--c3", col(B, "techUnitInMissions"), "b"), ("after", "--c1", col(A, "techUnitInMissions"), "a")],
            note="The tech value does not use RewardScale. A sale is a real bail-out again: 1 to 2.4 missions of money."),
    ]),
    ("heat", "Heat", [
        bars("Heat in one sector, before: one typical mission adds about 61 heat", SECTORS, [
            ("carried in", "--c5", col(B, "heatIn"), "x"), ("missions", "--c3", col(B, "heatMissions"), "x"),
            ("time", "--c2", col(B, "heatTime"), "x"), ("contracts", "--c4", col(B, "heatContracts"), "x")],
            lines=[(25, "Warning"), (50, "Patrol"), (75, "Hunt"), (100, "Strike")],
            note="Before the fame cap, the Strike came after 1 or 2 missions. After the cap, missions added no heat at all (the cap leak)."),
        bars("Heat in one sector, after: 6 missions in 17.5 minutes reach the Strike", SECTORS, [
            ("carried in", "--c5", col(A, "heatIn"), "x"), ("missions", "--c1", col(A, "heatMissions"), "x"),
            ("time", "--c2", col(A, "heatTime"), "x"), ("contracts", "--c4", col(A, "heatContracts"), "x")],
            lines=[(25, "Warning"), (50, "Patrol"), (75, "Hunt"), (100, "Strike")]),
        bars("Missions until the Strike", SECTORS, [
            ("before", "--c3", col(B, "missionsToStrike"), "b"), ("after", "--c1", col(A, "missionsToStrike"), "a")],
            lines=[(6, "target 6")]),
        bars("Fame per mission type (std fame units, measured)", [t.replace("HotCargo", "HC ").replace("Destruction", " attack") for t, _ in types[:12]], [
            ("fame", "--c1", [v["fame"] for _, v in types[:12]], "a"), ("money", "--c4", [v["money"] for _, v in types[:12]], "b")],
            note="A vanilla mission pays kill fame and reward fame, so a typical mission is 3.4 std fame, not 1."),
    ]),
    ("fuel", "Fuel", [
        bars("Fuel that the stations of each system hold (measured, same map seed)", sys_labels, [
            ("before", "--c3", fuel_before, "b"), ("after", "--c1", fuel_after, "a")]),
        bars("Fuel for one relay jump, and the fuel that every trade station keeps (after)", SECTORS, [
            ("typical jump, before", "--c3", col(B, "jumpFuel"), "b"), ("typical jump, after", "--c1", col(A, "jumpFuel"), "a"),
            ("worst jump, after", "--c2", col(A, "worstJumpFuel"), "w"), ("station floor, after", "--c4", col(A, "stationFuelFloor"), "f")],
            note="Typical: 7.5 LY, a ship of the tier, full drive efficiency. Worst: 10 LY, a fleet of the next tier, 70 % efficiency. "
                 "Every trade station keeps the floor for sale, so a run never gets stuck on fuel.",
            lines=[(30, "one hunter wreck (after)")]),
    ]),
    ("ships", "Ships and boards", [
        bars("Hunter wreck salvage (measured)", [f"T{t}" for t in htiers], [
            ("salvage % of ship value", "--c1", [hunters[t]["salvageFraction"] * 100 for t in htiers], "a")], unit="%",
            note="Destroyed hunters drop about 10 % of their value in resources on the Fast economy, about one standard mission at RewardScale 1."),
        bars("Station board offers per system (galaxy visit offers left out)", sys_labels, [
            ("vanilla offers, before", "--c5", [meas["board"][k]["total"] - meas["board"][k]["hotcargo"] for k in sys_keys], "b"),
            ("Hot Cargo, before", "--c3", [meas["board"][k]["hotcargo"] for k in sys_keys], "b"),
            ("vanilla offers, after", "--c5", [meas["boardAfter"][k]["total"] - meas["boardAfter"][k]["hotcargo"] for k in sys_keys], "a"),
            ("Hot Cargo, after", "--c1", [meas["boardAfter"][k]["hotcargo"] for k in sys_keys], "a")]),
    ]),
]

summary = [
    ("RewardScale", "2.0", "1.0", "Six missions paid 4 × the tier's ship value per sector, 5 to 8 times the need."),
    ("TechCostScale (new)", "1", "2.5", "Base blueprints cost 4 % of a run; now 19 %. Overclocks stay out of reach."),
    ("HeatPerStandardMission", "15", "1.5", "A typical mission pays 3.4 std fame, not 1."),
    ("HeatPerGameMinute", "2", "1.2", "Time gives about a quarter of a sector's heat."),
    ("Heat above the fame cap", "none", "counted", "Missions above the vanilla fame cap added no heat."),
    ("Peak tier", "any", "capped by sector", "A visit to a faction home system multiplied the tech value."),
    ("EscortHeatChange", "−15", "−8", "About 1.5 typical missions of heat."),
    ("Chains per system", "every station", "60 %, at most 2", "Chains were the biggest Hot Cargo share of the boards."),
    ("Hunters.MinFuel", "40", "60", "Hunters are the best fuel source."),
    ("Hunters.RouteTierFloor (new)", "system tier", "at least the route tier", "A low side system late in the run made the hunters weak."),
    ("Fuel price, stack, stock, jump cost", "50, 20, 85 %, ×1", "150, 10, 33 %, ×2", "Stations hold about a fifth of the vanilla fuel. Relay jumps cost fuel."),
    ("Station fuel floor (new)", "none", "one worst-case jump per trade station", "A run never gets stuck on fuel."),
]

page = f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Hot Cargo Balance</title>
<style>
:root {{ --bg:#f7f5f0; --fg:#1c1c1c; --muted:#666; --card:#fff; --line:#ddd;
  --c1:#2a7de1; --c2:#e1a32a; --c3:#c94c4c; --c4:#3aa676; --c5:#9a9a9a; }}
@media (prefers-color-scheme: dark) {{ :root:not([data-theme="light"]) {{ --bg:#15171a; --fg:#e8e6e1; --muted:#9a9a9a; --card:#1f2226; --line:#33373c;
  --c1:#5aa2ff; --c2:#f0b84a; --c3:#ef6b6b; --c4:#55c793; --c5:#777; }} }}
:root[data-theme="dark"] {{ --bg:#15171a; --fg:#e8e6e1; --muted:#9a9a9a; --card:#1f2226; --line:#33373c;
  --c1:#5aa2ff; --c2:#f0b84a; --c3:#ef6b6b; --c4:#55c793; --c5:#777; }}
body {{ margin:0; background:var(--bg); color:var(--fg); font:15px/1.5 system-ui, sans-serif; }}
main {{ max-width:1100px; margin:0 auto; padding:24px 16px 64px; }}
h1 {{ margin:0 0 4px; font-size:28px; }} h2 {{ margin:32px 0 8px; }}
p.lead, p.note {{ color:var(--muted); }} p.note {{ font-size:13px; margin:4px 0 0; }}
.grid2 {{ display:grid; grid-template-columns:repeat(auto-fit, minmax(min(100%, 480px), 1fr)); gap:16px; }}
figure {{ margin:0; background:var(--card); border:1px solid var(--line); border-radius:8px; padding:12px; }}
figcaption {{ font-weight:600; margin-bottom:6px; }}
svg {{ width:100%; height:auto; display:block; }}
svg .grid {{ stroke:var(--line); }} svg .mark {{ stroke:var(--fg); stroke-dasharray:4 4; opacity:.5; }}
svg .axis {{ fill:var(--muted); font-size:11px; }}
.legend {{ display:flex; flex-wrap:wrap; gap:12px; font-size:13px; color:var(--muted); }}
.legend i {{ display:inline-block; width:10px; height:10px; border-radius:2px; margin-right:4px; }}
table {{ width:100%; min-width:640px; border-collapse:collapse; background:var(--card); }}
th, td {{ text-align:left; padding:6px 8px; border-bottom:1px solid var(--line); vertical-align:top; }}
.scroll {{ overflow-x:auto; }}
</style></head><body><main>
<h1>Hot Cargo balance</h1>
<p class="lead">Economy model from the vanilla rules and two headless measurements (mission boards and hunters on the main route).
6 missions and 17.5 minutes per sector, 8 tech units held. Details and reasons: BALANCE.md.</p>
<h2>Changes</h2>
<div class="scroll"><table><tr><th>What</th><th>Before</th><th>After</th><th>Why</th></tr>
{"".join(f"<tr><td>{html.escape(a)}</td><td>{html.escape(b)}</td><td>{html.escape(c)}</td><td>{html.escape(d)}</td></tr>" for a, b, c, d in summary)}
</table></div>
{"".join(f'<h2 id="{cid}">{html.escape(name)}</h2><div class="grid2">{"".join(figs)}</div>' for cid, name, figs in charts)}
<h2>Open questions</h2>
<ol><li>Resolved: hunters use at least the main route tier of the sector index (requirement 305).</li>
<li>Co-op: rewards split between players, heat and hunters do not. Check a co-op RewardScale with telemetry.</li>
<li>Check the 6 missions in 17.5 minutes and the base attack share against real telemetry.</li></ol>
</main></body></html>
"""
(ROOT / "docs/balance.html").write_text(page)
print("wrote docs/balance.html", len(page))
