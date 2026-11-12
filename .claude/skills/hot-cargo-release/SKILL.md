---
name: hot-cargo-release
description: Publish a Hot Cargo GitHub release with the player zip (mod, loader, install.bat). Use when main has a full clean test pass.
---

# Release

1. Merge into `main`, push, and run `scripts/autotest-all.sh 400` on the clean tree. A full clean pass writes the marker for that commit. A later commit that changes only `*.md` or `docs/` keeps the marker valid.
2. `scripts/release.sh <version> --prerelease`. It checks the marker, builds `hot-cargo-<version>.zip` with `scripts/package.sh`, tags the commit, pushes the tag and publishes the release with a change list.
3. Install the release into the player's game (`hot-cargo-install`).

The zip holds the mod folder, `HotCargoLoader/`, `install.bat`, `uninstall.bat` and `INSTALL.md`. The installer finds the game through Steam and the Mods folder through Saved Games.

Dev prereleases need no action: CI publishes one for each push to `main` (see `CONTRIBUTING.md`, Releases). They are not game tested. A tested release is the one that this skill publishes.
