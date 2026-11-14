# Plan: Patrol Chatter listen in the world

Spec 46 rule: gambles happen in the world. Patrol Chatter keeps only the clear deal (pay 0.2, heat -8).

Decisions:
- Start: a part button on a selected, working vanilla sensor array (`cosmoteer.sensor_array`) of a ship that the player commands. It shows while a faction ship (the old hail target `FactionShip`) is in range, has not been heard, and no listen runs.
- MP input `HotCargoPatrolListen` (sensor part, target). The sim tick runs it, so co-op stays in step.
- 8 game seconds. A progress bar above the sensor array, every frame, for every player.
- Breaks when: the sensor array is gone or stops working, the target is gone or turns hostile, the distance goes over range, or combat (the listening ship, or an enemy of it within `ListenCombatRadius`, fired in the last `ListenCombatSeconds`).
- Once per faction ship, counted at the end (a broken listen can start again).
- Chance: linear, 100 % at heat 0 to 60 % at heat 100. Success heat -4. Failure heat +5 ("they log your question").
- One listen at a time for the fleet.
- Tunables in `EventsTunables` (`Listen*`). Pure math in `PatrolListenModel.cs` with unit tests.

Steps:
1. Rules: remove "Ask nicely".
2. Model + tunables + state.
3. Input, tick, button, bar, strings.
4. Autotest `events-listen`, co-op step, screenshot script.
5. EVENTS.md, spec 46, TUNABLES.md.
