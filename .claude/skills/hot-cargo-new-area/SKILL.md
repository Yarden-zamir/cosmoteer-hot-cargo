---
name: hot-cargo-new-area
description: Add a new Hot Cargo feature area with its entry point, tunables section, rules actions and strings. Use when a feature does not fit an existing area folder.
---

# New feature area

1. Code: `src/HotCargo/<Area>/` with `<Area>Area.Register()`. Add one call to it in `Mod.Boot`. Register subscribes to `Run.Hooks.*`, `DebugKeys.Extra`, and adds `Autotest.Commands[...]` and `Autotest.Fields[...]`.
2. Tunables: `src/HotCargo/<Area>/<Area>Tunables.cs` with `[ReflectiveSerialization(Explicit = true)]`. Add its field to the partial `Tunables` class in `src/HotCargo/<Area>/<Area>Hub.cs` (run state goes in the partial `RunState` class in the same file). Never edit `Core/Tunables.cs` or `Core/RunState.cs` for an area. Every field: `[Serialize(Optional = true)]`, a default and a doc comment. Read live values through `Run.Current.T.<Area>`. The in-game settings window picks the fields up by reflection.
3. Rules: `mod/<area>/actions.rules` holds only Action objects `{ ... }`, without an outer list. `scripts/assemble-mod.sh` merges every area's actions into `mod.rules`. Paths are relative to the mod root, for example `&<area/file.rules>`.
4. Strings: `mod/<area>/strings.en.rules`. Use a top-level section that no other area uses.
5. How to Play page and run log lines: see CONTRIBUTING.md, "How to Play pages and the run log".
6. Tests: `tests/autotest/<area>-*.txt`, co-op checks as `step <area>.<name>` in a session in `tests/autotest-mp/`, pure math in `tests/HotCargo.Tests`.
7. Spec: a new section file `spec/<next free number>-<name>.md` and a new requirement number range. Add the area to the architecture section and to `CONTRIBUTING.md`.
