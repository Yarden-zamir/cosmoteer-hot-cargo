---
name: hot-cargo-new-area
description: Add a new Hot Cargo feature area with its entry point, tunables section, rules actions and strings. Use when a feature does not fit an existing area folder.
---

# New feature area

1. Code: `src/HotCargo/<Area>/` with `<Area>Area.Register()`. Add one call to it in `Mod.Boot`. Register subscribes to `Run.Hooks.*`, `DebugKeys.Extra`, and adds `Autotest.Commands[...]` and `Autotest.Fields[...]`.
2. Tunables: `src/HotCargo/<Area>/<Area>Tunables.cs` with `[ReflectiveSerialization(Explicit = true)]`. Add one field for it in `Core/Tunables.cs`. Every field: `[Serialize(Optional = true)]`, a default and a doc comment. Read live values through `Run.Current.T.<Area>`. The in-game settings window picks the fields up by reflection.
3. Rules: `mod/<area>/actions.rules` holds only Action objects `{ ... }`, without an outer list. `scripts/assemble-mod.sh` merges every area's actions into `mod.rules`. Paths are relative to the mod root, for example `&<area/file.rules>`.
4. Strings: `mod/<area>/strings.en.rules`. Use a top-level section that no other area uses.
5. Tests: `tests/autotest/<area>-*.txt`, co-op tests in `tests/autotest-mp/`, pure math in `tests/HotCargo.Tests`.
6. Spec: a new section and a new requirement number range in `SPEC.md`. Add the area to the architecture section and to `CONTRIBUTING.md`.
