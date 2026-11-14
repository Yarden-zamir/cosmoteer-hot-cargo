# Cosmoteer headless sim (macOS and Linux)

The spike below ran on the Mac. The same host (tools/headless-sim) now also runs on Linux: CI runs every game test with it in the merge queue (`.github/workflows/check.yml`, `scripts/ci-sim-tests.sh`), with the game files from the private refs repo (`scripts/refresh-game-refs.sh`).

Question: can a .NET 10 host on macOS (arm64) run the real game logic with Hot Cargo, so that game tests do not need the Windows PC?

## Verdict: GO, in use for single-player tests

`tools/headless-sim/` boots the real `GameApp` without a window, loads the vanilla rules and the Hot Cargo mod, applies the Hot Cargo Harmony patches and runs the real game loop. Existing `tests/autotest/*.txt` scripts run unchanged. No game file and no Hot Cargo source changed.

75 of the 79 single-player tests pass on the Mac. The other 4 and the 21 co-op tests stay on the PC (see "Test results"). All 79 run in about 8 minutes with 8 runs in parallel, and no PC slot.

## How to run

1. Copy the game files once (again after a game update): `scripts/pull-game.sh`. It copies only files, so it is safe while the player plays.
2. Run tests: `scripts/autotest-sim.sh [timeout] [test ...]`. No test names: every single-player test.
3. The tests that a change touches: `scripts/autotest-affected.sh`. It runs single-player tests on the Mac and the others on the PC, at the same time.

`autotest-sim.sh`:

- Same pass rule as `scripts/autotest.sh`: the log has `telename <name>`, `AUTOTEST DONE`, and no `AUTOTEST FAIL` or `ERROR` line.
- Same log place: `../.state/logs/<worktree>/<name>.<time>.sim.log`, the summary in `run-sim-<time>/`.
- It builds the host and stages the mod once (`scripts/stage.sh`). Each run copies the stage into a new temporary save folder.
- Parallel runs: `HOT_CARGO_SIM_JOBS`, default `min(cores - 2, memory GB / 2)` (8 on this Mac). The limit is for the whole Mac: all worktrees share the locks `/tmp/hot_cargo_sim_slot_<n>.lock`.
- A failed run gets one retry, like on the PC. A pass on the retry prints `FLAKY` and goes to `../.state/flaky.log`.

Which runner a test uses:

- A test with the line `# runner: pc` stays on the PC. The line before it says why.
- Co-op sessions (`tests/autotest-mp/`) run on the Mac too, as two sim processes on 127.0.0.1 (`scripts/autotest-sim-mp.sh`). Both keep the real-time clock (`SIM_REALTIME=1`), because network requests and lockstep read the game clock. Encoding the co-op ship icon (`TextureData.SaveTo`, FreeImage) writes a blank PNG.
- `integrate.sh` sets `AUTOTEST_SIM=0`: merges keep the PC suite as the gate.
- Without the game files on the Mac, `autotest-affected.sh` runs every test on the PC.

`tools/headless-sim/run.sh` runs the host once:

- `run.sh` with no arguments: boot and load the rules, then exit.
- `run.sh autotest <script>`: run an autotest script. Exit code 0 only on a pass (same rule as above).
- `SIM_SAVE=<folder>` keeps the save folder (logs, saves, `hot_cargo_log.txt`). `SIM_STAGE=<dir>` uses a stage from `scripts/stage.sh` and builds nothing.

The host passes `--headless --nosteam --cores 2 --rootsavepath <folder>`. `GameApp` and `Paths` read these from `Environment.GetCommandLineArgs`. `--cores 2` is the same as in a PC test slot.

## Test results

Three full runs on this Mac (Apple Silicon, 10 cores, 64 GB, .NET 10.0.14), 8 runs in parallel. In the last run all 75 Mac tests passed, without a retry.

| Area | Pass on the Mac | Needs the PC |
|---|---|---|
| ai | ai-call, ai-cover, ai-flee, ai-personality, ai-team | mp:ai |
| balance | balance-hunter-floor, balance-rules | - |
| chains | chains-content, chains-fail, chains-later, chains-walk | - |
| contracts | contracts-contracts, contracts-trail, contracts-value | mp:contracts |
| core | core | mp:core |
| diplomacy | diplomacy-friends, diplomacy-hunters | diplomacy-surrender, mp:diplomacy |
| events | events-abandoned, events-batch2, events-courier, events-hail, events-outcomes, events-research, events-save, events-tow, events-triggers, events-unavoidable, events-wanted | mp:events |
| factions | factions-combat-jump, factions-jumps, factions-peace, factions-prices, factions-zones | mp:factions |
| hunters | hunters-spawn, hunters-tactics, hunters-waves | - |
| journey | journey-jump, journey-run | - |
| mechanics | mechanics-holdout, mechanics-listening, mechanics-offers, mechanics-placement, mechanics-prototype, mechanics-salvage, mechanics-save, mechanics-survey-mining | - |
| missions | missions-escort, missions-offers, missions-save, missions-shady | mp:missions |
| onboarding | onboarding-log | onboarding-hints, mp:onboarding |
| runmodes | runmodes-difficulty, runmodes-ironman, runmodes-music, runmodes-paneltip | mp:runmodes |
| scaling | scaling-counts, scaling-ui | mp:scaling |
| shipai | shipai-basic, shipai-save | mp:shipai |
| tech | tech-drop, tech-hunters, tech-outside, tech-sell, tech-start, tech-sun, tech-timer | - |
| telemetry | - | telemetry-basic, telemetry-run |
| tunablesmenu | tunablesmenu-live | mp:tunablesmenu |
| ux | ux-basic, ux-more | - |
| worlds | worlds-current, worlds-fit, worlds-lair, worlds-map, worlds-pass, worlds-save, worlds-solar | mp:worlds |
| co-op only | - | mp:hail, mp:research, mp:save, mp:sharedchoice, mp:timer, mp:trade, mp:wanted, mp:win |

Why the 4 single-player tests stay on the PC:

- `telemetry-basic`, `telemetry-run`: real time. The `perf` event fires every few real seconds (`Telemetry/Perf.cs` uses `Stopwatch`), and `teleflush` waits 10 game seconds for the writer thread. The headless sim runs faster than real time. Both pass in some runs and fail in others.
- `onboarding-hints`: failed in all 8 runs. `save onboarding` throws "Game saving is currently blocked". The test raises heat to 80, so 6 hunters arrive, and a delayed hit effect (`HitEffectRules`, `BlockGameSaving`) blocks the save 2 s later.
- `diplomacy-surrender`: failed in 4 of 5 suites. The hail of the beaten hunter ends (`CareerSimModeManager.ValidateShipHailing`) before `expect dip_hailing >= 1`, 1.5 s later.

The Mac draws the same hunters in every run (`diplomacy-surrender`: Swift, Pogoo, Spotter). The PC draws different hunters in each run, because it runs in real time. So a Mac failure that depends on the draw repeats, and the PC retry hides it. Both tests are fragile on the PC too (`onboarding-hints` is in `flaky.log`). A fix in the tests (no combat before the save, a `waitfor` for the hail) can move them back to the Mac.

## Evidence

| Check | Result |
|---|---|
| Load `Cosmoteer.dll`, `HalflingCore.dll` | Works after an IL-only rewrite (see "Seams"). |
| Harmony 2.4.3 on macOS arm64 | Works. Hot Cargo patches 146 methods, no errors. The PC run of the same main build also logs 146. |
| Vanilla rules plus the Hot Cargo mod | Load in 5 to 9 s. Mod load errors: 0. |
| Galaxy | Same as on the PC after the copy of the `*.markov` files: `core.txt` starts at "Corerio" on both. |
| Repeatability | Two runs of `core.txt` log identical sim values to full float precision (for example heat `37.06439971923828`). Tests with combat can still differ from run to run, because some game work runs on background threads in real time. |
| One run of `core.txt` | 21 s wall time (boot, career creation, script). On the PC the same script took 165 s (100 s of it waiting for a slot). |
| 8 tests in parallel | 52 s wall time. Peak memory about 2 GB per run. |
| All 79 single-player tests | 487 s wall time, 8 in parallel. |

## Seams

Each item names the blocker by type and method, and the workaround in the host.

Assembly loading (`GameAssemblies.cs`):

1. `Cosmoteer.dll` and `HalflingCore.dll` are win-x64 ReadyToRun images. CoreCLR on arm64 refuses them ("The data is invalid"). The host rewrites each one with Mono.Cecil to an IL-only AnyCPU copy and loads that copy. The JIT then compiles the IL for arm64. Parallel runs write the copy under a temp name per process.
2. `Steamworks.NET` 2025.163 is not on NuGet. The host uses 2024.8.0 and adds a throwing stub for each type and method that the game references but 2024.8.0 lacks (3 methods, 1 type). The stubs are needed because the JIT compiles whole methods, also the branches behind `Steam.IsInitialized`.
3. `Cosmoteer.Gui.ErrorBox` derives from `System.Windows.Forms.Form`. `Assembly.GetTypes()` on the game then throws, and `HotCargo.Telemetry.TelemetryPatches.ModuleMethods` calls it. The host loads the WinForms reference assemblies (NuGet `Microsoft.WindowsDesktop.App.Ref`) as stubs that throw.
4. The other game dependencies (IronPython, NAudio, NVorbis, ImageSharp and others) come from NuGet at the exact versions in `Cosmoteer.deps.json`. `gen-deps.py` writes `GameDeps.props` from that file.

Platform and game loop (`HeadlessPlatform.cs`, `Boot.cs`, `Program.cs`):

5. `Halfling.Platforms.Platform.Create` loads `HalflingPlatformWDX` (Direct3D 11, XAudio2, Win32). The host patches it to return `HeadlessPlatform`. That class returns the game's own null devices (`NullWindow`, `NullGraphicsManager`, `NullAudioManager`, `NullInputManager`), the same ones `--headless` uses on the PC.
6. `RealTimeClock.AdvanceRealTime` reads the real time. The host advances it by 1/60 s per frame. The game then runs faster than real time.
7. `GameRoot.JumpToNode` loads the new system on a background thread (about 4 s). A test waits for it in game seconds, and the fast clock ends the wait first. While a jump of the current game runs, each frame takes at least 1/60 s of real time. A jump with the FTL animation of a game that a test covers with a new game never ends (the animation waits for drawn frames), so it stops counting; before that fix, the rest of the process ran about 5 times slower.
8. `GameApp.Main` reads `--cores` and sets `FastParallel.ThreadCount`. The host skips `Main`, so `Program.cs` reads `--cores`. Without it, each run starts one spinning helper thread per core.
9. An exception that leaves the game loop after `AUTOTEST DONE` (for example a cancelled task in `StasisDoodadSpawner.CancelPreload` at shutdown) does not fail the run. The log decides, like on the PC.

Native libraries and missing files (`NativeSeams.cs`):

10. `TextureData.LoadFrom` decodes images with FreeImage (TeximpNet, Windows native). The host decodes with ImageSharp (managed, a game dependency). Ship files decode this way, because a ship design is stored in the PNG pixels.
11. `TextureData.Compress` calls nvtt (TeximpNet, Windows native). The host skips it.
12. `GameApp.CreateGraphicsManager` sets `TextureCache.LoadTexture` as the texture loader. The host replaces it: a missing PNG becomes a blank placeholder that is big enough for the requested mip levels.
13. `SoundStream.FromFile` opens `*.wav`, `*.ogg` and `*.music` files, which exist only on the PC. A missing file becomes a short silent stereo stream.
14. `ShipRoofRules.LoadRoofTextures` and `BuildRoofDecalsAtlas` list PNG folders. The host adds one placeholder for the default roof texture and skips the decal atlas. `DecalsManager.LoadSaveData` (ship paint) is skipped.
15. `TextureData.SaveTo` encodes images with FreeImage. Co-op setup sends the ship icon as PNG bytes; the host writes a blank PNG with ImageSharp.
16. Linux file names keep letter case; Windows and macOS ignore it, and the vanilla rules rely on that (`<tractor_Beam.rules>` for `tractor_beam.rules`, `Ghost hardware.ship.png`). CI puts the game files on an ext4 folder with case folding (`mkfs.ext4 -O casefold`, `chattr +F`), so every file access ignores case as on Windows.

The spike also had fallbacks for missing ship designs and name generator files. `scripts/pull-game.sh` now copies the real files, so the fallbacks are gone. A missing file now fails the run. Limit: `tar.exe` skips one design-contest ship whose name is outside the Windows ANSI code page. No test needs it.

Not a blocker: `FreezeDetector` tries to start `Cosmoteer.exe` for a freeze log when one frame takes more than 10 s (career creation). It logs the failure and continues.

## Startup time

The JIT compiles all game code, because the ReadyToRun code is gone (seam 1). `HeadlessSim.csproj` turns tiered compilation off: the JIT optimizes each method once, with no tier-0 code and no background re-JIT.

| Setting | One run of `core.txt` | 8 tests in parallel |
|---|---|---|
| Default (tiered, PGO on) | 27 s | 70 s |
| `TieredPGO` off | 23 s | 61 s |
| `TieredCompilation` off (in use) | 21 s | 52 s |

Not tried: crossgen of the IL-only copies for osx-arm64, a warm pool of booted hosts.

## Open items

- CI: the private game-refs repository holds only the DLLs. CI would also need `Data/` and the ship files there.
- The headless sim does not prove that the Windows build works. The release gate stays a full PC pass (`scripts/release.sh`).

Risks:

- Sim values on the Mac need not equal PC values (arm64 and x64 floating point, a fixed clock). Assertions with ranges are fine. The co-op state hash compares two processes on the same Mac, so it stays exact.
- The host depends on private game members (publicized). A game update can rename them. Each seam then fails at boot with a clear Harmony error.
- Placeholder art: code that reads texture pixels or sizes for game logic gets blank 1x1 to 64x64 images. No such case showed up in the tests.
