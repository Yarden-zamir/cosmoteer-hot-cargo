# Plan: 3 heat stages, heat 5 % slower

## Decisions
- Stages: Warning 40, Hunt 75, Strike 100.
  - Warning at 40: above the highest carried heat after a jump (25), so the scouts always come after arrival.
  - Hunt stays at 75: the station markup (spec 655) starts there, and BALANCE.md says "buy the next jump before the Hunt stage".
  - Strike keeps repeat waves, Strike music and "Jump out!".
  - Patrol goes. Warning sends 1 ship in sector 1, 2 in the last sector (scouts, small groups).
- HeatPerGameMinute 1.2 -> 1.14, HeatPerStandardMission 1.5 -> 1.425 (-5 %).
- The stage list in tunables decides the count. Code reads the count from it.

## Steps
1. Tunables, strings, code that assumes a stage count or index.
2. Tests (unit, autotest scripts) that name Patrol or a stage index.
3. Spec, TUNABLES.md, BALANCE.md (missions before each stage per jump kind), docs, tools.
4. Full autotest run, merge origin/main, push.
