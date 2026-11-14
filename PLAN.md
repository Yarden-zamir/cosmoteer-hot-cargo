# Plan: rescue beacon

Steam reviews: players lose a crippled ship with no way to recover. A Rescue Beacon calls a tug.

Decisions:
- Item `yarden_zamir.rescue_beacon`, sold by trade stations through a comms line ("Buy a rescue beacon.", a clear deal: 0.3 money units). Once per station per run, like every hail line.
- Item action "Fire the beacon" (spec 46 item actions). Item actions get an optional C# `Handler`; it runs first and can refuse (the unit stays). The action button tooltip shows the handler's live lines: the fee in credits and the heat.
- The handler (sim, from the item action MP input) starts a rescue tow mission: the Dead in Space tow objective (`HotCargoTow`, requirement 88) with the firing ship as the towed ship and the nearest trade station that is not hostile (new spawner option `FriendlyOnly`, `MinStationDistance` 0). No such station: it refuses, no fee, no heat.
- Then it takes the fee from the firing player (`Events.RescueFee` money units). Too little money: the tug takes what there is and comes anyway (a rescue must not fail for money).
- Heat `Events.RescueHeat` (10). A tug (spawns.rules `RescueTug`, civilian, no AI) flies to the ship, then to the station. The sim tick drags the ship on a tow line (`RescueTowLength`). On arrival the tow objective completes; the tug flies off and leaves the sim.
- The tug destroyed: the tow stops with a message; the mission stays, so the players can finish it themselves.
- Co-op: the item action MP input, sim tick only, spawn seeds from the sim random generator.

Steps:
1. Item rules, strings, comms line, mission metatype.
2. Item action handlers; rescue handler; tug tick.
3. Autotest `events-rescue`, co-op step, screenshots.
4. EVENTS.md, spec 46, default tunables.
