# Relays and stash plan (feat/relays)

Spec: `spec/43-relays-and-stash.md`, requirements 950 to 989.

- [ ] Area skeletons: `src/HotCargo/Relays/` (`RelaysHub.cs`, `RelaysModel.cs`, tunables, state), `src/HotCargo/Stash/` (`StashHub.cs`, `StashModel.cs`), `mod/relays/`, `mod/stash/`
- [ ] Pacing behind one small interface (own limit and gap until the pursuit director is on main)
- [ ] 1a. Lockdown roll per relay (rare, at most 2 per run), deterministic, saved
- [ ] 1b. Galaxy map label and tooltip before the jump (like HOT ZONE)
- [ ] 1c. Closed relay: the faction blocks the gate; blind jump is the way out
- [ ] 1d. Fee: pay through comms with the faction station, or blind jump
- [ ] 1e. Pirates guard the relay: bribe page (multiselect: money and resources), or fight (`Scaling.EnemyScale.Count`)
- [ ] 2a. Stash offer like a shady mission, registered like other mission types
- [ ] 2b. Found-first risk grows with time; shown in the offer and the mission log
- [ ] 2c. On arrival: loot (tech or money), empty, or ambush
- [ ] Unit tests for `RelaysModel` and `StashModel`
- [ ] Autotests `relays-*` (each kind with each way out, the map label), `stash-*` (forced rolls), co-op
- [ ] Spec 43, patch specs 11, 38 in place; TUNABLES.md, BALANCE.md
- [ ] Screenshots: map label, bribe page, stash offer
- [ ] Merge origin/main, final push, PR body
