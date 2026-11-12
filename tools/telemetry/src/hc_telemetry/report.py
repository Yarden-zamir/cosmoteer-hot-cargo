"""Renders run reports as markdown and as one self-contained HTML file (charts are inline PNG images)."""

from __future__ import annotations

import base64
import html
import io
from collections.abc import Mapping, Sequence

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt

from .analyze import RunReport, Series, Table

# ---- Markdown


def md_table(t: Table) -> str:
    if not t.rows:
        return f"### {t.title}\n\n_No data._\n"
    head = "| " + " | ".join(t.columns) + " |\n|" + "---|" * len(t.columns) + "\n"
    body = "".join("| " + " | ".join(str(c).replace("|", "/") for c in row) + " |\n" for row in t.rows)
    note = f"\n{t.note}\n" if t.note else ""
    return f"### {t.title}\n\n{head}{body}{note}"


def markdown(reports: list[RunReport], overview: list[Table], conclusions: list[str]) -> str:
    out = ["# Hot Cargo telemetry report\n"]
    if conclusions:
        out.append("## Findings across runs\n")
        out += [f"- {c}" for c in conclusions]
        out.append("")
    for t in overview:
        out.append(md_table(t))
    for r in reports:
        out.append(f"## Run {r.title}\n")
        out += [f"- **{k}**: {v}" for k, v in r.facts]
        out.append("")
        if r.findings:
            out.append("Findings:\n")
            out += [f"- {f}" for f in r.findings]
            out.append("")
        out += [md_table(t) for t in r.tables]
    return "\n".join(out)


# ---- Charts


def png(fig) -> str:
    buf = io.BytesIO()
    fig.savefig(buf, format="png", dpi=110, bbox_inches="tight")
    plt.close(fig)
    return base64.b64encode(buf.getvalue()).decode()


def line_chart(title: str, series: list[Series], marks: Sequence[tuple[float, str]] = (), stage_marks: Sequence[tuple[float, str]] = (),
               ylabel: str = "", logy: bool = False, xlabel: str = "game time (min)", xscale: float = 60.0) -> str | None:
    if not series or not any(s.x for s in series):
        return None
    fig, ax = plt.subplots(figsize=(9, 3.2))
    for s in series:
        ax.plot([x / xscale for x in s.x], s.y, label=s.label, linewidth=1.4)
    for x, label in marks:
        ax.axvline(x / xscale, color="#888", linestyle="--", linewidth=0.8)
        ax.text(x / xscale, ax.get_ylim()[1], label, fontsize=7, va="top", ha="left", color="#555")
    for x, label in stage_marks:
        ax.axvline(x / xscale, color="#d62728", linestyle=":", linewidth=0.8)
        ax.text(x / xscale, ax.get_ylim()[0], label, fontsize=7, va="bottom", ha="right", rotation=90, color="#d62728")
    if logy:
        ax.set_yscale("symlog")
    ax.set_title(title, fontsize=10)
    ax.set_xlabel(xlabel)
    ax.set_ylabel(ylabel)
    if len(series) > 1:
        ax.legend(fontsize=8)
    ax.grid(alpha=0.3)
    return png(fig)


def bar_chart(title: str, values: Mapping[str, float]) -> str | None:
    if not values:
        return None
    items = list(values.items())[:20]
    fig, ax = plt.subplots(figsize=(9, max(2.0, 0.32 * len(items) + 0.8)))
    labels = [k for k, _ in items][::-1]
    nums = [v for _, v in items][::-1]
    ax.barh(labels, nums, color=["#2ca02c" if v < 0 else "#1f77b4" for v in nums])
    ax.set_title(title, fontsize=10)
    ax.grid(alpha=0.3, axis="x")
    return png(fig)


def run_charts(r: RunReport) -> list[str]:
    charts = [
        line_chart("Heat (dashed: system entry, dotted: stage start)", r.heat, r.sector_marks, r.stage_marks, "heat"),
        line_chart("Money, fleet value and tech value", r.economy, r.sector_marks, ylabel="credits", logy=True),
        bar_chart("Heat by source", r.heat_sources),
        bar_chart("Stay per sector visit (game seconds)", dict(r.sector_stays)),
        bar_chart("AI strategy switches logged, by target module", r.ai_modules),
        line_chart("Performance per window", r.perf, xlabel="real time (min)"),
    ]
    return [c for c in charts if c]


# ---- HTML

CSS = """
:root { --bg: #fafaf7; --fg: #1b1b1b; --muted: #666; --line: #ddd; --head: #efefe9; --accent: #3b5bdb; }
@media (prefers-color-scheme: dark) { :root { --bg: #16171a; --fg: #e8e8e8; --muted: #9a9a9a; --line: #333; --head: #23252a; --accent: #8ca0ff; }
  img { background: #fff; } }
body { background: var(--bg); color: var(--fg); font: 14px/1.45 system-ui, sans-serif; margin: 0 auto; max-width: 1200px; padding: 16px; }
h1, h2, h3 { line-height: 1.2; } h2 { border-top: 2px solid var(--line); padding-top: 18px; margin-top: 36px; }
table { border-collapse: collapse; margin: 8px 0 4px; font-size: 13px; display: block; overflow-x: auto; max-width: 100%; }
th, td { border: 1px solid var(--line); padding: 3px 8px; text-align: left; white-space: nowrap; } th { background: var(--head); }
.note { color: var(--muted); font-size: 12px; margin: 0 0 14px; } img { max-width: 100%; border: 1px solid var(--line); margin: 6px 0; }
ul.facts { columns: 2; } nav a { color: var(--accent); margin-right: 12px; } details { margin: 8px 0; }
"""


def html_table(t: Table) -> str:
    if not t.rows:
        return f"<h3>{html.escape(t.title)}</h3><p class='note'>No data.</p>"
    head = "".join(f"<th>{html.escape(c)}</th>" for c in t.columns)
    body = "".join("<tr>" + "".join(f"<td>{html.escape(str(c))}</td>" for c in row) + "</tr>" for row in t.rows)
    note = f"<p class='note'>{html.escape(t.note)}</p>" if t.note else ""
    return f"<h3>{html.escape(t.title)}</h3><table><thead><tr>{head}</tr></thead><tbody>{body}</tbody></table>{note}"


def html_report(reports: list[RunReport], overview: list[Table], conclusions: list[str], overview_charts: list[str]) -> str:
    parts = ["<!doctype html><html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'>",
             f"<title>Hot Cargo telemetry</title><style>{CSS}</style></head><body><h1>Hot Cargo telemetry report</h1>"]
    parts.append("<nav>" + "".join(f"<a href='#{html.escape(r.run.id)}'>{html.escape(r.run.id)}</a>" for r in reports) + "</nav>")
    if conclusions:
        parts.append("<h2>Findings across runs</h2><ul>" + "".join(f"<li>{html.escape(c)}</li>" for c in conclusions) + "</ul>")
    parts += [html_table(t) for t in overview]
    parts += [f"<img alt='chart' src='data:image/png;base64,{c}'>" for c in overview_charts]
    for r in reports:
        parts.append(f"<h2 id='{html.escape(r.run.id)}'>Run {html.escape(r.title)}</h2>")
        parts.append("<ul class='facts'>" + "".join(f"<li><b>{html.escape(k)}</b>: {html.escape(v)}</li>" for k, v in r.facts) + "</ul>")
        if r.findings:
            parts.append("<ul>" + "".join(f"<li>{html.escape(f)}</li>" for f in r.findings) + "</ul>")
        parts += [f"<img alt='chart' src='data:image/png;base64,{c}'>" for c in run_charts(r)]
        parts += [html_table(t) for t in r.tables]
    parts.append("</body></html>")
    return "".join(parts)
