"""Reads telemetry JSONL files and groups their events into runs and sessions."""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path


@dataclass
class Session:
    path: Path
    header: dict
    events: list[dict]
    end: dict | None

    @property
    def id(self) -> str:
        return self.header["session"]

    @property
    def wall(self) -> str:
        return self.header.get("wall_utc", "")


@dataclass
class Run:
    id: str
    sessions: list[Session] = field(default_factory=list)

    @property
    def events(self) -> list[dict]:
        """Events of every session in session order. A loaded session repeats game time from the save, so 'gs' can go back."""
        return [e for s in self.sessions for e in s.events]

    @property
    def header(self) -> dict:
        return self.sessions[0].header

    def named(self, *names: str) -> list[dict]:
        return [e for e in self.events if e["e"] in names]


def read_file(path: Path) -> list[dict]:
    """Reads one JSONL file. A broken last line (a game that was killed while it wrote) is skipped; any other broken line fails."""
    lines = path.read_text(encoding="utf-8").splitlines()
    events = []
    for i, line in enumerate(lines):
        if not line.strip():
            continue
        try:
            events.append(json.loads(line))
        except json.JSONDecodeError:
            assert i == len(lines) - 1, f"{path}:{i + 1} is not valid JSON"
    return events


def load(root: Path) -> list[Run]:
    """All runs under root, oldest first. Part files (<name>.partN.jsonl) join the session of their first file."""
    files = sorted(root.rglob("*.jsonl"))
    by_session: dict[str, Session] = {}
    parts: list[tuple[Path, list[dict]]] = []
    for path in files:
        if path.parent.name == "report":
            continue
        events = read_file(path)
        if not events:
            continue
        if events[0]["e"] == "file.part":
            parts.append((path, events))
            continue
        header = events[0]
        assert header["e"] == "session.start", f"{path} does not start with session.start"
        # The same file can sit in two slot folders after a copy; the session ID keeps one.
        by_session.setdefault(header["session"], Session(path, header, events, None))
    for path, events in sorted(parts, key=lambda p: p[0].name):
        session = by_session.get(events[0]["session"])
        assert session is not None, f"{path} continues session {events[0]['session']}, whose first file is missing"
        session.events.extend(events[1:])
    runs: dict[str, Run] = {}
    for s in sorted(by_session.values(), key=lambda s: s.wall):
        s.end = next((e for e in reversed(s.events) if e["e"] == "session.end"), None)
        runs.setdefault(s.header["run"], Run(s.header["run"])).sessions.append(s)
    return sorted(runs.values(), key=lambda r: r.sessions[0].wall)
