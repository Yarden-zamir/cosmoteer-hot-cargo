## 53. AI upkeep

How enemy AI ships keep fighting over a long fight: how long their calls for help take, and how they get repairs. These rules apply to every hostile AI of requirement 360. Tunables: `Tactics`. Code: `Ai/CallStrategy.cs`.

### Call interception

1300. **Call time.** A call for help (requirements 196 to 199) takes `CallSeconds` (default 22 s). When the call starts, the game checks every human ship that is an enemy of the caller. If one of them has an operational part of a type in `CallInterceptParts` (default the vanilla sensor array `cosmoteer.sensor_array`) whose radar range reaches the caller, the call takes `CallInterceptSeconds` (default 30 s) instead. The call starts at the same time in both cases. It only takes longer. An unknown part ID in `CallInterceptParts` is an error that shows in the overlay.
1301. **Sensor reach.** The radar range of a part is the vanilla radar radius of one sensor (`SensorManager.RadarRadius`): the ship's bounding radius + the part's `RadarRadius` × the radar factor of the ship's nebulas. The distance is from ship center to ship center. Limit: the check runs only at the call start, so a sensor ship that comes into range later does not slow a running call. Revisit when players ask to intercept a running call.
1302. **Tell.** An intercepted call shows a log line to every player, with the part name and the caller: "Sensor array: distress call from {ship} intercepted early. It takes longer." The ring and the "CALLING HELP n%" label of requirement 197 use the duration of that call.
1303. **Determinism.** The check reads sim state only (ship positions, operational sensors, nebulas) in the AI tick, and walks the human ships in sim order. The call duration is module state: saved with the ship and part of the integrity hash. The autotest `ai-call` checks both durations. The co-op step `ai.personalities` intercepts calls on two machines with no desync.
