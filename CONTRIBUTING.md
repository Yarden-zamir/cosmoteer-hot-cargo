# Working on Hot Cargo

Read SPEC.md first. It holds the design, the numbered requirements and the plan.

## Layout

- `src/HotCargo/Core`, `Ui`, `Dev`: shared core. Change it only when an area cannot work through the hooks in `Core/Run.cs` (`Run.Hooks`), `Core/DebugKeys.cs` (`DebugKeys.Extra`) and `Dev/Autotest.cs` (`Autotest.Commands`, `Autotest.Fields`).
- `src/HotCargo/<Area>`: one folder for each area (Tech, Journey, Hunters, Missions, Tactics, Ux). Each area has `<Area>Area.Register()`, its tunables section class and its patches.
- Shared helpers in Core: `Errors.Guard` / `Errors.Report` for any failure in a patch, sim tick or UI frame (log once, show in the overlay, never crash the game), and `SimQueries` for tagged ships, human ships and sim lookups. Use them before you write a new one.
- `mod/mod.header.rules`: the mod header. `scripts/assemble-mod.sh` builds `mod.rules` from it and from every `mod/<area>/actions.rules`.
- `mod/<area>/actions.rules`: only Action objects `{ ... }`, without an outer list.
- `mod/<area>/strings.en.rules`: string sections for the area. Two areas must not define the same top-level section.
- `mod/tunables.rules`: changed values only. The mod writes `tunables.defaults.rules` with every value at game start.

## Build and test

1. Create `scripts/pc.local.sh` with `PC_MACHINE="<T3 name of the PC>"` (see the top of `scripts/pc.sh`). Then run `scripts/pull-game.sh` once. It copies the game DLLs and rules from the PC to `../.game` and decompiles them to `../.game/decompiled`.
2. Build: `dotnet build src/HotCargo/HotCargo.csproj -c Release`.
3. Unit tests (pure math only): `cd tests/HotCargo.Tests && dotnet run`.
4. Game test on the PC: `scripts/autotest.sh tests/autotest/<name>.txt`. It deploys, turns the mod on, runs the game minimized and prints the log. Commands are in `src/HotCargo/Dev/Autotest.cs`.
5. Other PC scripts: `deploy.sh`, `run.sh`, `stop.sh`, `logs.sh`, `mod-toggle.sh on|off`.

The game runs on a Windows PC that a person also uses. `autotest.sh` runs headless test instances (no window) in slots t1..t3, each with its own save folder `%USERPROFILE%\HotCargoSlots\<slot>`, so several tests run at the same time. Other scripts take `SLOT=<name>`; without it they act on the player's own game (slot main), which starts minimized.

## Git

- Push straight to `main`. Do not open pull requests in this repo.
- Use conventional commits (`feat:`, `fix:`, `docs:`, `chore:`).
- Feature branches are local only. Merge them into `main`, build, run the tests, then push.

Run one autotest at a time per worktree: parallel runs from one worktree build into the same output folder.
- Never commit personal machine data: user home paths, machine names, Steam IDs. `scripts/check-private.sh` checks this, and CI runs it on every push. Machine settings go in `scripts/pc.local.sh` (git ignores it).

## Releases

1. Merge, push, and run `scripts/autotest-all.sh` on a clean tree. A full pass writes a marker for that commit.
2. Run `scripts/release.sh <version> [--prerelease]`. It builds `hot-cargo-<version>.zip` (mod, loader, `install.bat`, `uninstall.bat`, `INSTALL.md`), tags the commit and publishes a GitHub release.
