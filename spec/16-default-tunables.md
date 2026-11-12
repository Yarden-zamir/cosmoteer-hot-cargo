## 16. Default tunables

| Tunable | Default | Requirement |
|---|---|---|
| `RunSectorCount` | 8 | 1 |
| `FinalSectorTier` | 12 (rules value in `mod/journey/galaxy.rules`, restart needed) | 3 |
| `RewardScale` | 1.0 | 4 |
| `Journey.TechCostScale` | 2.5 | 301 |
| `BuyerPriceFactor` | 4 | 7 |
| `StartingTechUnits` | 8 | 17 |
| `VaultDamagePerUnitLost` | 0.5 | 65, 442 |
| `LossGraceSeconds` | 180 s | 12, 443 |
| `TechMarkers` | on | 444 |
| `VaultTechLossChance` | 0.1 | 445 |
| `HunterGrabChance`, `HunterGrabRange`, `HunterGrabReach` | 0.8, 3,000, 40 | 446 |
| `VaultSunHeatFactor` | 0.25 | 448 |
| `TechShipFraction` | 0.25 | 19 |
| `TechShipFractionPerGameHour` | 0.10 | 19 |
| `TechShipFractionPer100Notoriety` | 0.05 | 19 |
| `NotorietyStep` | 5 | 19 |
| `HeatPerGameMinute` | 1.2 | 24 |
| `HeatPerStandardMission` | 1.5 | 25 |
| `HeatMultiplierByUnits` | [0.25, 0.4, 0.52, 0.64, 0.76, 0.88, 1.0, 1.1, 1.2] | 26 |
| `HeatCarryOverFraction` (a switch without a jump) | 0.25 | 28 |
| `HeatStages` | see section 10 | 30 |
| `StrikeWaveIntervalSeconds` | 180 s | 30 |
| `SpawnDistanceMin`, `SpawnDistanceMax` | 2000, 3000 | 32 |
| `ShadyRewardFactor` | 2.0 | 38 |
| `ShadyTrapChanceBase` | 0.2 | 39 |
| `ShadyTrapChancePerHeat` | 0.006 | 39 |
| `ShadyTrapSurvivalReward` | 0.5 | 40 |
| `EscortHeatChange` | −8 | 36 |
| `EscortRouteShare`, `EscortAmbushAt`, `EscortAmbushLead`, `EscortRaiderFocus` | 0.65, 0.3, 1800, 20 | 36 |
| `Events.ArrivalEventChance`, `ArrivalDelaySeconds` | 0.6, 6 s | 69 |
| `Events.StationEventChance`, `StationTriggerDistance` | 0.5, 1500 | 69 |
| `Events.SignalTriggerDistance` | 900 | 69 |
| `Events.CourierChance` | 0.5 | 85 |
| `Events.MaxUnavoidablePerRun` | 1 | 87 |
| `Events.WantedWarningMinStage`, `WantedEventChance` | 1, 0.7 | 470, 471 |
| `Events.ResearchRate`, `ResearchMinRate`, `ResearchMaxUnits`, `ResearchMaxTechs` | 1.5, 1.2, 2, 3 | 760 |
| `Events.ResearchSalePremium`, `ResearchSaleCap`, `ResearchWeightScale` | 1.3, 1.5, 1 | 761, 762 |
| `Events.AbandonedEventChance`, `AbandonedTriggerDistance` | 0.6, 800 | 764 |
| `Events.RelicEnabled`, `RelicMinSector`, `RelicMessageDistance`, `RelicBoardDistance` | true, 2, 3000, 900 | 765 |
| `Events.CooldownSeconds`, `MaxEventsPerSystem` | 120 s, 2 | 70 |
| `Events.MoneyScale`, `FameScale`, `HeatScale` | 1, 1, 1 | 73 |
| `Contracts.TrailFreeContracts`, `TrailHeatPerContractMinute` | 3, 1 | 101 |
| `Contracts.FeeChance`, `FeeMin`, `FeeMax`, `PaidBonusFactor` | 0.5, 0.3, 0.8, 2 | 102 |
| `Contracts.RivalPairChance` | 0.2 | 103 |
| `Contracts.InformantShare`, `InformantChanceMin`, `InformantChanceMax` | 0.75, 0.05, 0.15 | 104 |
| `Contracts.InformantDelayMin`, `InformantDelayMax`, `InformantHeat`, `InformantShips` | 45 s, 150 s, 15, 2 | 104 |
| `Chains.StationChance`, `MaxNewChainsPerSystem` | 0.6, 2 | 160 |
| `Chains.PageTriggerDistance`, `LaterDelaySeconds` | 1500, 8 s | 165 |
| `Chains.StoryLines` | 2 | 170 |
| `KiteRangeFraction` | 0.9 | 44 |
| `PincerAngleDegrees` | 120° | 45 |
| `FallBackHealthFraction` | 0.35 | 47 |
| `Hunters.Stages` Strike `EarlyScale`, `EarlyTierReduction` | 0.67, 1 | 190 |
| `Hunters.Stages` Strike `Waves`, `WaveGapSeconds` | 3, 45 s | 191 |
| `Hunters.MinFuel` | 60 | 192 |
| `FleeHealthFraction`, `FleeChance` | 0.25, 0.5 | 193 |
| `FleeDistance`, `FleeJumpSeconds`, `FleeRecoverFraction` | 1500, 25 s, 0.6 | 195 |
| `CallSeconds`, `CallRange`, `CallCooldownSeconds` | 6 s, 6000, 60 s | 196, 197 |
| `CallAssistSeconds` | 60 s | 198 |
| `CallBlockedInNebulas`, `CallStormStrength` | [ion_storm, electron], 0.5 | 199 |
| `FocusWeakFactor` | 1.5 | 200 |
| `RegroupFirepowerRatio`, `RegroupRadius`, `RegroupMinGroup`, `RegroupMaxSeconds` | 0.75, 1200, 2, 25 s | 201 |
| `PersonalitiesEnabled`, `PersonalityThreshold`, `ProfileRefreshSeconds` | on, 0.45, 10 s | 360, 362, 363 |
| `Personalities` | see section 31 | 361 |
| `Profile` (build score ends) | see `ProfileScaleTunables` | 362 |
| `TeamRadius`, `TeamFlankDegrees` | 2500, 90° | 364, 365 |
| `CoverSearchRange`, `CoverEngageRange`, `CoverPeekSeconds`, `CoverHideSeconds` | 2000, 1500, 8 s, 6 s | 366 |
| `BrawlRangeFraction` | 0.5 | 365, 367 |
| `Mechanics.<Mission>Chance` | 0.15 to 0.25 | 131 |
| `Mechanics.Siege`, `Guard`, `Scan` (hold-out profiles) | see MISSIONS.md | 132, 137, 140 |
| `Mechanics.DeliveryRadius` | 800 | 134 |
| `Mechanics.SurveyScanSeconds` | 15 s | 136 |
| `Mechanics.ScanRange`, `IntensivePayBonus`, `MaxIntensivePerWave` | 3500, 0.25, 2 | 140 |
| Archetype weights | rules values in `mod/worlds/archetypes.rules`, restart needed | 220 |
| `Worlds.CurrentFlowSpeed`, `CurrentAccel`, `CurrentArrows` | 45, 9, on | 221 |
| `Worlds.PassHoldSeconds`, `PassLostSeconds`, `PassWaveSeconds`, `PassWaveDistance` | 120 s, 15 s, 35 s, 2500 | 222 |
| `Worlds.SundiveSalvageSeconds`, `HeatWarnDepth` | 5 s, 0.5 | 223 |
| `Worlds.LairDiscoverDistance` | 2000 | 224 |
| `Worlds.ArrivalLine`, `ArrivalLineDelaySeconds` | on, 4 s | 225 |
| `Onboarding.VanillaTutorialDelaySeconds` | 300 s | 723 |
| `Diplomacy.*` (chances, prices, radii, hire and mercenary times) | see `Diplomacy/DiplomacyTunables.cs` | 561 to 584 |
| `ShipAi.Enabled`, `TickSeconds` | on, 1 s | 403 |
| `ShipAi.KiteRangeFraction`, `CloseInDistance`, `RetreatDistance`, `HoldTolerance` | 0.9, 100, 2000, 150 | 402 |
| `RunModes.Combat`, `Economy`, `Crew` | see section 34 | 680 |
| `Hunters.RouteTierFloor` | on | 305 |
| `RunModes.StrikeMusic` | on | 688 |
| `Factions.AllyFaction`, `RivalFaction` | picked from the galaxy seed | 640 |
| `Factions.AllyHeatFactor`, `AllyMoneyFactor`, `AllyNotorietyFactor` | 1.5, 1.3, 1.5 | 641, 642 |
| `Factions.ShelterReputation`, `ShelterHeatFactor` | 500, 0.6 | 643 |
| `Factions.RivalFencePremium` | 1.5 | 644 |
| `Factions.RelayWatchedHeatCut`, `RelayClearHeatCut`, `BlindHeatCut`, `RelayWatchRadius` | 0.75, 0.85, 0.9, 3500 | 650 |
| `Factions.BlindFuelFactor`, `BlindSpinUpFactor`, `BlindDamageChance`, `BlindDamageParts`, `BlindDamageFraction` | 1.5, 2, 0.35, 3, 0.4 | 651 |
| `Factions.MarkupStartHeat`, `MarkupMaxFactor`, `PriceHeatStep` | 75, 2, 5 | 655 |
| `Factions.CrewPriceAtMaxHeat` | 2 | 656 |
| `Factions.RefuseHeat`, `RefuseChance` | 90, 0.5 | 657 |
| `Factions.MapMarkers` | on | 660 |
| `Telemetry.Enabled` and category switches | on | 540 |
| `Telemetry.SnapshotSeconds`, `PerfSeconds`, `AccrualSeconds`, `ScanSeconds` | 30 s, 10 s, 5 s, 5 s | 537 to 539 |
| `Telemetry.AiSwitchMinSeconds`, `AiEventsPerMinute`, `CallerLookupsPerSecond` | 3 s, 600, 50 | 534, 537 |
| `Telemetry.MaxFileMegabytes`, `MaxFolderMegabytes` | 32, 1024 | 541 |

The defaults are first guesses. Live tuning sets the real values.
