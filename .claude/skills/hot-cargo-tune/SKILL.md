---
name: hot-cargo-tune
description: Change Hot Cargo tunables in the player's running game when the player asks (heat rate, hunter counts, rewards, fuel, chances and so on), in single player or in co-op as the host. Use for any "make X more or less" request during play.
---

# Change values in the running game

## 1. Find the field
- `TUNABLES.md`: the sections and when each value applies.
- The doc comments in `src/HotCargo/<Area>/*Tunables.cs`: the meaning of each field. The settings window shows the same text.
- `scripts/tune.sh --defaults`: every value of the installed build. `scripts/tune.sh --show`: the values that the player changed.
- Paths: `Section.Field`, `Section.Nested.Field`, `Section.List[index].Field` (index from 0). For example `Heat.HeatPerGameMinute`, `Mechanics.Siege.Seconds`, `Hunters.Stages[2].ShipCount`.
- If the player names a value from the settings window, search its label (the field name with spaces) in the `*Tunables.cs` files.

## 2. Change it
1. Run `scripts/tune.sh Path=Value [Path=Value ...]`. Without `SLOT` it acts on the player's game (slot main).
2. Values: numbers with a dot, `true`/`false`, enum names, texts without quotes, lists comma separated (`Heat.HeatMultiplierByUnits="1, 1.5, 2"`).
3. Read the answer. `ok <hash>` and one line per path with `applies Now`, `NextUse`, `NextSpawn`, `NextOffer`, `NextSystem` or `NewRun`. `error <reason>`: nothing changed. Fix the value and run it again.
4. Tell the player the change and when it takes effect, in their words: now, the next time it shows, the next ships that spawn, the next mission offers, the next new system, or the next new run.
- All values of one call apply together or not at all. Change values that depend on each other (for example `Contracts.FeeMin` and `FeeMax`) in one call.
- The game must be in a run, not on a menu screen. With no answer in 10 seconds, tune.sh says so, and nothing changed.

## 3. Reset
- One value: `scripts/tune.sh --reset Path [Path ...]`.
- A whole section or list: `scripts/tune.sh --reset Hunters` or `--reset Hunters.Stages`.

## Co-op
- Only the host's game takes changes. Run tune.sh against the host's PC. The host's game sends the values to every machine, and all apply them at the same tick.
- A client's file and requests have no effect: tune.sh answers with an error.
- The status line of the settings window shows the tunables hash. It is the same on every machine, and the same as the hash in the tune.sh answer.

## What tune.sh cannot change
- Part, resource, mission and other rules content in `mod/**/*.rules` (for example the tech vault part): change the repo, install the build (`hot-cargo-install`) and restart the game (spec 54).
- A value that applies at `NewRun`: tell the player that it needs a new career.
