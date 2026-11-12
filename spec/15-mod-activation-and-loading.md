## 15. Mod activation and loading

62. The C# code loads through a .NET startup hook. `scripts/deploy.sh` adds `STARTUP_HOOKS` to `Cosmoteer.runtimeconfig.json` and writes `Bin/steam_appid.txt`.
63. The C# code does nothing unless the Hot Cargo rules mod is enabled in the game's mod list. With the mod off, the game stays vanilla, including vanilla multiplayer.
64. A Steam "verify files" removes the hook. Run `scripts/deploy.sh` again to restore it.
