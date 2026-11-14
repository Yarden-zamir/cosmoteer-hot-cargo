---
name: hot-cargo-screenshots
description: Take in-game screenshots of Hot Cargo in a windowed test slot on the PC (never the player's game slot) and pull them to the Mac. Use for any visual check, PR screenshots, README images or UI review.
---

# Screenshots

A PR agent takes its own screenshots in a test slot. Never use slot main: it is the player's game.

1. Write a script `tests/screens/<name>.txt`. It starts with `render on` (the game draws only with it) and uses `screenshot NAME`.
2. Run: `scripts/screens.sh tests/screens/<name>.txt <local folder> [timeout]`. It takes a free test slot (or `SLOT=tN`), deploys this worktree, starts the game with graphics, minimized, with the slot's own save folder, runs the script, and copies the PNGs and the log to the folder. PC calls need an active turn: a background Bash call and `t3_background_wait` for long scripts.
3. Look at the PNGs with the Read tool. For a PR, use the `gh-pr-images` skill.

While `.state/player-hold` exists, `screens.sh` refuses (exit 3): a window in a test slot could take focus or GPU time from the player. Headless tests stay allowed. Try again when the hold ends, or ask the coordinator.

Co-op screenshots: `scripts/screens-mp.sh tests/screens/<name>.host.txt tests/screens/<name>.client.txt <local folder>`. The host runs in a windowed test slot, the client in a headless test slot.

Rules:
- Keep sessions short.
- Images for the repo go to `docs/screenshots/` as JPEG. Crop out anything personal: user names, paths, Steam names.
