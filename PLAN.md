# Pursuit plan (feat/pursuit)

Spec: `spec/42-pursuit.md`, requirements 900 to 949.

- [ ] Area skeleton: `src/HotCargo/Pursuit/` (`PursuitHub.cs`, `PursuitModel.cs`, tunables, state), `mod/pursuit/`
- [ ] Spec 42 with requirements; patch specs 09, 10, 23 in place
- [ ] 1. Pursuit director: wave ramp by sector index, fleet value factor (±15 %), big event pacer API (`MayStart`, `Started`)
- [ ] 2. Reporting scout at the Warning stage: marker "REPORTING" with distance bar, heat +15 on escape
- [ ] 3. Relay blockade at Hunt or Strike, paced, at most 2 per run
- [ ] 4. Network outage: natural (at most 2), heat gains blocked, blue heat panel with timer, +0 popups
- [ ] 4b. Hacker hail (own `CommsHail` provider): paid 4 minute outage, shared choice in co-op
- [ ] Unit tests for `PursuitModel`
- [ ] Autotests `pursuit-*` per feature plus co-op
- [ ] Heat budget measured, `BALANCE.md` updated
- [ ] Screenshots: scout marker, blue panel, hacker page
- [ ] Merge origin/main, final push, PR body
