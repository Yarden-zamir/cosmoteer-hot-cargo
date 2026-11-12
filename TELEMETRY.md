# Telemetry

Hot Cargo records each game session to a JSONL file, so test runs and playtests can be analyzed afterwards.
Spec: SPEC.md section 28 (requirements 530 to 545). Code: `src/HotCargo/Telemetry/`. Analysis: `tools/telemetry/`.

## Files

- Folder: `<mod folder>/telemetry/` on the PC, for example `%USERPROFILE%\HotCargoSlots\t3\Mods\hot_cargo\telemetry\` for a test slot.
  `scripts/deploy.sh` keeps this folder.
- One file for each session: `<run id>__<session id>.jsonl`. A session is one game from its start or load until it closes.
- A run is one career. The run ID is in the career save (path `HotCargoTelemetry`), so a loaded game continues the run ID.
  In co-op the host's save goes to the clients, so all machines of a game share the run ID.
- A file that reaches `MaxFileMegabytes` continues in `<run id>__<session id>.part<N>.jsonl`. Each part starts with a `file.part` line.
- At session start the oldest files go until the folder is below `MaxFolderMegabytes`.

## Event format

Each line is one JSON object. Every event has these fields:

| Field | Meaning |
|---|---|
| `e` | Event name, for example `heat.change`. |
| `t` | Real seconds since the session started. |
| `gs` | Game seconds of the run (`RunState.GameSeconds`). It stops after a win. A load continues from the save. |
| `ss` | Sim seconds of this session. It keeps counting after a win. |
| `sec` | Sector index. |
| `sys` | System name. |
| `tier` | Danger tier of the system (low tier). |
| `arch` | Sector type ID, for example `hc_trade_hub` or `basic`. |
| `heat` | Heat after the event. |
| `tech` | Fleet tech count of the last sim tick. |

Numbers have at most 3 decimals.

## Events

Category switches are in the `Telemetry` tunables section. The table names the switch of each event.

| Event | Switch | Fields |
|---|---|---|
| `session.start` | Run | `schema`, `run`, `session`, `session_no`, `new_run`, `loaded`, `wall_utc`, `game_version`, `mod_version` (`1.0.0+<commit>`), `tunables_hash` (of the file), `tunables_full_hash` (of the values in effect), `seed`, `map`, `map_size_index`, `combat_difficulty`, `econ_difficulty`, `crew_scarcity`, `money_reward_factor`, `fame_reward_factor`, `players`, `local_player`, `mp`, `host`, `autotest`, `label`, `cores`, `os`, `tunables` (every tunable value) |
| `session.end` | Run | `reason` (`game_closed`, `game_closed_after_loss`, `replaced`, `process_exit`), `result`, `real_s`, `sim_s`, `events`, `dropped`, `overhead_ms`, `overhead_pct_wall`, `writer_ms`, `frames`, `frame_ms`, `ticks`, `tick_ms`, `trace_missing` |
| `run.new`, `run.loaded` | Run | `starting_tech`; `result`, `sessions` |
| `run.save` | Run | `caller` (who saved: a save, an autosave, a co-op sync) |
| `run.jump` | Run | `from`, `stay_s`, `open_contracts`. A snapshot follows. |
| `system.enter` | Run | `from`, `first_visit`, `prev_stay_s`, `prev_stay_real_s`, `prev_missions_done`, `prev_missions_accepted`, `heat_before`, `stages`, `peak_tier`, `visited` |
| `system.poi` | Run | `poi` |
| `run.won`, `run.lost` | Run | `reason`, `score`, `delivered`, `notoriety`, `peak_tier`, `money` (per player). A snapshot follows. |
| `heat.change` | Heat | `src`, `caller`, `amount` (asked), `delta` (applied), `before`, `after`, `stages` |
| `heat.accrual` | Heat | `window_s`, `time`, `trail` (heat summed over the window), `mult`, `trail_per_min` |
| `heat.stage` | Heat | `stage`, `name`, `live_hunters`, `since_entry_s` |
| `heat.strike_wave` | Heat | `live_hunters` |
| `tech.count` | Tech | `from` (null for the first count of a session), `to`, `delta`, `cause`, `hints` |
| `tech.value` | Tech | `from`, `to`, `buyer_unit`, `buyback`, `notoriety`, `peak_tier` |
| `tech.vault_hit` | Tech | `ship`, `player`, `damage`, `health`, `max_health`, `tech_before`, `tech_after`, `destroyed` |
| `econ.money` | Economy | `player`, `amount`, `before`, `after`, `src`, `caller` |
| `econ.fame` | Economy | `amount`, `after`, `src`, `caller`, `std_mission_fame` |
| `econ.reputation` | Economy | `faction`, `from`, `to` |
| `econ.trade`, `econ.trade_pickup` | Economy | `trade` (Buy, Sell, SellBack, ...), `resource`, `amount`, `unit_price`, `total`, `tech`, `buyer`, `crew_player`, `from`, `from_side`, `to`, `to_side`, `tech_value`. Pickups only for tech. |
| `mission.offered` | Missions | mission fields, `list` (station, system, galaxy), `terms` |
| `mission.accepted` | Missions | mission fields, `terms`, `open_contracts` |
| `mission.ended` | Missions | mission fields, `result` (success, timeout, lost, fail, abandoned), `duration_s`, `terms`, `sold_out` |
| `event.shown` | Story | `event`, `serial`, `page_tier`, `page_tech_value`, `chain_page` |
| `event.choice` | Story | `serial`, `player`, `outcome` (all outcome rules fields) |
| `event.result` | Story | `serial`, `title`, `lines` (one line per effect) |
| `chain.start`, `chain.stage` | Story | `chain`, `station`, `from_stage`, `stage`, `part`, `ended`, `result`, `flags`, `story` |
| `combat.census` | Combat | `live` (ships by side), `factions`, `stasis_ships`. At the first visit of a system. |
| `combat.spawn` | Combat | ship fields, `from_stasis`. Every non-player ship that appears after the census. |
| `combat.destroyed` | Combat | `ship`, `name`, `player`, `side`, `faction`, `hunter`, `value`, `tags`. A ship that became a derelict. |
| `combat.removed` | Combat | ship fields, `src`, `caller`. A ship that left for good: a hunter that jumps out (`flee_jump`), a trader that exits. |
| `combat.crew_died` | Combat | `player`, `cause`, `carried_tech` |
| `combat.damage` | Combat | per side of the damaged ship: `damage`, `repaired`, `parts_lost`. Summed over `AccrualSeconds`. |
| `ai.switch` | Ai | `ship`, `name`, `side`, `ai`, `from`, `to` (module types), `integrity`, `dist` |
| `snap` | Snapshots | `why` (timer, jump, result, test), `result`, `stages`, `notoriety`, `peak_tier`, `tech_value`, `buyer_mult`, `heat_mult`, `fame`, `money`, `score`, `delivered`, `open_contracts`, `trail_per_min`, `mission_log`, `pending_informants`, `waiting_hunter_groups`, `events_seen`, `chains`, `crew`, `ships` (value, blueprint, integrity, parts, firepower, vaults, crew of each player ship), `fleet_value`, `fleet_blueprint`, `fleet_firepower`, `fleet_parts`, `vaults`, `live` (ships by side), `stasis`, `ai` (sums, see below) |
| `perf` | Perf | `window_ms`, `frames`, `fps`, `frame_mean_ms`, `frame_p50_ms`, `frame_p95_ms`, `frame_p99_ms`, `frame_max_ms`, `frames_over_33ms`, `ticks`, `tick_mean_ms`, `tick_max_ms`, `tick_total_ms`, `tele_ms`, `tele_pct_wall`, `tele_pct_tick`, `tele_writer_ms`, `tele_events`, `tele_bytes`, `tele_pending`, `tele_dropped`, `gc0`, `gc1`, `gc2`, `gc_pause_ms`, `heap_mb`, `working_set_mb` |
| `mp.players` | Coop | `from`, `to` |
| `mp.desync` | Coop | `input_tick`, `host`, `expected` (a test resync) |
| `mp.game_loaded` | Coop | `host`, `players`. A co-op game that the host's state rebuilt: a join or a resync. |
| `log` | Log | `level`, `msg`: every `hot_cargo_log.txt` line |
| `trace` | Trace | `method`, `args`, `result`, `ms`, `ctx` |

Mission fields: `mission`, `type`, `name`, `category`, `faction`, `tier_min`, `tier_max`, `reward_money`, `reward_fame`, `time_left_s`.
Ship fields: `ship`, `name`, `player`, `side` (player, hunter, hostile, neutral, derelict), `faction`, `tags`, `hunter`, `value`, `parts`, `ai`, `dist` (from the fleet center).
Terms: `fee`, `bonus`, `flat_heat`, `trail`, `rival`, `informant`.

### Sources and causes

- `src` of a heat, money or fame change comes from the code that called it. Run.OnTick and ContractBook.OnTick add heat every tick, so they are marked without a stack walk (`time`, `trail`). Other changes walk the stack and name the first frame that a rule in `TelemetryPatches.Sources` matches: for example `fame`, `contract_accept`, `informant`, `escort_cover`, `event`, `chain`, `mission_reward`, `contract_bonus`, `trade`, `build`, `build_refund`, `crew_hire`, `surrender`, `test`, `debug`. Without a match it is the type of the first frame. `caller` holds up to 3 frames, innermost first, joined with ` < `.
- Above `CallerLookupsPerSecond` stack walks in one real second, `src` is `rate_limited`.
- `cause` of a tech count change is the newest hint within 5 game seconds: `sale`, `buyer`, `buyback`, `vault_damage`, `vault_destroyed`, `ship_lost`, `crew_death`, `event`, `starting_vault`, `test`, `debug`. `initial` is the first count of a session. `other` means no hint.

### AI sums

The `ai` object of a snapshot holds sums for the session:

- `<StrategyModule>`: `ticks` (AI ticks that this module drove the ship) and `switches` (ships that switched to it). The game asks the strategy modules from last to first, and the first that returns true drives the ship. That module is the decision of the tick.
- `tactic:<TacticsModule>`: AI ticks with that tactic active (`UpdateAttackAngle` or `UpdateAttackRadiusAndRotation` returned true).
- `focus_evals`, `focus_boosted`: target scores, and the ones that focus fire (spec 200) raised.

`ai.switch` events log the switches one by one, at most one per ship per `AiSwitchMinSeconds` and `AiEventsPerMinute` in total. The sums count every switch.

## Tunables

Section `Telemetry` in `tunables.rules`. Defaults:

| Tunable | Default | Meaning |
|---|---|---|
| `Enabled` | true | Master switch. |
| `Run`, `Heat`, `Tech`, `Economy`, `Missions`, `Story`, `Combat`, `Ai`, `Snapshots`, `Perf`, `Coop`, `Log`, `Trace` | true | Category switches. |
| `SnapshotSeconds` | 30 | Game seconds between snapshots. |
| `PerfSeconds` | 10 | Real seconds between perf events. |
| `AccrualSeconds` | 5 | Game seconds over which time heat, trail heat and damage are summed. |
| `ScanSeconds` | 5 | Game seconds between scans for new offers and ships that spawned into stasis. |
| `AiSwitchMinSeconds`, `AiEventsPerMinute` | 3, 600 | Limits of `ai.switch` events. |
| `CallerLookupsPerSecond` | 50 | Limit of stack walks for `src`. |
| `MaxFileMegabytes`, `MaxFolderMegabytes` | 32, 1024 | File rotation and folder size. |
| `TraceMethods` | see `TelemetryTunables.cs` | Methods for `trace` events, as `Namespace.Type:Method`. Read at the first game start; a change needs a restart. A missing method is skipped with a log line. |

Telemetry is not sim state. Each co-op machine writes its own file with its own switches, and the switches can differ without a desync.

## Cost

- Game threads only build a line and add it to a list under a short lock. A background thread writes and flushes once per second.
- Telemetry measures its own time on game threads (every patch, handler and event build) and reports it in `perf` (`tele_ms`, `tele_pct_wall`) and `session.end` (`overhead_pct_wall`). At a steady frame rate the share of real time is the share of frame time. Target: well below 1 %.
- Headless test games run at about 400 frames per second without drawing, so their frame times are no measure of play frame times. The share of real time is.
- Limit: a hard kill of the game loses the lines of the last second.

## Pull and analyze

1. Pull: `scripts/pull-telemetry.sh [SLOT...]`. Without a slot it pulls the main slot and every test slot into `../.telemetry/<slot>/` (outside the repo). Files that are already here with the same size are skipped.
2. Analyze: `cd tools/telemetry && uv run hc-telemetry`. It reads `../.telemetry` and writes `../.telemetry/report/report.md` and `report.html` (self-contained, charts inline).
   Options: `--dir`, `--out`, `--run <id>`, `--latest N`, `--since <UTC ISO time>`, `--no-autotest`, `--min-game-seconds`.
3. Tests and the whole test pass: `scripts/autotest-all.sh --telemetry [timeout]` pulls the telemetry after the tests and writes a report of the runs that the pass made.

The report has, for each run: facts (versions, tunables hash, difficulty, seed), heat by source, stage starts, a table per sector visit (stay, heat in and out, stages, missions, hunters, sales), money and fame by source, power and money over time, tech changes by cause, tech trades, missions by type with results and time to finish, events and chains, ships by side, hunter outcomes, damage by side, AI module frequencies, performance and errors. Across runs: a run table, pooled heat sources and findings.

For your own analysis, each line is one JSON object: for example `jq -c 'select(.e=="heat.change")' file.jsonl`.

## Autotest

`scripts/autotest.sh` and `scripts/autotest-mp.sh` send `telename <script name>` first, so `session.start` has the test name in `label`.

- Commands: `telename <name>`, `teleflush` (wait until the writer wrote every queued event), `telesnap [why]`, `telecount <event> <op> <n>` (counts events of the current run in its files), `telestats` (logs the overhead numbers).
- Fields: `tele_active`, `tele_events`, `tele_dropped`, `tele_nested_dropped`, `tele_overhead_ms`, `tele_overhead_pct` (telemetry time in percent of the session's real time), `tele_trace_missing`, `tele_trace_patched`.
- Tests: `tests/autotest/telemetry-basic.txt` (every category and a save and load), `tests/autotest/telemetry-run.txt` (a scripted run over five systems that ends with a win; it makes data for a report).

## Add an event

1. Call `Tele.Emit(Cat.<Category>, "<area>.<name>", w => { ... })` from read-only code. Pass the state as an argument (`Tele.Emit(cat, name, state, static (w, s) => ...)`) on hot paths, so no closure allocates.
2. Read state only. Do not call code that changes sim state, and do not use sim random sources.
3. Add the event to the table above.
