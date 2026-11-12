## 18. Plan

The work is one batch. The order inside the batch follows the dependencies.

| Step | Work | Depends on |
|---|---|---|
| 0 | Toolchain: startup hook loader, Harmony, deploy and run scripts. **Done.** | — |
| 1 | Engine research: resources and trade, career and saves, missions and AI. **Done.** | 0 |
| 2 | Core: activation check, tunables with live reload, run state, heat model, overlay, debug keys, save and load. | 1 |
| 3 | Tech: resource, Tech Vault part, starting cargo, price patch, fleet count, loss rule. | 1 |
| 4 | Run: short map, sector tiers, reward scale, Buyer station, win rule. | 1 |
| 5 | Corporate response: hunter faction, stage spawns. | 2, 3 |
| 6 | Missions: escort, fleet assault (rules), shady offer with trap roll (C#). | 2, 5 |
| 7 | Hunter AI tactics. | 5 |
| 8 | Verification and a tuning pass. **Running.** Steps 2 to 7 are done. | all |

Parallel work: steps 3 and 4 touch separate files and can run at the same time as step 2. Steps 6 and 7 can run in parallel after step 5.
