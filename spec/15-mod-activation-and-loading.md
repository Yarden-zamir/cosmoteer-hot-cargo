## 15. Mod activation and loading

62. The C# code loads through a .NET startup hook. `scripts/deploy.sh` adds `STARTUP_HOOKS` to `Cosmoteer.runtimeconfig.json` and writes `Bin/steam_appid.txt`. It adds the hook to a copy of the game's own file, and a file without the hook (after a game update) becomes the new copy.
63. The C# code does nothing unless the Hot Cargo rules mod is enabled in the game's mod list. With the mod off, the game stays vanilla, including vanilla multiplayer.
64. A Steam "verify files" removes the hook. Run `scripts/deploy.sh` again to restore it.
