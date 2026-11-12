# UI work plan (branch feat/ui)

Status: PAUSED by the feature freeze (only fixes merge until the game is stable).
Spec: `spec/41-ui.md`, requirements 800 to 815.

## Done
- [x] Mission lists: kind icons, chain color and badge, distance or jumps, risk, risk sort, filter, per-player memory (800 to 805).
- [x] Icons for mission kinds and markers, vanilla sprites (805).
- [x] Win panel: "End run" primary, "Continue" secondary (806).
- [x] Mission descriptions in 2 short lines (807). Split out to branch `fix/mission-text` (PR 18), merged back here.
- [x] Keys 1 to 9 pick option rows; a digit toggles a multiselect row (814).
- [x] Toasts in `ShipAi/TacticsEditor.cs` and `Tech/LossTimer.cs` use `UxKit.CenterText`.
- [x] Multiselect options in the 11 events (810 to 813). Listening Post stays a mission panel order (see spec 41).
- [x] Tests passed on 2026-11-12: ux-missions, ux-more, events-options, chains-content, chains-walk, mechanics-offers,
      missions-offers, missions-shady, worlds-pass, journey-run, co-op events-options and wanted. Unit tests pass.
- [x] Test runner fix: the slot limit counts only slot locks and clears dead locks (`scripts/autotest-lib.sh`).

## Left after the freeze
- [ ] Merge origin/main again (after PR 18 lands).
- [ ] Run `scripts/autotest-affected.sh` (full suite: shared UI and scripts changed). Not run yet as a whole.
- [ ] Delete this file, then mark PR 7 ready.
