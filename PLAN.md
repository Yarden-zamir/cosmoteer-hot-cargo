# Starter vaults: plan

Decisions:
- No run-start placement. `StartingVault.OnNewRun` only fills the vaults that the ship design already has (share per player as before).
- New-game check: Harmony postfix on `CareerModeRules.IsShipDesignLegal`. A ship needs Tech Vaults for `StartingTechUnits`. The vanilla "invalid ship" dialog gets a clear Hot Cargo line.
- Design tool: autotest command `vaultdesigns <src> <out>` (main slot, needs graphics to save). It loads each design into the sim, places vaults with the old placement code, validates, saves with `SaveDesignAs`, and writes a report.
- Presets: generated from vanilla `Data/builtin_ships/Starter/*.ship.png`, shipped in `mod/tech/starters/`, rules `Replace` of `StarterShips`.
- `scripts/vault-saved-ships.sh`: backup, `vaultdesigns` in place, report.
- New careers unlock `StartingUnlockedTechs` (default FTL drive `cosmoteer.hyperdrive_small`).

Steps:
1. Check + fill-only start + FTL unlock, builds.
2. Design tool command.
3. Generate presets, rules replace, screenshots.
4. Tests: rewrite `tech-start`, `tech-outside`, new `journey-newgame-check`.
5. Specs 07, 39 (new 1010+).
6. `scripts/vault-saved-ships.sh`, run on main slot.
