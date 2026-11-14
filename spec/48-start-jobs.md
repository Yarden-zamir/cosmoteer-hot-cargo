## 48. Start jobs

Each run starts with a choice of jobs. A job changes the run with a few clear rules, so two runs do not play the same. Code: `src/HotCargo/StartJobs`. Pure logic: `StartJobs/StartJobsModel.cs`. Tunables: the `StartJobs` section.

1200. **Offer.** At the start of a new run, the run offers `OfferCount` (3) jobs from the `Jobs` list. The jobs with `Always` come first (default: "The Contract", the standard run). A draw from the galaxy seed (`GameInit.RandSeed`) fills the other places from the other jobs, so a new galaxy gives a new offer and every co-op machine offers the same jobs. The run state saves that a choice waits and the ID of the chosen job. A save from before this feature has no waiting choice, and its run has no job. With `Enabled` off, a new run offers no jobs and plays as before.
1201. **Rules.** A job has a name and a short text (strings `HotCargoStartJobs/<Id>Name` and `<Id>Text`) and these rules in the tunables:
    - `Tech`: tech units that leave the vaults at the pick. 0 or less: the run builds no vault (requirement 1014), so a job cannot add units.
    - `Heat`: heat at the pick, like event heat (requirement 72).
    - `Sectors`: added to `RunSectorCount` for this run, at least sector 1. The Buyer waits in that sector. Every reader of the run length uses the sum: the Buyer spawn (requirement 1), the heat panel, the intro, the run log, the end screens, the event needs (requirement 83), the hacker and outage sectors, the relay marks and the wave progress.
    - `BuyerPrice`: × the Buyer unit price (requirement 7). The Buyer sale and the score follow it.
    - `HeatRate`, `HunterShips`, `HunterTiers`, `ContractFees`, `TechValue`: pressure factors like a difficulty row (requirement 680). They combine with the difficulty rows, so every reader of the pressure follows (requirements 681 to 683).

    `Sectors`, `BuyerPrice` and the pressure factors apply while the run lasts and follow a live tunables change. `Tech` and `Heat` apply once, at the pick.
1202. **A clear deal.** Each job button lists all the rules of the job in plain text, made from its tunables values: one line for each rule that changes something, or "No changes." No rule is hidden, and no rule rolls a chance.
1203. **Panel.** The choice is in the run intro panel (UX H4): the intro text, then one button for each job. The panel has no close button and stays until the players pick. With `Ux.RunIntro` off, the panel shows only the jobs. A loaded run with a waiting choice shows the panel again, without the intro text. Event windows wait while the panel shows (requirement 77). The test command `closepanel` closes it and the choice keeps waiting.
1204. **Co-op.** The choice is a shared choice (section 32, domain `startjobs`): hover markers, first pick or vote, and the host's "Decide now". The page serial comes from the offered job IDs, so a tunables change that changes the offer drops the open votes.
1205. **After the pick.** The panel closes on every machine. A message at the top of the screen and a chat line name the job. The run log gets a line with the job and its rules (requirement 731). Removed tech leaves the vaults, so the vault fill shows it. The heat panel tooltip (requirement 684) names the job and includes its factors. The How to Play page `hc_jobs` shows (requirement 721).
1206. **Default jobs.**

| ID | Name | Rules | Reads as |
|---|---|---|---|
| `contract` | The Contract | none (`Always`) | The standard run. |
| `prototype` | Prototype Cores | Tech −4, tech value ×1.75 | Fewer units, each worth more. Less heat from the cargo, but less to lose. |
| `deep_space` | Deep Space Buyer | Sectors +2, Buyer price ×1.4 | A longer run for a better price. |
| `rush` | Rush Order | Sectors −2, Buyer price ×0.7 | A short run for a lower price. |
| `loud_exit` | Loud Exit | Heat +20, tech value ×1.25 | The theft made the news: hunted from the start, but the tech sells high. |
| `grudge` | Corporate Grudge | Hunter tiers +1, Buyer price ×1.3 | Stronger hunters, a better price. |
| `inside_man` | Inside Man | Heat rate ×0.8, Buyer price ×0.8 | Slower heat, and the inside man takes a cut. |

1207. **Validation.** A tunables error (requirement 53): two jobs with the same ID, a job with `Tech` above 0, `BuyerPrice` or `HunterShips` or `TechValue` at 0 or less, `HeatRate` or `ContractFees` below 0, an `OfferCount` below 1, or more `Always` jobs than `OfferCount`. An `OfferCount` above the number of jobs offers every job. A job without its strings is an error in the overlay at the first frame of a run.
1208. **Tests.** Unit tests `StartJobsModelTests`: the draw, the serial and the run length. Autotest `startjobs-pick`: the offer, the panel and its button texts, each rule of a pick (tech, heat, Buyer sector, Buyer price, pressure), the log line, the tooltip, the save and load of a waiting choice and of a pick, and `Enabled` off. Co-op step `startjobs.pick` in `coop-core`: the client's hover, a pick on both machines and no desync. Screenshot: `docs/screenshots/startjobs-panel.jpg` (`tests/screens/startjobs.txt`).
