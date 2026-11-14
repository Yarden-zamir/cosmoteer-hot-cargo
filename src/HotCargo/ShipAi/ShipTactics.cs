using System.Reflection;
using System.Runtime.CompilerServices;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Ships.Parts.Weapons;
using Halfling;
using Halfling.Geometry;
using Halfling.ObjectBits;
using Halfling.Serialization;
using Halfling.Serialization.Generic;
using Halfling.Timing;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.ShipAi;

/// <summary>
/// The tactics of one player ship and the state of its last decision (spec 400 to 412).
/// It lives on the Ship object and travels with the ship's own serialization: the career save, a co-op join or resync,
/// and the ship design file (rows and the Auto switch only). Vanilla reads ships by named paths, so the extra node does not
/// stop a save or a design from loading without the mod.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
internal sealed class ShipTactics
{
    public const string SavePath = "HotCargoTactics";
    const string DesignAutoPath = "HotCargoTacticsAuto";
    const string DesignCargoPath = "HotCargoTacticsCargo";

    /// <summary>The rows in ShipAiModel text form. Set only through SetRows, so Rows always matches it.</summary>
    [Serialize(Optional = true)] public string Text { get; private set; } = "";
    [Serialize(Optional = true)] public bool Auto;
    /// <summary>Spec 1062, 1063: the cargo settings in AutonomyModel text form. Set only through SetCargo.</summary>
    [Serialize(Optional = true)] public string CargoText { get; private set; } = "";
    /// <summary>Spec 1064: "return to me". The ship ignores its move rows and returns to the owner's first crewed ship.</summary>
    [Serialize(Optional = true)] public bool Recall;
    [Serialize(Optional = true)] public Time NextTick;
    /// <summary>A manual order is in the ship's queue, so the tactics wait (spec 408).</summary>
    [Serialize(Optional = true)] public bool Waiting;
    [Serialize(Optional = true)] public int TargetRow = -1;
    [Serialize(Optional = true)] public int MoveRow = -1;
    [Serialize(Optional = true)] public int FacingRow = -1;
    /// <summary>Spec 1067: the trade row of the last decision.</summary>
    [Serialize(Optional = true)] public int TradeRow = -1;
    /// <summary>Bit i = row i matched in the last decision. Weapon modes come from it (ShipAiModel.WeaponAct).</summary>
    [Serialize(Optional = true)] public int Matched;
    [Serialize(Optional = true)] public ObjectID? Target;
    /// <summary>Hold position: the spot that the ship keeps.</summary>
    [Serialize(Optional = true)] public Vector2? Anchor;
    /// <summary>The last command that the tactics gave. Only this command is ever removed by the tactics.</summary>
    [Serialize(Optional = true)] public CommandID? Issued;

    /// <summary>Spec 418: the term memory of the last decision, in term order (ShipAiModel.TermMemory as two lists).</summary>
    [Serialize(Optional = true)] public List<bool> MemoryOn = [];
    [Serialize(Optional = true)] public List<double> MemorySince = [];

    public TermMemory[]? Memory
    {
        get => MemoryOn.Count == 0 || MemoryOn.Count != MemorySince.Count ? null : MemoryOn.Zip(MemorySince, (on, since) => new TermMemory(on, since)).ToArray();
        set
        {
            MemoryOn = value?.Select(m => m.On).ToList() ?? [];
            MemorySince = value?.Select(m => m.Since).ToList() ?? [];
        }
    }

    List<TacticRow>? _rows;
    public IReadOnlyList<TacticRow> Rows => _rows ??= ShipAiModel.Decode(Text);

    /// <summary>Weapons that may not fire now (HoldFire, PointDefenseOnly). Derived from Matched and the part groups; null = build again.</summary>
    internal HashSet<Weapon>? Held;

    public bool IsEmpty => Text.Length == 0 && !Auto && CargoText.Length == 0 && !Recall;

    Autonomy.CargoSettings? _cargo;
    public Autonomy.CargoSettings Cargo => _cargo ??= Autonomy.Cargo.Decode(CargoText);

    /// <summary>Throws FormatException on bad text, before anything changes.</summary>
    public void SetCargo(string text)
    {
        var cargo = Autonomy.Cargo.Decode(text);
        CargoText = text;
        _cargo = cargo;
    }

    public bool[] MatchedRows => Enumerable.Range(0, Rows.Count).Select(i => (Matched & (1 << i)) != 0).ToArray();

    /// <summary>Throws FormatException on bad text, before anything changes.</summary>
    public void SetRows(string text)
    {
        var rows = ShipAiModel.Decode(text);
        Text = text;
        _rows = rows;
    }

    public void GetIntegrityHash(IntegrityHash iHash)
    {
        iHash.AddHash(ShipAiModel.StableHash(Text));
        iHash.AddHash(Auto ? 1 : 0);
        iHash.AddHash(ShipAiModel.StableHash(CargoText));
        iHash.AddHash(Recall ? 1 : 0);
        iHash.AddHash(Waiting ? 1 : 0);
        iHash.AddHash(Matched);
        iHash.AddHash(TargetRow * 10000 + MoveRow * 100 + FacingRow);
        iHash.AddHash(TradeRow);
        for (int i = 0; i < MemoryOn.Count; i++) iHash.AddHash(MemoryOn[i] ? 1 : 0);
        iHash.AddHash((int)(NextTick.Seconds * 10));
    }

    static readonly ConditionalWeakTable<Ship, ShipTactics> Store = new();

    public static ShipTactics? Of(Ship ship) => Store.TryGetValue(ship, out var t) ? t : null;

    public static ShipTactics For(Ship ship) => Store.GetValue(ship, _ => new ShipTactics());

    static void Attach(Ship ship, ShipTactics t) => Store.AddOrUpdate(ship, t);

    /// <summary>Test only: reads ignore the tactics nodes, like a game without the mod.</summary>
    internal static bool VanillaRead;

    static bool ReadOn => !VanillaRead && Activation.IsModEnabled();

    /// <summary>Checks the text of a loaded node. Bad text drops the tactics of that ship and shows the error; the ship still loads.</summary>
    static bool Valid(string text, string cargo, string where)
    {
        try
        {
            ShipAiModel.Decode(text);
            Autonomy.Cargo.Decode(cargo);
            return true;
        }
        catch (FormatException ex)
        {
            Errors.Report($"ship tactics {where}", ex.Message, ex);
            return false;
        }
    }

    // ---- Ship serialization: career save, co-op join and resync.

    [HarmonyPatch]
    static class WritePatch
    {
        static MethodBase TargetMethod() =>
            typeof(Ship).GetInterfaceMap(typeof(IGenericSerializable)).TargetMethods.Single(m => m.Name.EndsWith("WriteTo", StringComparison.Ordinal));

        static void Postfix(Ship __instance, GenericSerialWriter writer)
        {
            if (Of(__instance) is { IsEmpty: false } t) writer.WriteToPath(SavePath, t);
        }
    }

    [HarmonyPatch(typeof(Ship), MethodType.Constructor, typeof(GenericSerialReader))]
    static class ReadPatch
    {
        static void Postfix(Ship __instance, GenericSerialReader reader) => Errors.Guard("ship tactics read", () =>
        {
            if (!ReadOn || !reader.TryReadFromPath<ShipTactics>(SavePath, out var t) || t is null) return;
            if (Valid(t.Text, t.CargoText, "in a save")) Attach(__instance, t);
        });
    }

    [HarmonyPatch(typeof(Ship), nameof(Ship.GetIntegrityHash))]
    static class HashPatch
    {
        static void Postfix(Ship __instance, IntegrityHash iHash)
        {
            if (Of(__instance) is { } t) t.GetIntegrityHash(iHash);
        }
    }

    // ---- Ship design file (.ship.png): rows and the Auto switch.

    [HarmonyPatch(typeof(Ship), nameof(Ship.SaveDesign), typeof(OBNode))]
    static class SaveDesignPatch
    {
        static void Postfix(Ship __instance, OBNode node) => Errors.Guard("ship tactics design save", () =>
        {
            if (Of(__instance) is not { IsEmpty: false } t) return;
            App.ObjectBitsSerializer.Write(node.GetOrMakeChild(SavePath), t.Text);
            App.ObjectBitsSerializer.Write(node.GetOrMakeChild(DesignAutoPath), t.Auto);
            if (t.CargoText.Length > 0) App.ObjectBitsSerializer.Write(node.GetOrMakeChild(DesignCargoPath), t.CargoText);
        });
    }

    [HarmonyPatch(typeof(Ship), nameof(Ship.Load), typeof(OBNode), typeof(string), typeof(int), typeof(bool), typeof(bool), typeof(bool),
        typeof(bool), typeof(bool), typeof(ulong?), typeof(ObjectID))]
    static class LoadDesignPatch
    {
        static void Postfix(OBNode node, Ship __result) => Errors.Guard("ship tactics design load", () =>
        {
            if (!ReadOn || !node.ContainsChild(SavePath)) return;
            string text = App.ObjectBitsSerializer.ReadFromPath<string>(node, SavePath);
            string cargo = node.ContainsChild(DesignCargoPath) ? App.ObjectBitsSerializer.ReadFromPath<string>(node, DesignCargoPath) : "";
            if (!Valid(text, cargo, "in a ship design")) return;
            var t = new ShipTactics { Auto = node.ContainsChild(DesignAutoPath) && App.ObjectBitsSerializer.ReadFromPath<bool>(node, DesignAutoPath) };
            t.SetRows(text);
            t.SetCargo(cargo);
            Attach(__result, t);
        });
    }

    /// <summary>
    /// Build mode "Load ship (replace)" copies the blueprints of a design into a live ship (BuildToolbox.OnLoadShipReplaceClicked).
    /// When the design file has tactics, they go to the live ship through the tactics input, like an edit in the editor.
    /// A design without the tactics node keeps the ship's tactics.
    /// </summary>
    [HarmonyPatch(typeof(ShipUpdateInfo), nameof(ShipUpdateInfo.CopyBlueprintsFromShip))]
    static class LoadReplacePatch
    {
        static void Postfix(ShipUpdateInfo __instance, Ship fromShip) => Errors.Guard("ship tactics load replace", () =>
        {
            if (Of(fromShip) is not { } t || Run.Current is not { } run || __instance.Ship.Game != run.Game) return;
            var live = Of(__instance.Ship);
            run.Game.NetManager.EnqueueInput(new TacticsInput(__instance.Ship, t.Text, t.Auto, t.CargoText, live?.Recall ?? false));
            Log.Info($"ship tactics: design tactics go to {__instance.Ship.Metadata.PlainShipName}: auto={t.Auto} {t.Text}");
        });
    }

    /// <summary>Spec 404: HoldFire and PointDefenseOnly take away the fire permission. Read only; the AI tick builds Held.</summary>
    [HarmonyPatch(typeof(Weapon), nameof(Weapon.HasPermissionToFire))]
    static class FirePermissionPatch
    {
        static void Postfix(Weapon __instance, ref bool __result)
        {
            if (!__result || __instance.Ship is not { } ship || Of(ship) is not { Matched: not 0 } t) return;
            t.Held ??= ShipAiController.BuildHeld(ship, t);
            if (t.Held.Contains(__instance)) __result = false;
        }
    }
}
