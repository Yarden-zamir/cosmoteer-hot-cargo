using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Halfling.Network;
using Halfling.Serialization;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.ShipAi;

/// <summary>
/// Spec 409, 1065. Sets the rows, the Auto switch, the cargo settings and the return switch of one ship. Every edit in the editor, a paste and a design load send one,
/// so the change runs on every co-op machine at the same tick. Only a player who can command the ship can change it.
/// </summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoShipTactics")]
internal sealed class TacticsInput : MPInput
{
    [Serialize] public Ship? Ship { get; private set; }
    [Serialize] public string Text { get; private set; } = "";
    [Serialize] public bool Auto { get; private set; }
    [Serialize(Optional = true)] public string Cargo { get; private set; } = "";
    [Serialize(Optional = true)] public bool Recall { get; private set; }

    public TacticsInput(Ship ship, string text, bool auto, string cargo, bool recall)
    {
        Ship = ship ?? throw new ArgumentNullException(nameof(ship));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Auto = auto;
        Cargo = cargo ?? throw new ArgumentNullException(nameof(cargo));
        Recall = recall;
    }

    /// <summary>New rows and Auto switch; the ship keeps its cargo settings and return switch.</summary>
    public static TacticsInput Rows(Ship ship, string text, bool auto) =>
        new(ship, text, auto, ShipTactics.Of(ship)?.CargoText ?? "", ShipTactics.Of(ship)?.Recall ?? false);

    public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false)
    {
        if (Run.Current is not { } run || run.Game != game || game.Sim is not { } sim) return;
        if (Ship is null || !Ship.ExistsInSim(sim, falseIfNoParts: false)) return;
        if (!force && !Ship.Commands.IsCommandableBy(playerIndex)) return;
        // Bad text fails the same way on every machine: the error shows and the ship keeps its tactics.
        Errors.Guard("ship tactics input", () => ShipAiController.Set(Ship, Text, Auto, Cargo, Recall, sim));
    }
}

[HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.AllowMPInput))]
static class AllowTacticsInputPatch
{
    static void Postfix(MPInput mpInput, ref bool __result)
    {
        if (mpInput is TacticsInput) __result = true;
    }
}
