using System.Globalization;
using System.Runtime.CompilerServices;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Game.Multiplayer.MPInputs;
using Cosmoteer.Gui;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using Halfling.Graphics.Text;
using Halfling.Gui;
using Halfling.Gui.Components.Rects;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Dev;
using HotCargo.Hunters;

namespace HotCargo.ShipAi;

/// <summary>Player ship tactics (spec 400 to 412). Mod.Boot calls Register once at startup.</summary>
public static class ShipAiArea
{
    public static void Register() => AddAutotest();

    /// <summary>The tactics run in every career game, also after a win: the career goes on.</summary>
    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.OnGameStarting))]
    static class TickHook
    {
        static void Postfix(CareerGameModeManager __instance)
        {
            var game = __instance.Game;
            game.EventHub.Tick += ShipAiController.OnTick;
            game.Popping += (_, _) =>
            {
                game.EventHub.Tick -= ShipAiController.OnTick;
                TacticsEditor.Close();
            };
        }
    }

    /// <summary>Frame hook of the editor. UI only: it reads sim state and sends inputs.</summary>
    [HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.Update))]
    static class FrameHook
    {
        static void Postfix(CareerGameModeManager __instance)
        {
            if (Run.Current is not { } run || run.Mode != __instance) return;
            Errors.Guard("tactics editor", () => TacticsEditor.OnFrame(run));
        }
    }

    /// <summary>An "AI" box in the info row of the ships card, for one selected ship that the local player commands. A click opens the editor.</summary>
    [HarmonyPatch(typeof(ShipsCard), MethodType.Constructor, typeof(GameRoot), typeof(GameGui), typeof(IRectProvider))]
    static class CardButton
    {
        internal static readonly ConditionalWeakTable<ShipsCard, Button> Buttons = new();

        static void Postfix(ShipsCard __instance) => Errors.Guard("tactics card button", () =>
        {
            var b = new Button { Width = 56f };
            b.CopySettingsFrom(WidgetRules.Instance.FlatButton);
            b.Width = 56f;
            b.StateNormalTextRenderer.XmlFormatting = true;
            b.StateHighlightedTextRenderer.XmlFormatting = true;
            b.StatePressedTextRenderer.XmlFormatting = true;
            b.ToolTips.TextProvider = new Ux.LiveText(() => Strings.GetText("HotCargoShipAi/CardTip"));
            b.Clicked += (_, _) => Errors.Guard("tactics card click", () =>
            {
                if (Run.Current is { } run && run.Game.Sim.PlayerInput.SelectedCommandableShips.OrderBy(s => s.UniqueID).FirstOrDefault() is { } ship)
                    TacticsEditor.Open(run, ship);
            });
            __instance._infoIconsBox.Children.Add(b);
            Buttons.AddOrUpdate(__instance, b);
        });
    }

    [HarmonyPatch(typeof(ShipsCard), nameof(ShipsCard.UpdateForSingleShip))]
    static class CardUpdate
    {
        static void Postfix(ShipsCard __instance, Ship ship)
        {
            if (!CardButton.Buttons.TryGetValue(__instance, out var b)) return;
            b.SelfActive = Run.Current is not null && ship.Commands.IsCommandableByLocalPlayer && ship.IsHumanPlayerShip;
            if (!b.SelfActive) return;
            bool auto = ShipTactics.Of(ship) is { Auto: true };
            b.Text = Strings.GetText(auto ? "HotCargoShipAi/CardOn" : "HotCargoShipAi/CardOff");
        }
    }

    // ---- Autotest

    static SimRoot Sim() => Autotest.RequireRun().Game.Sim;

    /// <summary>The ship that the test commands act on: tactics_ship picks it by index among the acting player's ships (by ID).</summary>
    static int _shipIndex;

    static Ship TestShip()
    {
        var sim = Sim();
        int player = Autotest.ActingPlayerIndex;
        var own = sim.Ships.HumanPlayerShips.Where(s => s.Commands.CanExecuteCrewCommandsFrom(player)).OrderBy(s => s.UniqueID).ToList();
        if (_shipIndex >= own.Count) throw new InvalidOperationException($"no own ship {_shipIndex}; the player has {own.Count}");
        return own[_shipIndex];
    }

    static ShipTactics Tactics() => ShipTactics.Of(TestShip()) ?? throw new InvalidOperationException("the test ship has no tactics");

    static string DesignPath(string name)
    {
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException($"bad design name '{name}'");
        string dir = Path.Combine(Mod.ModRoot, "testships");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name + ".ship.png");
    }

    static Ship? _loadedDesign;

    static string RowsText(string arg) =>
        arg == "clear" ? "" : Enum.TryParse<Preset>(arg, out var p) && !char.IsDigit(arg[0]) ? ShipAiModel.Encode(ShipAiModel.PresetRows(p)) : arg;

    static void AddAutotest()
    {
        Autotest.Fields["tactics_rows"] = () => ShipTactics.Of(TestShip())?.Rows.Count ?? 0;
        Autotest.Fields["tactics_auto"] = () => ShipTactics.Of(TestShip()) is { Auto: true } ? 1 : 0;
        Autotest.Fields["tactics_hash"] = () => ShipAiModel.StableHash(ShipTactics.Of(TestShip())?.Text ?? "");
        Autotest.Fields["tactics_target_row"] = () => Tactics().TargetRow;
        Autotest.Fields["tactics_move_row"] = () => Tactics().MoveRow;
        Autotest.Fields["tactics_matched"] = () => Tactics().Matched;
        Autotest.Fields["tactics_waiting"] = () => Tactics().Waiting ? 1 : 0;
        Autotest.Fields["tactics_evals"] = () => ShipAiController.Evaluations;
        Autotest.Fields["tactics_held"] = () => TestShip().Weapons.Count(w => !w.HasPermissionToFire());
        Autotest.Fields["tactics_weapons"] = () => TestShip().Weapons.Count();
        // Weapons with a sim-given part target on the tactics target, and how many of them aim at a weapon or thruster part.
        Autotest.Fields["tactics_aimed"] = () => AimedParts().Count;
        Autotest.Fields["tactics_aimed_not_weapons"] = () => AimedParts().Count(k => !k.Contains(PartKind.Weapon));
        Autotest.Fields["tactics_aimed_not_engines"] = () => AimedParts().Count(k => !k.Contains(PartKind.Thruster));
        // Weapons that the rows let fire (not held by HoldFire or PointDefenseOnly).
        Autotest.Fields["tactics_unheld"] = () => TestShip().Weapons.Count() - (Tactics().Held?.Count ?? 0);
        // The test ship's current command: 0 none, 1 attack, 2 move, 3 follow, 4 local FTL jump, 5 other. Negative = a player gave it.
        Autotest.Fields["tactics_cmd"] = () =>
        {
            var c = TestShip().Commands.CurConfirmedCommand;
            int kind = c switch { null => 0, AttackCommand => 1, MoveCommand => 2, FollowCommand => 3, FtlLocalJumpCommand => 4, _ => 5 };
            return c?.UniqueID.PlayerID is not null ? -kind : kind;
        };
        Autotest.Fields["tactics_follow_radius"] = () => (TestShip().Commands.CurConfirmedCommand as AttackCommand)?.EdgeToEdgeFollowRadius ?? -1;
        // The tactics target: 1 hunter with the most health, 2 the least, 0 another ship, -1 none.
        Autotest.Fields["tactics_target"] = () =>
        {
            if (Tactics().Target is not { } id) return -1;
            var hunters = HunterSpawner.Hunters(Sim()).ToList();
            if (hunters.Count < 2) return 0;
            if (hunters.MaxBy(HunterOrders.HealthFraction)!.UniqueID == id) return 1;
            return hunters.MinBy(HunterOrders.HealthFraction)!.UniqueID == id ? 2 : 0;
        };
        Autotest.Fields["tactics_editor"] = () => TacticsEditor.IsOpen ? 1 : 0;
        Autotest.Fields["design_rows"] = () => _loadedDesign is null ? -1 : ShipTactics.Of(_loadedDesign)?.Rows.Count ?? 0;
        Autotest.Fields["design_hash"] = () => ShipAiModel.StableHash(_loadedDesign is null ? "" : ShipTactics.Of(_loadedDesign)?.Text ?? "");
        Autotest.Fields["design_auto"] = () => _loadedDesign is not null && ShipTactics.Of(_loadedDesign) is { Auto: true } ? 1 : 0;
        Autotest.Fields["design_parts"] = () => _loadedDesign?.BlueprintParts.Count ?? -1;
        Autotest.Fields["ship_parts"] = () => TestShip().BlueprintParts.Count;

        // tactics_ship <index>: the test commands act on this own ship (by ID order). Default 0.
        Autotest.Commands["tactics_ship"] = arg => _shipIndex = int.Parse(arg, CultureInfo.InvariantCulture);
        // tactics <preset name|clear|HC1:...>: sets the rows of the test ship through the tactics input, like the editor. Auto keeps its value.
        // A real player action: run it without "mp".
        Autotest.Commands["tactics"] = arg =>
        {
            var ship = TestShip();
            Sim().RequireGame().NetManager.EnqueueInput(new TacticsInput(ship, RowsText(arg), ShipTactics.Of(ship)?.Auto ?? false));
            Autotest.Wait(1);
        };
        // tacticsauto on|off: the Auto switch of the test ship, through the tactics input.
        Autotest.Commands["tacticsauto"] = arg =>
        {
            var ship = TestShip();
            Sim().RequireGame().NetManager.EnqueueInput(new TacticsInput(ship, ShipTactics.Of(ship)?.Text ?? "", arg != "off"));
            Autotest.Wait(1);
        };
        // Human ships with Auto on, of every player (co-op checks).
        Autotest.Fields["tactics_any_auto"] = () => Sim().Ships.HumanPlayerShips.Count(s => ShipTactics.Of(s) is { Auto: true });
        // expecttacticsany <preset name|HC1:...>: some human ship, of any player, has exactly these rows.
        Autotest.Commands["expecttacticsany"] = arg =>
        {
            string want = RowsText(arg);
            var have = Sim().Ships.HumanPlayerShips.OrderBy(s => s.UniqueID).Select(s => ShipTactics.Of(s)?.Text ?? "").ToList();
            Autotest.Check(have.Contains(want), $"a human ship has tactics '{want}' (have: {string.Join(" | ", have)})");
        };
        // expecttactics <preset name|clear|HC1:...>: the test ship has exactly these rows.
        Autotest.Commands["expecttactics"] = arg =>
        {
            string want = RowsText(arg), have = ShipTactics.Of(TestShip())?.Text ?? "";
            Autotest.Check(want == have, $"tactics are '{want}' (have '{have}')");
        };
        // shipai <ShipAiTunables field> <value>: sets one tunable for this run. Test only.
        Autotest.Commands["shipai"] = arg => Autotest.SetField(Autotest.RequireRun().T.ShipAi, arg);
        // tactics_manual: a player move order 400 ahead of the test ship, sent like a right click. Real player action.
        Autotest.Commands["tactics_manual"] = _ =>
        {
            var ship = TestShip();
            var to = ship.DetWorldCenter + Vector2.FromAngle(ship.DetRotation) * 400f;
            var move = new MoveCommand(ship, ship.Commands.AllocLocalCommandID(), to, null, CollisionAvoidanceFlags.All, isTentative: false);
            Sim().RequireGame().NetManager.EnqueueInput(new MPEnqueueCommandsInput(move, append: false, playLocalSound: false));
        };
        // tactics_ally: a copy of the starting design as a second ship of the acting player, near the first one. Single player test only.
        Autotest.Commands["tactics_ally"] = _ =>
        {
            var run = Autotest.RequireRun();
            if (run.IsMultiplayer) throw new InvalidOperationException("tactics_ally is single player only");
            var sim = run.Game.Sim;
            var first = sim.Ships.HumanPlayerShips.Where(s => s.IsLocalPlayerShip).MinBy(s => s.UniqueID) ?? throw new InvalidOperationException("no own ship");
            using var stream = new MemoryStream();
            first.SaveDesign(stream);
            var ally = Ship.Load(stream.ToArray(), first.Metadata.PlainShipName + " II", first.Metadata.PlayerIndex, builtByPlayer: true, randSeed: 777UL);
            ally.WorldCenter = Autotest.ClearSpotNear(sim, first.DetWorldCenter, first.WorldBoundingRadius * 2f + 300f, ally.WorldBoundingRadius);
            sim.Ships.Add(ally);
            ally.InitializeToMax();
            Log.Info($"AUTOTEST tactics ally {ally.Metadata.PlainShipName} id={ally.UniqueID} human={ally.IsHumanPlayerShip}");
            Autotest.Wait(1);
        };
        // savedesign <name> [png]: saves the test ship's design. With png, through the game's own Save Ship (needs graphics: main slot).
        // Without png, the same design node goes to the file without the picture.
        Autotest.Commands["savedesign"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string path = DesignPath(parts[0]);
            var ship = TestShip();
            if (parts.Length > 1 && parts[1] == "png")
            {
                ship.SaveDesignAs(path, dialogOnError: false, out bool error);
                if (error) throw new InvalidOperationException($"SaveDesignAs failed: {path}");
            }
            else
            {
                using var file = File.Create(path);
                ship.SaveDesign(file);
            }
            Log.Info($"AUTOTEST saved design {path} ({new FileInfo(path).Length} bytes)");
        };
        // loaddesign <name> [vanilla]: loads a saved design like the ship library does. With vanilla, the tactics nodes are ignored,
        // like a game without the mod.
        Autotest.Commands["loaddesign"] = arg =>
        {
            var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            _loadedDesign?.Dispose();
            ShipTactics.VanillaRead = parts.Length > 1 && parts[1] == "vanilla";
            try { _loadedDesign = Ship.Load(DesignPath(parts[0]), blueprintsOnly: true); }
            finally { ShipTactics.VanillaRead = false; }
            Log.Info($"AUTOTEST loaded design {parts[0]}: parts={_loadedDesign.BlueprintParts.Count} tactics='{ShipTactics.Of(_loadedDesign)?.Text}'");
        };
        // loadreplace <name>: the build menu's "Load ship (replace)" blueprint copy from a saved design into the test ship. Its tactics follow.
        Autotest.Commands["loadreplace"] = arg =>
        {
            var ship = TestShip();
            using var prototype = Ship.Load(DesignPath(arg), blueprintsOnly: true);
            using var update = ShipUpdateInfo.Alloc(ship);
            update.CopyBlueprintsFromShip(prototype);
            Autotest.Wait(1);
        };
        // tactics_editor open|close|apply <preset>|add|paste <text>: drives the editor window like a click. Real player actions.
        Autotest.Commands["tactics_editor"] = arg => TacticsEditor.TestCommand(Autotest.RequireRun(), TestShip(), arg);
        // tactics_damage <fraction>: sets every part of the test ship to this health fraction. Single player test only.
        Autotest.Commands["tactics_damage"] = arg =>
        {
            if (Autotest.RequireRun().IsMultiplayer) throw new InvalidOperationException("tactics_damage is single player only");
            float f = float.Parse(arg, CultureInfo.InvariantCulture);
            foreach (var part in TestShip().Parts.ToList()) part.Health = Math.Max(1, (int)(part.Rules.MaxHealth * f));
        };
        // tactics_strings: every condition, action, preset and parameter name has a string.
        Autotest.Commands["tactics_strings"] = _ =>
        {
            var keys = Enum.GetNames<Cond>().Select(n => "Cond_" + n)
                .Concat(Enum.GetNames<Act>().Select(n => "Act_" + n))
                .Concat(Enum.GetNames<Preset>().Select(n => "Preset_" + n))
                .Concat(Enum.GetNames<PartKind>().Select(n => "PartKind_" + n))
                .Concat(Enum.GetNames<ShipClass>().Select(n => "ShipClass_" + n))
                .Concat(Enum.GetNames<GuardWho>().Select(n => "GuardWho_" + n));
            var missing = keys.Where(k => !Strings.TryGetText("HotCargoShipAi/" + k, out _)).ToList();
            Autotest.Check(missing.Count == 0, $"tactics strings exist (missing: {string.Join(", ", missing)})");
        };
        Autotest.Commands["tactics_log"] = arg =>
        {
            var ship = TestShip();
            var t = ShipTactics.Of(ship);
            Log.Info($"AUTOTEST TACTICS {arg}: ship={ship.Metadata.PlainShipName} auto={t?.Auto} waiting={t?.Waiting} target={t?.TargetRow} move={t?.MoveRow} " +
                     $"matched={t?.Matched} cmd={ship.Commands.CurConfirmedCommand?.GetType().Name} health={HunterOrders.HealthFraction(ship):0.00} text='{t?.Text}'");
        };
    }

    static List<HashSet<PartKind>> AimedParts()
    {
        var ship = TestShip();
        if (Tactics().Target is not { } id) return [];
        var result = new List<HashSet<PartKind>>();
        foreach (var w in ship.Weapons)
            if (w.ExplicitTargets.CurConfirmedTarget is { Target: Cosmoteer.Ships.Parts.Part p } c && c.UniqueID.PlayerID is null && p.Ship?.UniqueID == id)
                result.Add(ShipAiController.KindsOf(p.Rules));
        return result;
    }
}
