# Plan: hunters never join the players

Playtest 2026-11-13: hunters became friendly and joined the fleet. Fix 4d007b6 is on main without a test.

1. Test `hunters-takeover`: the players take over a live hunter (test command), a new stage spawns. Every new hunter has a non-human index and is hostile. It fails on 4d007b6^ and passes on main.
2. Hunters in stasis: a new wave takes the index of the hunters in stasis too, so all hunters stay one side.
3. Invariants (spec 1002, 1003): a new hunter is hostile to every human ship; hunters that the players do not own share one index.
4. Audit of other paths (diplomacy, truces, ally peace, capture, stasis, save and load) in the PR text.
