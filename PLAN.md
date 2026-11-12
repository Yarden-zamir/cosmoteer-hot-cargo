# Test tiers

Goal: fewer slow game tests. Logic checks move to unit tests (`*Model.cs`). Game tests keep a smoke set per area.

Decisions:
- A check is "unit" when it only reads a number or state that a pure function computes from tunables and inputs.
- A check is "game" when it needs spawns, AI, physics, UI, saves, co-op sync or vanilla integration.
- Refactors keep identical behavior. Small commits that merge cleanly.
- Areas with active bug-fix agents (Tech vault, Events, Chains, Contracts, Missions, heat stages, AI) come last, after their fixes land.

Steps:
1. Audit every game test into `tests/TIERS.md`.
2. Move logic to models with unit tests, biggest game time first.
3. Slim game tests to a smoke set per area.
4. `autotest-affected.sh` runs the smoke set; full suite stays for merge batches.
5. Report numbers before and after.
