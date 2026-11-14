## 51. The Pursuit

The heat clock is the core resource. The heat stages still start the waves (section 10). The Pursuit is a director on top: it makes waves weaker and rarer early and ramps them toward the Buyer, and it paces big events so that they do not pile up. Code: `src/HotCargo/Pursuit`. Strings: `mod/pursuit`. Numbers: the `Pursuit` tunables section. Pure math: `Pursuit/PursuitModel.cs`.

### Director

900. **Switch.** `Enabled` (default on) turns the ramp, the fleet factor, the reporting scout, the blockade, natural outages and the hacker on or off. The pacer (requirement 905) works in both states, so other areas can still ask it.
901. **Ramp.** Each stage response sends its ships (requirement 190) × a ramp factor, before the difficulty factors (requirement 682). The factor is `EarlyShipFactor` (0.75) in sector 1 and rises in a straight line with the sector index to `LateShipFactor` (1) in the last sector of the run. The repeat Strike wave interval (requirement 30) × `EarlyIntervalFactor` (1.5) in sector 1, falling in a straight line to `LateIntervalFactor` (1). Progress is the hunter wave progress (`WaveModel.Progress`).
902. **Fleet factor.** The ships of a stage response also × a fleet factor: 1 + `FleetFactorPerDoubling` (0.15) × log2(fleet value per player ÷ reference), clamped to 1 ± `FleetFactorMax` (0.15). Fleet value is the vanilla `Ship.BasePhysicalValue` (damage counts) summed over the human ships in the system, ÷ the human player count (co-op already scales the count, requirement 334). Reference = `FleetReferenceShare` (1) × the vanilla ship value maximum (`TierValueMaximums`) of the hunter base tier (requirement 305).
903. **Rounding.** Ships × ramp × fleet factor: the whole part always spawns, the fractional part is the chance of one more ship (`sim.Rand`). A stage that sends 1 or more ships never drops to 0. Co-op scaling (requirement 334) and the live cap apply after this, in the Hunters area.
904. **Determinism.** The factors read sim state only: the sector index, the run tunables and the human ships. The roll uses `sim.Rand` inside the stage hook.

### Pacer

905. **Pacer API.** Big events (blockade, outage, hacker here; relay lockdowns and others in other areas) ask `Pacer.MayStart(run, kind)` before they start and call `Pacer.Started(run, kind)` when they start. A kind may start when it has fewer than its `MaxPerRun` starts in the run, at least its `MinGapSeconds` of run game time since its last start, and at least `MinEventGapSeconds` (300) since the last start of any kind. Both calls are sim logic (sim tick, Run hooks, MP inputs).
906. **Kinds.** `Events` lists the kinds with their limits. Defaults: `Blockade` (2 per run, 1,200 s gap), `Outage` (2, 1,200 s), `Hacker` (1, no gap), `RelayLockdown` (2, 1,200 s). A kind that is not in the list is an error: the call throws, so a missing entry shows at once. Each kind is in the list once (tunables validation).
907. **Save.** The run state saves every start (kind, run game seconds, sector index). The history lasts the whole run.

### Reporting scout

910. **Scout roll.** When the `ScoutStage` stage (Warning) starts, the first ship of its first group rolls `ScoutChance` (0.5) with `sim.Rand`. On success it becomes a reporting scout: it flies to the relay nearest to it (`FtlGate.GetGates`) and does not fight.
911. **Tells.** A scout's ship marker shows "REPORTING", a bar of how far it got toward the relay and its distance. A chat line says that it runs for the relay. In stasis it keeps flying to the relay.
912. **Report.** A scout within `ScoutRelayRadius` (400) of its relay reports: it jumps out (FTL effect, then removal, no wreck), heat + `ScoutReportHeat` (15), a center text and a chat line "Scout reported your position: heat +15". During an outage the gain is blocked and the text says +0.
913. **Stopped.** A scout that is destroyed, becomes a wreck, surrenders, makes a truce, loses the hunter tag (it agreed to leave, requirement 562) or jumps out while it flees (requirement 195) reports nothing. A jump of the players to another system ends every scout.
914. **Budget.** At most one scout per Warning start. Expected heat per sector is 15 × 0.5 × the share of scouts that escape: about +3 to +4 (BALANCE.md).

### Relay blockade

915. **Blockade roll.** When a stage of `BlockadeStages` (Hunt, Strike) starts, no blockade is active and the pacer allows a `Blockade`, the stage rolls `BlockadeChance` (0.5) with `sim.Rand`. On success the ships of the stage's first group park at the relay that the players need.
916. **Needed relay.** The relay toward the players' confirmed jump destination (`GameRoot.DestinationMapNode`, first node of the path), else the relay to an unvisited system nearest to the fleet, else the nearest relay. Each blockade ship parks at its own spot on a circle of `BlockadeParkRadius` (600) around the relay. In stasis it flies to its spot. A center text and a chat line name the relay.
917. **Ways out.** A blockade ship at the relay (within 2 × `BlockadeParkRadius` of it) chases like any hunter as soon as a human ship comes within `BlockadeAggroRange` (2,200, edge to edge). Any blockade ship chases when it takes damage. On the way to its spot it ignores the players it spawned near (requirement 32). So the players can fight through, lure ships away from the relay, or jump blind (requirement 651). The parked ships make the relay a watched relay (requirement 650).
918. **End.** The blockade ends when no ship parks any more, or when the players leave the system.
919. **Limit.** With the defaults, at most 2 blockades per run and 1,200 s of game time between them.

### Network outage

920. **Natural outage.** On the first visit of a system with sector index `OutageMinSector` (2) or more and below the last sector (the Buyer), the system rolls `OutageChance` (0.5). The roll is seeded by the galaxy seed (`GameInit.RandSeed`) and the sector index, so every machine gets the same roll. On success an outage is due `OutageDelayMin` to `OutageDelayMax` (90 to 480) game seconds after arrival. When it is due, it starts if the pacer allows an `Outage`, else it drops. With the defaults a run has 6 rolls, at most 2 outages and 1.9 on average. A switch to another system drops an outage that did not start.
921. **Effect.** For `OutageSeconds` (120) of game time every heat gain is blocked: time heat, fame heat (missions and kills), the contract trail, flat contract heat, informants, event outcomes and scout reports (`Run.AddHeat` drops every positive amount through `Run.Hooks.HeatGainsBlocked`). Losses still apply (jumps, hand-overs, event outcomes). The outage lasts across a jump. Debug keys and test commands are not blocked. Notoriety (requirement 19) does not grow, because it sums real heat gains.
922. **"+0".** A blocked gain of 0.5 heat or more shows "+0 heat: network down" on the heat panel for a moment. A scout report says +0. Known limit: event outcome texts (`Events/`) still name their nominal heat change; revisit when the event dialog can read the outage.
923. **Heat panel.** During an outage the heat bar is blue and the panel shows "NETWORK DOWN m:ss" with the time left and a heat rate of +0. A center text and a chat line say when it starts ("NETWORK DOWN for 2:00: no heat gains") and when it ends ("Corporate network back up").
924. **Save.** The run state saves the outage end time and a due outage.
925. **Tests.** `pursuit-outage`: blocked gains (time, fame, flat), losses, the end, a natural outage refused by the pacer.

### Hacker

930. **Hacker roll.** On the first visit of a system with sector index `HackerMinSector` (2) or more and below the last sector, while the pacer allows a `Hacker`, the system rolls `HackerChance` (0.25), seeded like requirement 920. On success a hacker comes `HackerDelayMin` to `HackerDelayMax` (60 to 300) game seconds after arrival, if the pacer still allows it: a small neutral proxy ship (`mod/pursuit/spawns.rules`, tag `hc_hacker`) spawns near the fleet and stays at its spot.
931. **Offer.** The hacker offers an outage of `HackerOutageSeconds` (240) for `HackerPriceShare` (0.5) × the vanilla ship value maximum of the hunter base tier, rounded to `HackerPriceRoundTo` (1,000). The paid outage does not count toward the natural limit of the pacer. An outage that runs gets longer, up to the paid length from now.
932. **Stay.** He hails a human ship like a vanilla AI hail (`CareerSimModeManager.AIHailPlayer`) as soon as one can call back. He leaves `HackerStaySeconds` (180) after his hail, or after his arrival if he never could hail, and 8 game seconds after an answer. He jumps out with the FTL effect.
933. **Comms page.** The hacker is a provider on the comms hook (`Ui/CommsHail.cs`, requirement 560). His page shows the offer, the price, the time until he leaves, a pay button and a decline button. The pay button is off without the money when the game allows no debt (Ironman, vanilla `AllowDebt`), like the diplomacy pages (`Diplomacy.Situation.CanPay`).
934. **Co-op.** Both buttons are shared choices of the domain `pursuit` (requirement 600 and later), page serial the ship ID: co-op players see hovers and can vote. The sim checks the choice again when it applies (hacker present, chooser's money); the chooser pays.
935. **Tests.** `pursuit-hacker` (price, buttons, pay, outage length, natural count, leave) and the co-op step `pursuit.coop` in the session `coop-combat` (a client's payment).

### Save, determinism and tests

940. **Save.** `RunState.Pursuit` holds the pacer history, the scouts, the blockade, the outage, the due rolls, the hacker and the ships that jump out. A system switch clears the scouts, the blockade and the ships that jump out; the outage end time stays.
941. **Determinism.** All changes run in the sim tick, Run hooks, AI ticks and the shared choice input. Rolls use `sim.Rand` or a `FastRand` seeded by the galaxy seed and the sector index. Lists keep a stable order (relays by destination ID, hunters in spawn order). Texts, the marker and the panel change no sim state.
942. **Unit tests.** `PursuitModelTests`: the fleet factor band, count rounding, the pacer rules, the outage odds, the scout bar, the clock and the hacker price.
943. **Headless tests.** `pursuit-director` (ramp, interval factor, fleet factor band, ship counts, pacer), `pursuit-scout` (escape with +15, a destroyed scout), `pursuit-blockade` (the ships head for the relay and stay out of the fight, chase on approach, pacer), `pursuit-outage`, `pursuit-hacker`. Co-op: the step `pursuit.coop` in the session `coop-combat` (scout report and the client's hacker payment, no desync). Autotest games start with the Pursuit off: `Enabled` defaults to false while an autotest runs, also after a tunables file change or a settings request. So the hunter counts and behavior of other areas stay exact. The Pursuit tests, the step `pursuit.coop` and the Pursuit screenshots turn it on with `pu_set Enabled true`.
944. **Hooks used.** Core: `Run.Hooks.HeatGainsBlocked`, `Run.Hooks.HeatGainBlocked`, `Run.Hooks.StrikeWaveIntervalFactor`. Hunters: `HunterSpawner.ScaleShips`, `HunterSpawner.LastSpawned`, `HunterOrders.Override` (the hunter module `HcOverride`, above every hunter tactic except fall back, call and flee), `HunterOrders.StasisGoal`. The Pursuit registers after the Hunters area, so its stage hook sees the ships that the stage spawned.
