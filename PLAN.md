# Smaller systems (spec 105): one SystemScale

Player request (playtest 2026-11-13): make the systems smaller than they are now.

Decisions
- `mod/contracts/system_scale.rules` holds `Scale` (0.5), `Area` (Scale squared) and `SunClearance` (sun heat radius from the vanilla sun doodad, plus a hull margin).
- Each gameplay distance is a formula: vanilla value × Scale. Each scattered count: vanilla count × Area, with a minimum. Sun gaps: max(vanilla gap × Scale, SunClearance).
- Scenery (suns, planets, nebulas, fog) and hull clearances under 1,000 keep their values. Tactical spawn distances near the players keep their values.
- Trade stations move in too, so the gates stay outside the station ring.

Steps
1. Scale file, formula test on the gate entry, ring check command (`worlds rings`).
2. Vanilla overrides in `mod/contracts/actions.rules` as formulas.
3. Hot Cargo sites (mechanics, events, archetypes, chains, contract sites, Buyer).
4. Constraints: heat, military fit, gate ring, escort route, Buyer bounties.
5. Balance notes (BALANCE.md, docs/balance.html).
6. Tests (worlds-fit, missions, contracts, chains, events, journey; co-op on the PC).
7. Spec 105 and other spec lines.
