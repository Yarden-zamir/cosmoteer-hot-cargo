# Plan: bounty target leaves the sim but stays alive

Bug: in core-playthrough the vanilla Bounty target ("Swift") leaves the sim mid-fight. The game still counts it alive, so the bounty never ends.

Steps
1. Log the target state (ShipID, node, live ship, stasis spawner) in the playthrough watch line, and a stack trace when a target ship leaves the sim.
2. Reproduce on the Mac sim, find what removes the ship.
3. Fix the cause with vanilla behavior where possible.
4. Invariant (spec 44): a bounty target in progress must be reachable, if it fits.
5. Check the mission marker limit in Missions/MissionMarkers.cs for the same cause.
6. core-playthrough passes on the PC.
