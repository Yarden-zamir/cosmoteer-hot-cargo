## 33. How to Play and the run log

Players of the mod likely know the base game. So the Hot Cargo mechanics join the game's own help, and they come first. Code: `src/HotCargo/Onboarding`. Rules: `mod/onboarding`. Tunables: the `Onboarding` section.

720. Each Hot Cargo mechanic has a vanilla codex page (`CodexPages`, `mod/onboarding/codex.rules`) in the "Hot Cargo" codex tab. The page ID starts with `hc_`, and the title starts with "Hot Cargo:". A page shows like a vanilla tutorial: a button in the "How to Play" list of the HUD with the new tutorial sound, and the page in the codex dialog. Page texts are short, in the Hot Cargo voice, and name no numbers that a tunable sets.
721. Pages and triggers. A page shows the first UI frame its trigger holds:

| Page | Trigger |
|---|---|
| `hc_run` The Run | 5 s of game time in a run, after the run intro |
| `hc_vault` The Tech Vault | a Tech Vault of a human ship is damaged |
| `hc_heat` Heat | the first heat stage starts |
| `hc_hunters` Hunters | a hunter is in the system |
| `hc_selling` Selling Tech | a comms window is open while the fleet holds tech |
| `hc_contracts` Contracts | a comms window is open and the system has contract offers |
| `hc_missions` New Jobs | a comms window is open and the system offers a Hot Cargo mission (a mission type that starts with `HotCargo`) |
| `hc_chains` Story Chains | a chain stage mission is in the log, or a comms window is open and the system offers one |
| `hc_events` Events | an event page waits for a choice |
| `hc_worlds` System Types | the current system has a Hot Cargo archetype (sector type `hc_*`) |
| `hc_wanted` Wanted and Hunted | the Wanted warning (requirement 470) |
| `hc_buyer` The Buyer | the Buyer spawned |
| `hc_settings` Settings | sector 2 |
| `hc_ship_tactics` Ship Tactics | `ShipAi.Enabled` and the stage before the last (Hunt) or later, or more human ships than human players |
| `hc_coop` Co-op | a multiplayer run with 2 or more human players |

722. The game keeps the page state in its settings (`Settings.CodexPageStates`), so each page shows once per player, across careers, and "Reset Tutorials" in the game settings shows them again. With "Show tutorials" off, no trigger runs and no page shows. Triggers only read state and change no sim state.
723. Priority. The Hot Cargo pages come first in the codex list (requirement 725), so the How to Play list checks them first, and their buttons move to the top of the list. Vanilla tutorials (the "Tutorials" tab) wait while a Hot Cargo run is active: during the first `VanillaTutorialDelaySeconds` (default 300 s) of run time, and while a Hot Cargo page is in the list unread. A waiting tutorial is not removed: it shows when the wait ends and its own condition holds. A tutorial that shows for now (`TempShowCondition`) keeps its update. Lore and tips never wait. Without a Hot Cargo run, the codex stays vanilla.
724. A trigger is C# code: `Onboarding.Hints.Add(pageId, condition)` in an area's `Register`. CONTRIBUTING.md says how an area adds a page.
725. `mod/onboarding/actions.rules` adds the Hot Cargo page list as the first base of the vanilla `CodexPages` list (`AddBase` with `Index = 0`). At the first frame of a run the mod checks that each trigger has a page, each `hc_` page has a trigger, no `hc_` page comes after a vanilla page, and each page string exists. A failure shows in the overlay.
726. The autotest `onboarding-hints` checks each trigger, that each page shows once (also after a load), the wait of the vanilla tutorials and its end, the button order, and the "Show tutorials" switch.
730. The run log. The run state saves a list of lines (`RunState.Log`): game time, sector index, kind and text. At most 400 lines; the oldest drop.
731. Lines: the run start, each new sector (name and system type), each heat stage and strike wave, each event result (title and result lines; a choice that leads to a follow-up page logs nothing), each contract accept (fee, withdrawn rivals) and informant sell-out, each chain stage result (story line, result lines, chain end), each tech sale and buy-back at a station, each Buyer delivery, each destroyed vault (its tech dropped as crates or burned), and the win or the loss. Areas add lines with `Onboarding.RunLog.Add` (or a named helper) from sim logic.
732. The vanilla career log window gets a third tab, "Hot Cargo run", with the Stolen Tech icon. It lists the lines newest first: sector, game time and text. Above the lines, the tab shows the past runs of this machine (requirement 1225).
733. Lines are written in sim logic on every machine, so co-op players see the same lines. The text is in the language of each machine. The log is not in the integrity hash.
734. The autotest `onboarding-log` checks each line kind, the tab rows and that the log loads with the save.

Screenshots (`tests/screens/onboarding.txt`): `docs/screenshots/onboarding-howtoplay.jpg`, `onboarding-page.jpg`, `onboarding-careerlog.jpg`.
