# Co-op listen-in (spec 47, requirements 1131 to 1149)

Player request: in co-op, other players listen in on a running hail and vote on its choices.

## Design

- Source of "a hail runs": the vanilla sim set `CareerSimModeManager.OpenComms` (callee -> caller ships). Every machine has it, because `MPOpenCommsInput` and `MPCloseCommsInput` change it. No new network message.
- Listen list (UI only): above the local comms button, one button per open comms of another player whose callee shows a dialogue tab. Text "Listen in: <player> ↔ <ship>". Single player: no list.
- Listen window: a real vanilla `CommWindow(game, speakerShip, callee)` in listen mode. Harmony prefixes skip its MP inputs (open/close comms, pause), show only the dialogue tab, and close it a few seconds after the speaker's comms close. The page is the same `GetDialogueExchange(speakerShip)` that the speaker sees, so it is live.
- Read-only: vanilla buttons, hail lines and multiselect toggle rows are disabled for a listener. Shared choice buttons and action rows stay active: a click is a vote.
- Votes reuse section 32. Vote mode: a listener vote is a normal vote (same rules, same input). First pick mode: a vote is advisory: it shows on every page, never decides; the speaker picks. A pick clears the advisory votes of the page.
- Hovers of listeners reach the speaker through the existing presence RPC.
- Determinism: listen state is UI only. Votes go through `HotCargoSharedChoice`.

## Steps

1. PLAN.md, draft PR with the design.
2. SharedChoices: advisory votes outside vote mode; panel shows votes in both modes; panel listener mode. Unit tests.
3. Listen list and listen window (CommWindow patches), CommsHail read-only rules.
4. Autotest commands, co-op steps (event page carried by a ship, hunter demand page).
5. Spec 47, spec 32 and 37/40 patches, docs.
