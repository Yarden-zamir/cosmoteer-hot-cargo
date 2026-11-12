# Relays and stash plan (feat/relays)

Spec: `spec/43-relays-and-stash.md`, requirements 950 to 989.

- [x] Area skeletons: `src/HotCargo/Relays/` (`RelaysHub.cs`, `RelaysModel.cs`, tunables, state), `src/HotCargo/Stash/` (`StashHub.cs`, `StashModel.cs`), `mod/relays/`, `mod/stash/`
- [x] Pacing behind one small interface (own limit and gap until the pursuit director is on main)
- [x] 1a. Lockdown roll per relay (rare, at most 2 per run), deterministic, saved
- [x] 1b. Galaxy map label and tooltip before the jump (like HOT ZONE)
- [x] 1c. Closed relay: the faction blocks the gate; blind jump is the way out
- [x] 1d. Fee: pay through comms with the faction station, or blind jump
- [x] 1e. Pirates guard the relay: bribe page (multiselect: money and resources), or fight (`Scaling.EnemyScale.Count`)
- [x] 2a. Stash offer like a shady mission, registered like other mission types
- [x] 2b. Found-first risk grows with time; shown in the offer and the mission log
- [x] 2c. On arrival: loot (tech or money), empty, or ambush
- [x] Unit tests for `RelaysModel` and `StashModel`
- [ ] Autotests `relays-*` (each kind with each way out, the map label), `stash-*` (forced rolls), co-op
- [x] Spec 43, patch specs 11, 38 in place; TUNABLES.md, BALANCE.md
- [ ] Screenshots: map label, bribe page, stash offer
- [ ] Merge origin/main, final push, PR body
