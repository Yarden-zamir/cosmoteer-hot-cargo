---
name: hot-cargo-game-update
description: Bring Hot Cargo to a new Cosmoteer version. Pull the game files, refresh the CI game files, rebuild, run every test, check the loader, and change the target version. Use when Steam updated Cosmoteer on the PC, or when scripts/pull-game.sh reports a game version that the mod does not target.
---

# Game update

1. Pull the game files: `scripts/pull-game.sh`. It only reads files on the PC, so it is safe while the player plays. Its last line names the game version and tells if the mod targets it.
2. Show what changes in the CI game files: `scripts/refresh-game-refs.sh`. Then push them: `scripts/refresh-game-refs.sh --push`. From then on, CI builds and tests every PR with the new game.
3. Do the rest in a branch and a PR (`hot-cargo-pr-flow`).
4. Build: `dotnet build src/HotCargo/HotCargo.csproj -c Release`. A compile error shows a changed game API. Read the new definition in `../.game/decompiled/` and match its exact signature.
5. Find the vanilla changes: `git -C ../.game-refs show --stat HEAD` lists the changed game files. Check each spec or doc line that names a changed method or rules file (CLAUDE.md, The game).
6. Change the target version: `CompatibleGameVersions` in `mod/mod.header.rules`. It is the only source for the scripts (`scripts/game-version.sh`; the release notes read it).
7. Find the other places: `git grep -n '<old version>'`.
   - A line that names the target version (for example `SPEC.md`, `README.md`): change it.
   - A code comment that states a vanilla fact of the old version (for example the reward factor readers in `src/HotCargo/Journey/RewardScale.cs`): check the fact in the new decompiled code, then change the version.
   - An audit that names the version that it measured (`BALANCE.md`, `UX.md`): keep it until someone measures again.
8. Loader: the PC replaces the startup hook loader only when `src/HotCargo.Loader/VERSION` changes. If the loader code must change (for example for a new .NET version), bump `VERSION`. A new loader needs every game on the PC closed once, the player's game too: ask the coordinator.
9. The deploy takes the game's new `Cosmoteer.runtimeconfig.json` as the original and adds the hook again (`scripts/slot-install.ps1`). Nothing to do.
10. Test: CI runs every game test in the headless sim. The real game is the ground truth after an update, so also run every test on the PC: `scripts/autotest-all.sh 400` (`hot-cargo-testing`). A Harmony patch whose target changed fails at start and shows in the overlay and the log.
11. After the merge, install (`hot-cargo-install`) and publish a release (`hot-cargo-release`).
