# Plan: runtime invariants and a no-cheat playthrough

## Design
- `src/HotCargo/Dev/Invariants.cs`: one read-only checker on `Run.Hooks.Tick`, once per game second (tunable `Dev.InvariantSeconds`, switch `Dev.Invariants`, default on).
- A broken rule logs `ERROR invariant <name>: <details>` once per rule and cause (no flood). Autotests fail on ERROR; telemetry records the log line.
- Event-based rules (vault hit, heat change, hostile attack) hook the existing patches or telemetry points read-only.
- Spec: `spec/44-invariants.md`, requirements 990 to 1009.

## Steps
1. Plan and draft PR.
2. Checker skeleton, switch, tech count rule, heat range rule.
3. Vault damage step rule.
4. Hostile-before-trigger rule.
5. Mission marker rule, shown text rule, contract rival symmetry rule, heat source rule.
6. `core-playthrough` test (no godmode, no teleports).
7. Full suite with invariants on; list each failure with its bug; fix wrong rules.
8. Spec, merge origin/main, final push.
