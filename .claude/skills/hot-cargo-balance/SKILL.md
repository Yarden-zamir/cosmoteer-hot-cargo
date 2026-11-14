---
name: hot-cargo-balance
description: Change Hot Cargo balance defaults (rewards, prices, heat, fuel, hunter counts) with the economy model, the balance tests and the balance docs. Use when a change sets a new default for one of these values, or when a playtest or telemetry shows that the economy is off.
---

# Balance

A change in the player's running game on request is `hot-cargo-tune`. This skill changes the defaults in the repo.

## Sources

- `BALANCE.md`: the method, the findings, the table of changes, the dependencies on other areas and the open questions.
- `spec/35-balance.md` (requirements 300 to 305): the balance rules.
- `tools/balance/model.py`: the economy model. It reads the vanilla rules from `../.game/Data`, the heat stages from `src/HotCargo/Core/Tunables.cs` and three measurement logs. It writes `tools/balance/model.json`. Limit: the other tunable values are in its `OLD` and `NEW` sets, not read from the code.
- `tools/balance/render.py`: writes `docs/balance.html` from `model.json`.
- `tests/balance/*.txt`: the measurement scripts (mission boards, hunters). They are not game tests: CI and `autotest-affected.sh` do not run them. Their logs are in `tools/balance/logs/`.
- `tests/autotest/balance-*.txt`: the game tests of the balance rules. `balance-rules` is the smoke test.

## Change a default

1. Read the finding in `BALANCE.md` that the value belongs to, and the dependencies on other areas.
2. Change the default in the area's `*Tunables.cs` or in `mod/balance/actions.rules` (CLAUDE.md, Rules for code).
3. If the value is in the model, set it in `NEW` in `model.py`. Then run the model and `render.py` (commands in `BALANCE.md`, Method).
4. If the change alters the mission offers or the hunter ships, run the measurement scripts again first. They run on the PC.
5. Update the numbers in `BALANCE.md`: the finding and the row of the table of changes. Update the lines of spec 35 and of the area's own spec.
6. Update the expectations in `tests/autotest/balance-*.txt` and the unit tests of the `*Model.cs` files that the value changes. Run them (`hot-cargo-testing`).
7. When telemetry has real runs, compare them with the model (`hot-cargo-telemetry`).
