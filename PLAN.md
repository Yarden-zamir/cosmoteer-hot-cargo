# Remove the run-start event "Clean Getaway?"

Decisions:
- Remove the `getaway` event, the `Start` trigger, `StartDelaySeconds` and `EventsState.StartDueSeconds`. Signals and stations no longer wait for a start event.
- `fake_codes`: keep the Corporate Checkpoint choice. The Fence's Ledger chain page ("Clean transponder codes") already sets the flag, so no new source is needed.

Steps:
1. Code and rules.
2. Autotests (single-player and co-op).
3. Spec 69, EVENTS.md, PLAYTEST.md, TUNABLES.md, CHAINS.md.
4. Affected autotests, merge origin/main, push.
