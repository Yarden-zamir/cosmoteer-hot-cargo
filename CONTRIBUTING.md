# Working on Hot Cargo

Read SPEC.md first. It holds the design, the numbered requirements and the plan.

## Layout

- `src/HotCargo/Core`, `Ui`, `Dev`: shared core. Change it only when an area cannot work through the hooks in `Core/Run.cs` (`Run.Hooks`), `Core/DebugKeys.cs` (`DebugKeys.Extra`) and `Dev/Autotest.cs` (`Autotest.Commands`, `Autotest.Fields`).
- `src/HotCargo/<Area>`: one folder for each area (Tech, Journey, Hunters, Missions, Tactics). Each area has `<Area>Area.Register()`, its tunables section class and its patches.
- `mod/mod.header.rules`: the mod header. `scripts/assemble-mod.sh` builds `mod.rules` from it and from every `mod/<area>/actions.rules`.
- `mod/<area>/actions.rules`: only Action objects `{ ... }`, without an outer list.
- `mod/<area>/strings.en.rules`: string sections for the area. Two areas must not define the same top-level section.
- `mod/tunables.rules`: changed values only. The mod writes `tunables.defaults.rules` with every value at game start.

## Build and test

1. Run `scripts/pull-game.sh` once. It copies the game DLLs and rules from the PC to `../_shared/game` and decompiles them to `../_shared/game/decompiled`.
2. Build: `dotnet build src/HotCargo/HotCargo.csproj -c Release`.
3. Unit tests (pure math only): `cd tests/HotCargo.Tests && dotnet run`.
4. Game test on the PC: `scripts/autotest.sh tests/autotest/<name>.txt`. It deploys, turns the mod on, runs the game minimized and prints the log. Commands are in `src/HotCargo/Dev/Autotest.cs`.
5. Other PC scripts: `deploy.sh`, `run.sh`, `stop.sh`, `logs.sh`, `mod-toggle.sh on|off`.

The game runs on a Windows PC that a person also uses. Scripts start the game minimized. Only one game runs at a time; `autotest.sh` waits for a lock.
