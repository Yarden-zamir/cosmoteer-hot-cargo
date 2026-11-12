# Hot Cargo missions

SPEC.md section 24 holds the numbered requirements. This file describes how each mission plays. Numbers are defaults. The `Mechanics` section of `tunables.rules` holds the live numbers. Rewards are in `mod/missions/mechanics/sysgen_mechanics.rules`.

## Mechanics missions

These missions use the game's own systems: building, tractor beams, mining, crew, surrender, sensors and careful targeting. Each one shows up in a modest share of systems (`Mechanics.<Name>Chance`, 15 to 25 %). Rewards use the vanilla tier tables, so the pay rises with the sector tier. The table shows each reward as a multiple of a standard vanilla mission.

| Mission | Offered at | Money | Fame | Contract terms |
|---|---|---|---|---|
| Drone Siege | Stations | 1.5× | 1.25× | free |
| Prototype Retrieval | Bounties | 2× | 1.5× | fee 0.3, heat +5, informant 10 % |
| Sunside Ore | Deliveries | 1.25× plus ore sales | 1× | free, rival `ore_claims`, informant 5 % |
| Salvage Rights | Stations | 1.25× | 1× | free, rival `salvage_rights` |
| Repossession | Bounties | 1.75× | 1.25× | fee 0.4, heat +4, rival `salvage_rights`, informant 10 % |
| Storm Survey | Stations | 1× | 0.75× | free, trail weight 0.5 |
| Claim Jumpers | Stations | 1.25× | 1× | free, rival `ore_claims`, informant 5 % |
| Listening Post | Faction wars | 1.75×, more with intensive scans | 1.25× | heat +3, informant 10 % |

The contract terms are `HotCargoContract` entries in each mission's rewards (the economy area reads them). The fee is in units of a standard mission's pay at the mission's tier.

### Drone Siege
Drones attack a faction listening post in open space. Hold the post for five minutes. The clock runs only while one of your ships is within 1,500 of the post. Any crewed ship counts. So the clever answer is a cheap gun platform: use **Build New Ship** next to the post, add a reactor, guns and a crew, and fly on. Waves come every 35 s while the post is guarded. They are tier-1 to tier-3 combat ships, the smallest built-in ships. If the post dies, the mission fails. A 25-minute cap stops the offer from lasting forever.

### Prototype Retrieval
A pirate ship carries a Prototype Core on its outer hull. The core is a 2×2 part with 8,000 health. Fight with care and target the reactors, bridge or crew instead. There are two ways to win. Wreck the ship, then bring a ship within 400 of the core, and the crew lift it out. Or beat the ship until it offers surrender, and accept: it hands the core over at once. If the core part is destroyed, the mission fails. When the ship breaks apart, the mission follows the core to its new piece.

### Sunside Ore
A seam of Helion Ore asteroids sits 2,000 to 3,300 from a single sun, inside the sun's heat zone. Mine the ore with mining lasers. Then sell 30 to 45 units to the station within 30 minutes (vanilla DeliverResources). Each unit needs a storage slot (5 to a tile), so bring storage room. The heat damages parts, so keep moving. Binary suns get no seam. Hook for other system generators: a doodad tagged `sun` can host the seam, and the spawn flag `hc_no_sun_mining` blocks it.

### Salvage Rights
A crewless freighter drifts 3,000 to 4,500 from a station. Bring its hull within 800 of the station's hull within 25 minutes. Tractor beams work best. Your crew can also claim the wreck and fly it, if its engines still work. If the hulk is destroyed or salvaged to nothing, the mission fails.

### Repossession
A stolen freighter flies with one or two escorts. Take it intact. Beat it until it offers surrender and accept, or kill its crew and move your own crew aboard to claim it. If the freighter is destroyed, the mission fails. A wreck that nobody claims yet does not fail the mission.

### Storm Survey
Three markers sit inside clouds or storms (gas clouds, electron clouds or ion storms). Hold a ship with a working sensor array within 400 of each marker for 15 s. Ion storms and electron clouds are the danger. A system without any of them offers no survey.

### Claim Jumpers
A civilian mining barge sits in an asteroid field. Raiders one tier below the sector come in small waves. Keep the barge alive for four minutes of guarded time. It works like the drone siege, with fewer and stronger enemies.

### Listening Post
A faction listening post hides in a gas cloud, 4,000 to 5,000 from a hostile base. Its scout follows you. Take it anywhere within 3,500 of the base. Then click **Start the scan here** in the mission panel. The base sends four waves, one every 45 s, from its side. Before each wave you can click **Intensive scan**, at most twice. Each click gives the next wave one more ship and one more tier, and gives the pay +25 %. If the scout dies, the mission fails.

### Leave early (Drone Siege, Claim Jumpers, Listening Post)
After the first wave interval, the mission panel shows **Leave now and keep X credits**. One click completes the mission at once. The pay is one share for each whole wave interval you survived, times the intensive-scan factor. For example, 2 of 4 waves pay half. Fame scales the same way, but without the intensive-scan factor. You collect the reward at the station as usual.

### Orders from the mission panel
The clickable lines start with **[Click]**. A click sends a multiplayer input (`HotCargoHoldOutOrder`), so all co-op players see the same result. The game checks each order again when it runs. For example, the scan does not start when the scout is out of range.
