## 17. Architecture

```
mod/                       rules content; scripts/assemble-mod.sh builds mod.rules and strings from it
  mod.header.rules         mod header
  tunables.rules           changed tunables only (live reload)
  <area>/                  one folder for each area, named like its src/HotCargo folder in lowercase
  <area>/actions.rules     the Actions of each area
  <area>/strings.en.rules  the strings of each area
  missions/mechanics/      mechanics missions (sysgen_mechanics.rules), Helion Ore, Prototype Core
  tactics/ai_hunter.rules  hunter AI (HotCargoHunter)
  tactics/ai_vanilla.rules enemy AI modules and tactics that actions.rules appends to the vanilla combat AIs
src/HotCargo.Loader/       startup hook entry point, assembly resolve
src/HotCargo/
  Mod.cs                   Harmony setup, area registration
  Core/                    Run, RunState (save), Tunables, TunablesFile (live reload, validation), HeatModel,
                           CareerPatches (lifecycle), Activation (requirement 63), Errors, SimQueries, DebugKeys
  <Area>/                  one folder for each area: <Area>Area.Register, <Area>Hub.cs (its Tunables and RunState fields), tunables, patches
  Ai/                      enemy AI behaviors (flee, call, regroup, focus) and personalities; TacticsArea registers it, Tactics tunables
  ShipAi/                  player ship tactics: model (ShipAiModel), per-ship state and save hooks (ShipTactics), sim controller, MP input, editor
  Autonomy/                autonomous ships (section 45): Computer manning and ownership, Funnel, cargo and trade (AutonomyModel, AutonomyWorld)
  Pursuit/                 the Pursuit director: wave ramp, fleet factor, big event pacer (Pacer), reporting scout, relay blockade, network outage, hacker
  Debt/                    debt (section 55): loans, interest, collectors, the hostile lender, the comms pages and shared choice domain
  StartJobs/               start jobs (section 48): the offer, the job rules, the intro panel with the jobs, shared choice domain
  RunRecord/               run record (section 49): past runs in the save folder, rows in the career log tab
  Debris/                  debris decay (section 50): old loose junk glows, then burns up (DebrisModel, DebrisGlow)
  Friendly/                friendly encounters (section 54): the outcome effects of friendly ship pages (join, and later ones)
  Ui/                      overlay, heat panel (areas add lines through HeatPanel.ExtraLines), run intro and end panels
  Dev/Autotest.cs          headless test commands
  Telemetry/               session telemetry files (TELEMETRY.md); read-only hooks and patches
tests/HotCargo.Tests/      fast unit tests for the pure model files
tests/autotest/            game tests that scripts/autotest.sh runs on the PC and scripts/autotest-sim.sh runs in the headless sim
scripts/                   pull-game, stage, deploy, run, stop, logs, autotest, autotest-sim, suite-wait, integrate, pull-telemetry, release
tools/headless-sim/             headless host that runs the game loop and autotests on the Mac (docs/headless-sim.md)
tools/telemetry/           telemetry analysis (uv project): markdown and HTML reports
```

Rules:

- The model files (`*Model.cs`, for example `HeatModel`) have no game types, so that they are testable on the Mac without the game.
- Patches stay thin. They call into the mod classes.
- One tunables record type. The overlay and the code read the same values.
