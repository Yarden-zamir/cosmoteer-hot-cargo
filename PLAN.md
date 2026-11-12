# Live tunables (branch feat/live-tunables)

Goal: the coordinator changes tunables in the player's running game, also as the co-op host. Spec 850 to 869.

## Done
- Co-op: a change of the host's `tunables.rules` goes out as the `HotCargoTunables` input (spec 850). Clients ignore their file.
- `scripts/tune.sh` writes `tune.request`; the mod applies it as one settings window change and writes `tune.result` (spec 853). Array and nested paths work.
- The open settings window shows file changes (spec 851). Clients see the host's values read-only.
- `Run.Hooks.TunablesApplied`; a higher station fuel floor tops up live stations (spec 852).
- Live pass part 1: jump heat cuts, carry-over and fuel floor apply now.
- Skill `hot-cargo-tune`, CLAUDE.md skill list, spec 52, 53, 60, 504, 506 patched.

## Left
- Live pass part 2: check the priority values (hunters, rewards, fees, events) for cheap live changes.
- Run tests: `tunablesmenu-file`, `mp:tunablesmenu-file`, `tunablesmenu-live`, `mp:tunablesmenu`, then `scripts/autotest-affected.sh`.
- Merge origin/main, push, update the draft PR.
