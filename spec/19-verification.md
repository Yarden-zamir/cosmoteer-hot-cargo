## 19. Verification

- **Mac**: `dotnet build` and unit tests for `HeatModel` and tunables parsing. These tests are fast and catch silent math errors.
- **PC smoke runs**: `scripts/run.sh` starts the game minimized. The mod writes a self-check to `hot_cargo_log.txt` at load: tunables parsed, patches applied, rules content found. Scripts read the log.
- **In game**: a new career in single player with debug keys. Checks: overlay values, heat growth, each stage spawn, sale price, vault loss, Buyer win, save and load.
- **Multiplayer**: co-op autotests (`scripts/autotest-mp.sh`, see CONTRIBUTING.md) run a host and a client on the PC and fail on any desync or state difference. A co-op session with two players checks the rest (PLAYTEST.md, Co-op).
- **Playtest checklist** for the user, in `PLAYTEST.md`.
