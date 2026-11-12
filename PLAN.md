# Plan: chain rivals attack before their stage starts

Cause: Station-stage missions spawn their guard ships at offer time. `ChainSalvageRace` (rivals) and `ChainBlackBox` (drone) give them the `HotCargoHunter` AI, which chases player ships across the whole system. At the first station of sector 1, the rivals attack the starter ship before the players accept the chain.

Steps:
1. Give these guards the vanilla `Normal` AI, like the other Station-stage guards. It keeps the Hot Cargo tactics (spec 360) and respects `TargetAcquireRangeFromOrigin`.
2. Test `chains-rivals-start`: 30 s with the offer open, no vault damage. Accept, go to the pod, rivals attack.
3. Patch CHAINS.md section 4.
4. Run `scripts/autotest-affected.sh` and co-op `events`, `core`.
