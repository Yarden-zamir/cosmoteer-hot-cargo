---
name: hot-cargo-screenshots
description: Take in-game screenshots of Hot Cargo in the player's game slot (the only slot with graphics), with the shared slot lock, and pull them to the Mac. Use for any visual check, README images or UI review.
---

# Screenshots

Headless test slots draw nothing. Screenshots need the player's own game slot (`SLOT=main`). The player may be using it.

1. Take the lock: `until mkdir /tmp/hot_cargo_main_slot.lock 2>/dev/null; do sleep 15; done; echo $$ > /tmp/hot_cargo_main_slot.lock/pid`.
2. Check the game is closed: `source scripts/pc.sh; SLOT=main; pc "$PS_PATHS"'Get-SlotProcess'` must print nothing. If it runs, release the lock and try later.
3. Deploy: `SLOT=main scripts/deploy.sh`.
4. Install a script as `autotest.txt` in the main slot mod folder: `pc "$PS_PATHS"'$in = [Console]::In.ReadToEnd(); [IO.File]::WriteAllText((Join-Path $modDir "autotest.txt"), $in)' < tests/screens/<name>.txt`. Scripts start with `render on` and use `screenshot NAME`.
5. Run: `SLOT=main scripts/run.sh` (minimized, no focus). Wait until `Get-SlotProcess` prints nothing.
6. Pull: `pc "$PS_PATHS"'tar.exe -cf - -C (Join-Path $modDir "screenshots") .' | tar -xf - -C <local folder>`. Look at the PNGs with the Read tool.
7. Release the lock: `rm -rf /tmp/hot_cargo_main_slot.lock`, also on failure.

Rules:
- Keep sessions short. Leave the player's settings and saves as you found them.
- Images for the repo go to `docs/screenshots/` as JPEG. Crop out anything personal: user names, paths, Steam names.
