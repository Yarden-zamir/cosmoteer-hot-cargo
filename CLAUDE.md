# Hot Cargo — agent instructions

Hot Cargo is a Cosmoteer career mod: C# Harmony code plus `.rules` content. Read `SPEC.md` and `spec/` (design and numbered requirements, one file per section) and `CONTRIBUTING.md` (layout, build, test) before you change anything. Feature docs: `EVENTS.md`, `MISSIONS.md`, `CHAINS.md`, `WORLDS.md`, `UX.md`, `TUNABLES.md`, `BALANCE.md`, `TELEMETRY.md`.

Procedures are skills in `.claude/skills/`:
- `hot-cargo-testing`: build, unit tests, headless game tests, co-op tests, infrastructure failures.
- `hot-cargo-screenshots`: screenshots in a windowed test slot (never the player's game slot).
- `hot-cargo-install`: install a build into the player's own game.
- `hot-cargo-release`: publish a GitHub release.
- `hot-cargo-new-area`: add a feature area (entry point, tunables, rules, strings).
- `hot-cargo-tune`: change tunables in the player's running game on request (`scripts/tune.sh`), also as the co-op host.

## Git

- Feature work goes through a pull request: push the feature branch and open a PR with a short summary and screenshots of visible changes (`gh-pr-images` skill). CI checks it (build, unit tests, private data, game tests). Start PR descriptions and comments with 🤖; keep them short. Small fixes go through a short PR too. Conventional commits.
- Feature work happens in a worktree of the bare repo (`../.bare`), one branch for each feature, and reaches `main` only through its pull request.
- Owner-merged PRs: the agent that owns a PR merges it. CI runs the checks `build`, `private-data` and `game-tests` (every game test in the headless sim on Linux, except tests with the line `# runner: pc`). `main` requires them and an up-to-date branch, so GitHub orders the merges. The flow:
  1. Push the branch and open the PR. CI runs the game tests.
  2. Tests outside CI: `LIST_ONLY=1 RUNNER=pc scripts/autotest-affected.sh` lists the affected tests that CI does not run (the `game-tests` job summary lists them too). Run only those the merge depends on, on the PC (`scripts/autotest-all.sh 400 <names>`), and tick the box of the PR template. Lean toward the minimum.
  3. `gh pr merge --auto --merge`. Watch the PR instead of polling it.
  4. When `main` moves, GitHub marks the PR out of date. Merge `origin/main` into the branch, read what the new commits and PRs intend (`git log`, `gh pr view`), adapt the change to them, and push. CI runs again; auto-merge does the rest.
- Tests a PR needs: what CI runs, the affected tests outside CI that the merge depends on, plus anything the change calls for (a new test, a co-op step for sim changes, screenshots for visible changes).
- Unrelated failures: a flaky or infrastructure failure in a test that the PR does not touch counts as passing; say so in the PR. A failure in a touched test, or one that also fails on `main`, blocks the merge.
- Never push to `main` directly; `scripts/integrate.sh` stays for the release and maintenance runs that the coordinator starts.
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
- Never start, stop or restart the game in slot main (the player's game): no `vault-saved-ships.sh`, `run.sh`, `stop.sh` or deploy with `SLOT=main`. While `.state/player-hold` exists, the scripts refuse it (exit 3). They also refuse a windowed test slot (`screens.sh`, `screens-mp.sh`) while that file exists: a window could take focus from the player. Headless test slots stay allowed. Do not remove that file and do not work around it. Ask the coordinator for main-slot work; the coordinator runs it when the player says the PC is free.

- An agent works only in its own worktree and branch. At the end it pushes its branch, opens a PR and merges its own PR with auto-merge after CI passes (Git section). It never pushes to `main` directly.
- Tests: run `scripts/autotest-affected.sh` (the smoke tests of your areas, their co-op tests, `core`, and any test you changed) and the unit tests. Put logic in a `*Model.cs` file with unit tests (`tests/TIERS.md`). CI runs every game test in the headless sim before a merge.
- Save progress so a crash or restart loses nothing:
  - In the first minutes, commit a short `PLAN.md` (design decisions, steps) to your branch, push it, and open the draft PR with a checklist of the steps. Delete `PLAN.md` before the final commit.
  - Commit and push after each step that builds. Tick the step in the PR checklist.
  - A restarted agent reads the branch, `PLAN.md` and the PR checklist first, and continues from there.
- PC work (tests, deploys, screenshots) only works while your turn is active: the T3 link refuses PC calls from a thread with no active turn ("no longer owns an active thread run"), and the run then hangs on its slot. PC work: run long suites with Bash run_in_background and wait with t3_background_wait (or foreground calls under 9 minutes). Never end your turn while a PC run is going. A `--detach` run started from a short Bash call loses PC access when that call ends (current T3), so use `--detach` only for runs that do not need the PC. This is about how you call Bash, not about the player: whether the player's own game runs only changes how many test slots exist (the scripts handle it), never whether you may run tests.
- Never end a turn while your own tests or builds run in the background. Ending the turn early leaves the work hanging. How to wait:
  - Short runs (under about 9 minutes): run them in the foreground with a long Bash timeout.
  - Long runs: start them with Bash `run_in_background`, then call `t3_background_wait` with the task ID (load it with ToolSearch `select:mcp__t3-code__t3_background_wait`). The turn then stays open until the run finishes.
- Start only the last message with `FINAL REPORT`: what works with test results, what does not, Core changes, spec changes, commits, merge notes.
