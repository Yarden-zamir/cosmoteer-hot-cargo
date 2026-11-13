# Ally zone: faction ships and hunters stay passive

Rule (spec 1150): in corporate ally space (Zone.Ally), the ally faction's player indexes (military and civilian) and the hunters' allegiance are not enemies. Neutral and Shelter zones keep the vanilla rule.

Patch point: postfix on `CareerSimModeManager.AreEnemies`. AI targeting (`Ship.IsEnemiesWith`), auto-fire and damage (`CanDamage`) all read it.
The postfix reads a cache that `Run.Hooks.Tick` fills from sim state (zone, ally indexes, hunter indexes), so the hot path is a few int compares and every co-op machine agrees.

Steps:
1. FactionsModel rule + unit test.
2. AllyPeace.cs: tick cache + AreEnemies postfix; register in FactionsArea.
3. Hire and mercenaries: ally-faction ships in an ally zone take no hire contract (they do not fight hunters there).
4. Autotest `factions-peace` (ally zone: no targeting either way; neutral zone control: vanilla targeting).
5. Spec 38 (1150), spec 37 hire line, test lists.
6. Merge origin/main after PR #20, final tests (Mac + PC co-op `factions`).
