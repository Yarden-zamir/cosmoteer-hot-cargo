# Relays and stash plan (feat/relays)

Spec: `spec/43-relays-and-stash.md`, requirements 950 to 976. Paused for the feature freeze.

## Decisions

- A lockdown is marked on an unvisited neighbour system (map tag before the jump) and starts when the players enter it. It blocks relay jumps (`MPReadyFtlJumpInput` prefix); a blind jump always passes.
- Pacing: `Relays/Pacing.cs` (`IBigEventPacer`, `Pacing.Current`). Switch to the pursuit director's pacer in one line when it is on main.
- The pirate guards are a rules group (`mod/relays/guards.rules`), so the co-op scaling sets the count. The bribe page is a multiselect page (money plus up to 3 resources).
- Relays registers its comms provider before Diplomacy, so a guard page has only relay options.
- The stash reuses the shady trap ambush generator. Tech without vault room drops as a loose crate (`HunterGrab.Drop`).

## Steps

- [x] Area code: `src/HotCargo/Relays/`, `src/HotCargo/Stash/`, `mod/relays/`, `mod/stash/`
- [x] Pacing interface with a local limit and gap
- [x] Lockdown roll, map tag and tooltip, blocked relay jumps, spin-up window line
- [x] Closed relay (timer), toll (station comms), pirates (bribe page, fight)
- [x] Stash offer, found-first risk in the offer and the log, loot / empty / ambush
- [x] Unit tests (`RelaysModelTests`, `StashModelTests`) pass
- [x] Spec 43; spec 38 (650, 657), TUNABLES.md, BALANCE.md patched
- [x] Merge origin/main (d00e1fe)
- [ ] `relays-closed`: passed the block, timer and jump part; the blind part failed only on a test counter (fixed: `fa_jumpstarts >= 2`). Rerun.
- [ ] `relays-fee`: failed, the player ship was out of comms range after `rl_tostation` (`CanShipCallShip` false). Fixed `MoveNear` to stand next to the hull; not rerun yet.
- [ ] `stash-rolls`: passed up to the tech loot; failed because the start vaults are full. Fixed (loose crate, test frees a slot first); not rerun yet.
- [ ] `relays-map`, `relays-pirates`, co-op `mp:relays`: not run yet (slots were busy).
- [ ] Remove the `rl_page` diagnostics line once `relays-fee` passes (optional).
- [ ] Screenshots (`tests/screens/relays.txt`): map tag, bribe page, stash offer. Needs the main slot while the player's game is closed. Note: PC calls fail from a background shell after the turn ends, so take them in a live turn.
- [ ] Merge notes for the integrator: `TunablesMenu/TunableTiming.cs` entries (see TUNABLES.md rows Relays and Stash); I did not edit `TunablesMenu/`.
- [ ] Delete `PLAN.md`, merge origin/main, final push, PR body with screenshots
