## 42. Performance

Co-op runs in lockstep, so the game runs at the speed of the slowest machine. Hot Cargo code that runs every frame runs on every machine, also on a slow client. So frame code does little work, and sim code caches what it asks often. Measure: autotest command `selfprof` (`Telemetry/SelfProfile.cs`), test `telemetry-stress` (single player and `mp3:telemetry-stress`).

870. **Trait cache.** `PersonalityStrategy.Traits` keeps its result for one sim tick on the ship's `HcPersonality` module. A new build measure, another player index or other tunables compute it again. The cache is not sim state: a fresh computation gives the same value.
