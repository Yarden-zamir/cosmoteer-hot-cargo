# Plan: start jobs (spec 48) — PR 1 of 2 (PR 2: run record)

Decisions
- New area `StartJobs`. Jobs are tunables rows (like difficulty rows). Strings give name and text.
- Persistent rules read live: pressure factors combine in `Difficulty.Pressure`, `Sectors` in one run-length helper, `BuyerPrice` in `Buyer.PriceFactor`.
- One-shot rules at the pick: `Tech` (≤ 0, through `Vaults.Remove`) and `Heat` (`AddHeat`).
- The choice is in the run intro panel (no second popup), as a shared choice domain `startjobs`.

Steps
1. Spec 48 + plan.
2. Model + unit tests (draw, serial, run length).
3. Area, tunables, state, domain, apply, validation.
4. Run-length helper: every `RunSectorCount` reader.
5. Panel (intro + jobs, SharedChoicePanel), tooltip line, log line, hc_jobs page.
6. Autotest `startjobs-pick`, co-op step `startjobs.pick`.
7. Screenshot, docs (UX.md H4, TUNABLES.md, spec 33/17), PR text.
