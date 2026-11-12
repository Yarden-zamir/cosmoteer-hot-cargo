# Hot Cargo — agent instructions

Hot Cargo is a Cosmoteer career mod: C# Harmony code plus `.rules` content. Read `SPEC.md` and `spec/` (design and numbered requirements, one file per section) and `CONTRIBUTING.md` (layout, build, test) before you change anything. Feature docs: `EVENTS.md`, `MISSIONS.md`, `CHAINS.md`, `WORLDS.md`, `UX.md`, `TUNABLES.md`, `BALANCE.md`, `TELEMETRY.md`.

Procedures are skills in `.claude/skills/`:
- `hot-cargo-testing`: build, unit tests, headless game tests, co-op tests, infrastructure failures.
- `hot-cargo-screenshots`: screenshots in the player's game slot, with the slot lock.
- `hot-cargo-install`: install a build into the player's own game.
- `hot-cargo-release`: publish a GitHub release.
- `hot-cargo-new-area`: add a feature area (entry point, tunables, rules, strings).

## Git

- Push straight to `main`. No pull requests. Conventional commits.
- Feature work happens in a worktree of the bare repo (`../.bare`), one branch for each feature. The integrator merges into `main` with `scripts/integrate.sh <branch>`: merge, build, unit tests, private-data check, affected autotests, push, install. It pushes only when every step passed.
- Never credit an AI, model or tool in commits or files.
- Never commit personal machine data. `scripts/check-private.sh` checks this, and CI runs it.

## The game

- Cosmoteer 0.30.5a, .NET 10, Windows only. It runs on a Windows PC that `scripts/pc.local.sh` names (`PC_MACHINE`). You work on a Mac.
- A spec or doc line that says how the vanilla game behaves names the decompiled method or rules file it comes from. Check it there before you build on it.
- Ground truth for game code: the decompiled game in `../.game/decompiled/Cosmoteer/Cosmoteer/` and `../.game/decompiled/HalflingCore/Halfling/`. Read the definition of a game method before you call or patch it, and match its exact signature.
- Vanilla rules: `../.game/Data/`. PNG files exist only on the PC.
- The build publicizes Cosmoteer and HalflingCore, so internal and private members are accessible.
- The C# code loads through a .NET startup hook before the game reads rules. Polymorphic rules types (`Type = X`) in our assembly are found by the game's serializer. Explicit bases (ObjectiveSpawner, Objective, RewardSpawner, SimSpawner, MPInput) need `[SerialDerivedType(TypeName = "...")]`.

## Shared IDs

- Resource `yarden_zamir.stolen_tech`. Part `yarden_zamir.tech_vault`. Hunter AI `HotCargoHunter`. Tags: Buyer `hc_buyer`, hunters `hc_hunter`. Mod folder `hot_cargo`, mod ID `yarden_zamir.hot_cargo`.

## Rules for code

- Simplest code that works. Look for an existing helper (ours or the game's) first. Shared helpers: `Core/SimQueries.cs`, `Core/Errors.cs`, `Autotest.Teleport`, `Autotest.ClearSpotNear`, `Scaling.EnemyScale.Count`.
- Defensive code: null checks and guards. Unexpected input crashes, except a failing Harmony patch: it logs through `Core/Errors.cs` and shows in the overlay, and the game keeps running.
- Every hostile spawn count goes through `Scaling.EnemyScale.Count` (C#) or a hostile rules spawner (scaled by the scaling patch).
- Every designer number is a tunable with `[Serialize(Optional = true)]`, a default and a doc comment. The doc comment is the description in the in-game settings window. A tunable that does not apply at once gets an entry in `TunablesMenu/TunableTiming.cs` and in `TUNABLES.md`.
- Comments only where needed. A simpler approach with a known limit gets a comment that names the limit and when to revisit it.
- No regex unless no string method works. Modern C#.

## Co-op determinism (lockstep)

- Change sim state only from sim ticks (`EventHub.Tick`), `Run.Hooks`, rules spawners and MP inputs. Never from UI frames, except single-player debug keys.
- Randomness: `sim.Rand` or a `FastRand` seeded from sim data. Never `System.Random`, `DateTime` or `App.Clock` in sim logic. Iterate in a stable order.
- Player choices that change the sim go through an `MPInput`, allowed in `CareerGameModeManager.AllowMPInput`.
- Test sim changes with the co-op harness (see `hot-cargo-testing`).

## Text

- Player-facing text: short, clear, readable at a glance during play, FTL-like voice. Same terms everywhere: tech, vault, heat, Buyer, contract, hunters.
- Docs and comments: short sentences, active voice, simple tenses.

## Spec

- A change is done when the spec lines it touches are true. Patch stale lines in place. Never renumber requirements. A new feature gets a new file in `spec/` with the next free section number and a new requirement number range.

## Agents

- An agent works only in its own worktree and branch, and does not push.
- Start only the last message with `FINAL REPORT`: what works with test results, what does not, Core changes, spec changes, commits, merge notes.
