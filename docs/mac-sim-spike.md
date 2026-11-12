# Spike: Cosmoteer sim on the Mac

Question: can a .NET 10 host on macOS (arm64) run the real game logic with Hot Cargo, so that game tests do not need the Windows PC?

## Verdict: GO

The prototype in `tools/mac-sim/` boots the real `GameApp` without a window, loads the vanilla rules and the Hot Cargo mod, applies the Hot Cargo Harmony patches and runs the real game loop. Existing `tests/autotest/*.txt` scripts run unchanged. No game file and no Hot Cargo source changed.

The remaining failures come from game data that the Mac copy does not have (ship designs, name generator files), not from code. One copy step from the PC fixes that data gap.

## Evidence

Measured on this Mac (Apple Silicon, .NET 10.0.14), commit of this branch.

| Check | Result |
|---|---|
| Load `Cosmoteer.dll`, `HalflingCore.dll` | Works after an IL-only rewrite (see "Seams"). |
| Harmony 2.4.3 on macOS arm64 | Works. Hot Cargo patches 146 methods, no errors. The PC run of the same main build also logs 146. |
| Vanilla rules plus the Hot Cargo mod | Load in 5 to 9 s. Mod load errors: 0. |
| `core.txt` (career on the run map, heat, stages, fame heat, time heat, save, load) | PASS. 31 s wall time in total (build, boot, script). On the PC the same script took 165 s (100 s of it waiting for a slot). |
| Determinism | Two runs of `core.txt` log identical sim values to full float precision (for example heat `37.06439971923828`). |
| Parallel runs | 4 runs at the same time: 50 to 55 s each. 8 at the same time: about 60 s each. |
| `hunters-spawn`, `missions-offers`, `factions-prices`, `mechanics-salvage` | PASS. |
| `tech-drop`, `journey-jump`, `events-hail`, `worlds-map`, `shipai-basic` | FAIL, data gap (see below). All pass on the PC. |
| `telemetry-basic` | FAIL: the `perf` event fires every few real seconds (`Telemetry/Perf.cs` uses `Stopwatch`). The fixed-step clock runs faster than real time. |

The failures by cause:

- Name generator files (`*.markov`) are missing. The host makes names from the sim `Rand` instead. `MapNodesSpawner.DoSpawn` draws node names from the same `Rand` as node positions and seeds, so the galaxy differs from the PC galaxy for the same seed. The PC starts `core.txt` at "Corerio", the Mac at "Tau Zadofi". This explains the `journey-jump`, `events-hail` and `worlds-map` failures. Not yet verified with the real files.
- Ship designs (`*.ship.png`) are missing. The host loads the vanilla example ship (`StandardMods/example_mod/ships/Arclight.ship.png`) for every missing ship file. The starter ship then has no steel storage (`tech-drop`: "vault steel: added 0 steel"), and enemy ships are all the same design (`shipai-basic`).

## How it works

`tools/mac-sim/run.sh <args>` builds the host and Hot Cargo, stages the mod like `deploy.sh` stages a test slot, and runs the host with the game folder (`../.game`) as working directory.

- `run.sh` with no arguments: boot and load the rules, then exit.
- `run.sh autotest <script>`: run an autotest script in the real game loop. Exit code 0 only with `AUTOTEST DONE` and no `AUTOTEST FAIL` or `ERROR` line (same rule as `scripts/autotest.sh`).
- `MAC_SIM_SAVE=<folder>` keeps the save folder (logs, saves, `hot_cargo_log.txt`). Without it, each run uses a new temporary folder, so runs can go in parallel.

The host passes `--headless --nosteam --rootsavepath <folder>`, because `GameApp` and `Paths` read these from `Environment.GetCommandLineArgs`.

## Seams

Each item names the blocker by type and method, and the workaround in the host.

Assembly loading (`GameAssemblies.cs`):

1. `Cosmoteer.dll` and `HalflingCore.dll` are win-x64 ReadyToRun images. CoreCLR on arm64 refuses them ("The data is invalid"). The host rewrites each one with Mono.Cecil to an IL-only AnyCPU copy and loads that copy. The JIT then compiles the IL for arm64.
2. `Steamworks.NET` 2025.163 is not on NuGet. The host uses 2024.8.0 and adds a throwing stub for each type and method that the game references but 2024.8.0 lacks (3 methods, 1 type). The stubs are needed because the JIT compiles whole methods, also the branches behind `Steam.IsInitialized`.
3. `Cosmoteer.Gui.ErrorBox` derives from `System.Windows.Forms.Form`. `Assembly.GetTypes()` on the game then throws, and `HotCargo.Telemetry.TelemetryPatches.ModuleMethods` calls it. The host loads the WinForms reference assemblies (NuGet `Microsoft.WindowsDesktop.App.Ref`) as stubs that throw.
4. The other game dependencies (IronPython, NAudio, NVorbis, ImageSharp and others) come from NuGet at the exact versions in `Cosmoteer.deps.json`. `gen-deps.py` writes `GameDeps.props` from that file.

Platform (`MacPlatform.cs`, `Boot.cs`):

5. `Halfling.Platforms.Platform.Create` loads `HalflingPlatformWDX` (Direct3D 11, XAudio2, Win32). The host patches it to return `MacPlatform`. That class returns the game's own null devices (`NullWindow`, `NullGraphicsManager`, `NullAudioManager`, `NullInputManager`), the same ones `--headless` uses on the PC.
6. `RealTimeClock.AdvanceRealTime` reads the real time. The host advances it by 1/60 s per frame. The game then runs faster than real time, and sim results repeat from run to run.

Native libraries and missing files (`NativeSeams.cs`):

7. `TextureData.LoadFrom` decodes images with FreeImage (TeximpNet, Windows native). The host decodes with ImageSharp (managed, a game dependency). Ship files decode this way, because a ship design is stored in the PNG pixels.
8. `TextureData.Compress` calls nvtt (TeximpNet, Windows native). The host skips it.
9. `GameApp.CreateGraphicsManager` sets `TextureCache.LoadTexture` as the texture loader. The host replaces it: a missing PNG becomes a blank placeholder that is big enough for the requested mip levels.
10. `SoundStream.FromFile` opens `*.wav`, `*.ogg` and `*.music` files, which exist only on the PC. A missing file becomes a short silent stereo stream.
11. `ShipRoofRules.LoadRoofTextures` and `BuildRoofDecalsAtlas` list PNG folders. The host adds one placeholder for the default roof texture and skips the decal atlas. `DecalsManager.LoadSaveData` (ship paint) is skipped.
12. `MarkovGenerator` reads `*.markov` files. A generator without its file makes consonant-vowel names from the sim `Rand`.
13. `ShipFile.FilePath`: a missing ship file loads the example ship, under the original ship name.

Not a blocker: `FreezeDetector` tries to start `Cosmoteer.exe` for a freeze log when one frame takes more than 10 s (career creation). It logs the failure and continues.

## Effort to make it a test harness

Steps, in order:

1. Copy the missing data once from the PC. Add `*.ship.png` and `*.markov` to the file list in `scripts/pull-game.sh`. Then seams 12 and 13 can go, and the 5 data failures can be checked again. Optional: copy all PNG and audio files too, and drop seams 9 to 11 (slower boot: thousands of PNG decodes).
2. Add `scripts/autotest-mac.sh` that runs a list of scripts in parallel through `run.sh autotest`, with the same output as `autotest-all.sh`. Let `autotest-affected.sh` use it first and fall back to the PC.
3. Decide which tests stay on the PC: tests of real-time behavior (`telemetry-basic` `perf`), screenshots, co-op over UDP (not tried here), and the final release gate. The Mac sim does not prove that the Windows build works.
4. Startup speed: boot plus career creation take about 15 s, because the JIT compiles everything (the R2R code is gone). Options: a Release build of the host, a warm pool of booted hosts, or crossgen of the IL-only copies for osx-arm64.
5. CI: the private game-refs repository holds only the DLLs. CI would also need `Data/` and the ship files there.

Risks:

- Sim values on the Mac need not equal PC values (arm64 and x64 floating point, the data gap). Assertions with ranges are fine. Exact cross-machine comparisons (the co-op `MPSTATE` check) stay on the PC.
- The host depends on private game members (publicized). A game update can rename them. Each seam then fails at boot with a clear Harmony error.
- Placeholder art: code that reads texture pixels or sizes for game logic gets blank 1x1 to 64x64 images. No such case showed up in the scripts that ran.
