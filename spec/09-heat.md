## 9. Heat

23. Heat belongs to the current sector and starts at 0 in sector 1.
24. Each simulation tick, heat increases by heat multiplier × `HeatPerGameMinute` × the zone factor × elapsed game minutes. The heat multiplier includes the difficulty heat rate (requirement 681). The zone factor comes from the controlling faction of the system (requirements 641 and 643), else 1. Default: 1.2 heat per minute. Open contracts add a trail on top (requirement 101).
25. When the players earn fame, heat increases by heat multiplier × `HeatPerStandardMission` × (fame earned ÷ standard mission fame of the sector tier). Default: 1.5 heat for each standard mission fame unit. The standard mission fame includes the same factors as the payout: the difficulty fame factor and `RewardScale`.
    The division keeps "heat per mission" stable across tiers. A typical mission pays about 3.4 units, because a vanilla mission pays the kill fame of its targets and its own fame reward (a bounty pays 2 to 2.5 units, a base attack 6 to 15). So a typical mission adds about 5 heat × multiplier. Fame that the vanilla fame cap takes adds heat too (requirement 302).
26. Heat multiplier = `HeatMultiplierByUnits[fleet tech count]`. Default table for 0 to 8 units: `[0.25, 0.4, 0.52, 0.64, 0.76, 0.88, 1.0, 1.1, 1.2]`. A count above the table uses the last entry.
27. Heat does not increase while the game is paused. Game speed changes the rate, as it does for all game time.
28. When the players enter another system, a share of the heat stays: a jump removes the cut of its kind (requirement 650). A switch to a system where own ships wait, without a jump, keeps `HeatCarryOverFraction` (default 0.25).
29. Heat stays in the range 0 to 100. During a network outage every heat gain is blocked and losses still apply (requirement 921).

With defaults and 8 units held, the players enter a sector with about 25 heat (requirement 28). Each typical mission adds about 6 heat from fame and about 2.6 heat from its contract (flat heat and informants), and time adds about 1.4 heat per minute. So a sector reaches the top stage after about 6 missions in about 17 minutes, or after about 50 minutes with no missions. BALANCE.md has the full heat budget.
