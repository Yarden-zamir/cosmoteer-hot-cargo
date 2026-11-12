# UI work plan (branch feat/ui)

Spec: `spec/41-ui.md`, requirements 800 to 815.

## Done
- [x] Mission lists: kind icons, chain color and badge, distance or jumps, risk, risk sort, filter, per-player memory (800 to 805).
- [x] Icons for mission kinds and markers, vanilla sprites (805).
- [x] Win panel: "End run" primary, "Continue" secondary (806).
- [x] Keys 1 to 9 pick option rows; a digit toggles a multiselect row (814).
- [x] Toasts in `ShipAi/TacticsEditor.cs` and `Tech/LossTimer.cs` use `UxKit.CenterText`.
- [x] Multiselect options in the 11 events (810 to 813). Listening Post stays a mission panel order (see spec 41).
- [x] Build, unit tests, private-data check.

## Left
- [ ] Full autotest suite and co-op tests after the machine restart.
- [ ] Merge origin/main, push, PR screenshots.
