# Pursuit plan (feat/pursuit)

Paused for the feature freeze. Spec: `spec/42-pursuit.md`, requirements 900 to 944.

Design decisions:
- Pacer API: `Pursuit.Pacer.MayStart(run, kind)` and `Pacer.Started(run, kind)`. Kinds come from `Pursuit.Events` (`Blockade`, `Outage`, `Hacker`, `RelayLockdown`); an unknown kind throws.
- Core hooks: `Run.Hooks.HeatGainsBlocked`, `HeatGainBlocked`, `StrikeWaveIntervalFactor`; `AddHeat(..., ignoreBlock)` for debug keys and test commands.
- Hunters hooks: `HunterSpawner.ScaleShips`, `LastSpawned`, `HunterOrders.Override`, `HunterOrders.StasisGoal`.
- The hacker is the first comms provider, so he owns his page (one shared choice domain per page).
- Tests of other areas that start heat stages set `pu_set Enabled false`.

Done:
- [x] Area `src/HotCargo/Pursuit/`, `mod/pursuit/`, tunables, state
- [x] Director: ramp, interval factor, fleet factor, pacer
- [x] Reporting scout, relay blockade, network outage (blue panel, +0), hacker (comms page, shared choice)
- [x] Unit tests `PursuitModelTests` (pass)
- [x] Headless tests pass: `pursuit-director`, `pursuit-scout`, `pursuit-blockade`, `pursuit-outage`
- [x] Spec 42; specs 09, 10, 17, 23, 28, 38 patched; BALANCE.md (modelled), TUNABLES.md
- [x] Merged origin/main (d00e1fe)

Left:
- [ ] `pursuit-hacker`: the page is right now; fix the test. The starting money covers the 30,000 price, so take money away before the "pay button off" check (line 18)
- [ ] Co-op test `pursuit` (written, not run)
- [ ] Screenshots with `tests/screens/pursuit.txt` (main slot was in use): scout marker, blue panel, hacker page
- [ ] `TunablesMenu/TunableTiming.cs` entries for the "next new system" and "next spawned ships" Pursuit fields (owned by another area; listed in TUNABLES.md)
- [ ] Delete PLAN.md, final PR body
