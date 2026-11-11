# Hot Cargo — install

You stole corporate tech. The corporation wants it back. Get it to the Buyer before the hunters catch you.

## Needs

- Windows, Cosmoteer from Steam, game version 0.30.5a.
- In co-op, every player needs the same Hot Cargo version.

## Install

1. Close Cosmoteer.
2. Unzip this folder anywhere.
3. Double-click `install.bat`.
4. Start Cosmoteer, open **Mods**, turn on **Hot Cargo**, and restart the game when it asks.
5. Start a new career. The map size "Hot Cargo run" is the default.

After a Cosmoteer update or a Steam "verify files", run `install.bat` again.

## What the installer changes

- It copies the mod to `Saved Games\Cosmoteer\<your id>\Mods\hot_cargo`.
- It copies `HotCargo.Loader.dll` to `<Cosmoteer>\Bin\HotCargoLoader`.
- It adds a .NET startup hook to `<Cosmoteer>\Bin\Cosmoteer.runtimeconfig.json` and keeps the original as `Cosmoteer.runtimeconfig.json.hot_cargo_orig`.

With the mod turned off in the Mods menu, the game plays vanilla.

## Uninstall

Close Cosmoteer and double-click `uninstall.bat`.

## Keys

- F8: show or hide the Hot Cargo panel.
- Debug keys (single player, off by default): F9 / Shift+F9 add or remove 10 heat, F10 starts the next heat stage, F11 / Shift+F11 add or remove 1 tech.
  To turn them on, add this to `Mods\hot_cargo\tunables.rules`: `Debug { DebugKeys = true }`.

## Tuning

Edit `Mods\hot_cargo\tunables.rules` while the game runs. `tunables.defaults.rules` next to it lists every value.
A comms window pauses the game in single player. To turn that off, add `Ux { PauseOnComms = false }`.
