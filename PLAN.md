# Escort mission fix (Convoy Duty, spec 36)

## Causes
1. Short route: the jump point is 4,500 to 6,500 from the station, in rules, with no link to the system size.
2. No fight: vanilla `CareerSimModeManager.ModifyAIAttackValue` divides the value of civilian targets by 10,000, and AggroEnemies scores targets by firepower. An unarmed civilian freighter never wins against a player ship. The raiders also wait at the jump point (900 to 1,400 from it), often in stasis, and arrival completes at 400.
3. Marker stays: the objective reveals the POI doodad and never removes it.

## Design
- Route: tunable `EscortRouteShare` (share of the gate ring radius). At offer creation the objective moves the POI along the rules direction, turned in steps until the route stays inside the nearest gate radius and clear of suns.
- Ambush: the raiders leave the system generator. When the freighter covers `EscortAmbushAt` of the route, the objective spawns them from `missions/convoy_ambush.rules`, `EscortAmbushLead` ahead of the freighter on its route (live, near the players). The rules spawner scales with the scaling patch.
- Targeting: a `ModifyAIAttackValue` postfix gives convoy raiders a value for the freighter of their own firepower × `EscortRaiderFocus`.
- Marker: the objective removes the POI when it completes or fails. Leftover raiders stay as a normal hostile group with no marker.

## Steps
1. Tests that show the bugs: extend `missions-escort`, add `missions-escort-ambush`.
2. Route tunable.
3. Ambush spawn and targeting.
4. Marker removal.
5. Spec, docs, co-op test, merge origin/main.
