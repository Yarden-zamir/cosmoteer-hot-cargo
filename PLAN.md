# Live tunables (branch feat/live-tunables)

Goal: the coordinator changes tunables in the player's running game, also as the co-op host. Spec 850 to 869 (850 to 854 used).

Paused for the feature freeze. origin/main d00e1fe is merged.

## Done
- Co-op: a change of the host's `tunables.rules` goes out as the `HotCargoTunables` input (spec 850). Clients ignore their file.
- `scripts/tune.sh` writes `tune.request`; the mod applies it as one settings window change and writes `tune.result` (spec 853). Array and nested paths work.
- The open settings window shows file changes (spec 851). Clients see the host's values read-only.
- `Run.Hooks.TunablesApplied`. A higher station fuel floor tops up live stations and stations in stasis (spec 852).
- Live pass: jump heat cuts, carry-over, fuel floor, event chances, waiting hunter groups (spec 854).
- Skill `hot-cargo-tune`, CLAUDE.md skill list, spec 52, 53, 60, 504, 506 patched, TUNABLES.md "Why some values stay delayed".
- Tests passed: `tunablesmenu-file` (single player, after the merge of main), `tunablesmenu-live` (before the hunter and fuel changes).

## Left
- Run `mp:tunablesmenu-file` (new co-op test, not run to the end yet) and `mp:tunablesmenu`. Fix what fails.
- Run `tunablesmenu-live` again, then `scripts/autotest-affected.sh` (Core changed, so it runs the full suite; use the shared slots).
- Merge origin/main again, delete this file, push, mark the PR checklist done.
