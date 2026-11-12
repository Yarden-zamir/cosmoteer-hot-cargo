## 13. Tunables, debug and live tuning

50. The C# code reads all numbers in requirements 1, 4, 7, 12, 17, 19, 23 to 48, 65, 67 to 78, 100 to 104, 130 to 141, 160 to 175, 190 to 202, 221 to 225, 301, 304, 305, 360 to 367, 400 to 412, 561 to 584, 593 to 595, 640 to 660, 680 to 688 and 723 from `tunables.rules`. `tunables.rules` holds only changed values. The mod writes all values and defaults to `tunables.defaults.rules` at game start.
51. The file uses the game's `.rules` format.
52. The mod watches the file. When the file changes, the mod reloads it on the next frame, without a game restart. The settings menu (requirement 500) writes the same file.
53. If the file has an error, the mod keeps the last valid values and shows the error in game. Errors include values that break sim logic later, for example a stage name that `Hunters.Stages` or a tactic does not match (`TunablesFile.Validate`).
54. Part, resource and mission definitions are mod `.rules` content. A change to them needs a game restart.
55. A heat panel shows heat as a bar with a mark for each stage, the heat rate (time heat with the zone factor, plus the contract trail), the open contracts and the trail rate when any are open, the fleet tech count, the tech value, the stage, the next stage and the sector index. After a win it shows the score. With debug keys on, it also shows the heat value, the heat multiplier and the tunables hash. F8 toggles it. With `Ux.HeatPanel = false`, a text overlay shows the same values. UI switches are in UX.md.
56. When `Debug.DebugKeys = true` (default false), debug keys work: add or remove heat, set the stage, add or remove tech units (F11 / Shift+F11, one unit), and spawn the next stage.
