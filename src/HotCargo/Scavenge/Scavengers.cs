using Cosmoteer;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Commands;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Geometry;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Hunters;

namespace HotCargo.Scavenge;

/// <summary>
/// The Scavengers faction (spec 1340 to 1349): weak mining ships with their own player index. They are friendly to the players and the
/// vanilla factions until a player hits one, and enemies of pirates and hunters by the vanilla rule.
/// </summary>
internal static class Scavengers
{
    /// <summary>
    /// Spec 1340: the faction's player index. Vanilla factions use 100 to 501 and allocated sides (pirates, hunters) 100,000 and up;
    /// CheckIndex makes sure no faction takes it. Its team is the index (a new PlayerState), so the vanilla rule makes it an enemy of all
    /// other teams, and the relations patch below turns that off for players, factions and neutral ships.
    /// </summary>
    internal const int PlayerIndex = 900;

    internal static bool IsScavenger(Ship ship) => ship.Metadata.PlayerIndex == PlayerIndex;

    /// <summary>Spec 1340: throws when a vanilla or modded faction uses the scavenger index, so the clash shows at the first run.</summary>
    internal static void CheckIndex()
    {
        foreach (var f in GameApp.Rules.Factions)
            if (f.MilitaryPlayerIndex == PlayerIndex || f.CivilianPlayerIndex == PlayerIndex)
                throw new InvalidOperationException($"faction {f.ID} uses the scavenger player index {PlayerIndex}");
    }

    /// <summary>
    /// Spec 1343: the scavengers in the system: live ones that are not junk, and the ones that the game put into stasis far from the
    /// players (a vanilla SerializedStasisShip keeps the whole ship and its AI).
    /// </summary>
    internal static int InSystem(SimRoot sim) =>
        sim.Ships.AllShips.Count(s => IsScavenger(s) && !s.IsJunk) + sim.Stasis.Count(sp => sp.ObjectAllegiance == PlayerIndex);

    /// <summary>The names of the scavengers in the system, live or in stasis.</summary>
    internal static HashSet<string> Names(SimRoot sim) =>
        sim.Ships.AllShips.Where(IsScavenger).Select(s => s.Metadata.PlainShipName)
            .Concat(sim.Stasis.Where(sp => sp.ObjectAllegiance == PlayerIndex).Select(sp => sp.ObjectName).OfType<string>()).ToHashSet();

    static bool Provoked(Run run) => run.State.GameSeconds < run.State.Scavenge.ProvokedUntil;

    // ---- Arrivals (spec 1343)

    /// <summary>
    /// Spec 1343: every SpawnCheckSeconds, a system with fewer than MaxPerSystem live scavengers gets one with SpawnChance. It arrives
    /// SpawnDistanceMin to SpawnDistanceMax from the players' first ship, clear of suns and stations, facing the players. Sim tick only.
    /// </summary>
    internal static void CheckArrivals(Run run, SimRoot sim)
    {
        var t = run.T.Scavenge;
        if (!t.FactionEnabled || run.State.Result != RunResult.Running) return;
        if (InSystem(sim) >= t.MaxPerSystem) return;
        var first = sim.Ships.HumanPlayerShips.Where(s => !s.IsJunk).MinBy(s => s.UniqueID);
        if (first is null || sim.Rand.Single() >= t.SpawnChance) return;
        float angle = sim.Rand.Single(new Range<float>(0f, MathF.Tau));
        var at = HunterSpawner.SpawnLocation(sim, first.DetWorldCenter, angle, MathF.PI, new Range<float>(t.SpawnDistanceMin, t.SpawnDistanceMax));
        ScavengerShip.Spawn(sim, at, at.DirectionTo(first.DetWorldCenter));
    }

    // ---- Relations (spec 1340, 1341)

    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.AreEnemies))]
    static class AreEnemiesPatch
    {
        // No Errors.Guard: this runs for every enemy check, and the body has no call that can throw.
        static void Postfix(CareerSimModeManager __instance, int player1, int player2, ref bool __result)
        {
            if (player1 == player2) return;
            int other = player1 == PlayerIndex ? player2 : player2 == PlayerIndex ? player1 : int.MinValue;
            if (other == int.MinValue) return;
            var side = __instance.GameMode.IsHumanPlayerIndex(other) ? Side.Human
                : PlayerIndexes.IsFactionMilitary(other) || PlayerIndexes.IsFactionCivilian(other) ? Side.Faction
                : other is PlayerIndexes.Neutral or PlayerIndexes.Junk or PlayerIndexes.AllPlayers ? Side.Neutral
                : Side.Other;
            bool provoked = Run.Current is { } run && run.Game.Sim == __instance.Sim && Provoked(run);
            if (ScavengeModel.Enemies(side, provoked) is { } enemies) __result = enemies;
        }
    }

    /// <summary>
    /// Spec 1341: vanilla calls CheckIfWarTriggered when a player's shot hits a ship. A hit on a scavenger by a player (not a forgivable
    /// one, such as an explosion that spread) makes the scavengers hostile to the players for ProvokedSeconds; each hit starts the time again.
    /// </summary>
    [HarmonyPatch(typeof(CareerSimModeManager), "CheckIfWarTriggered")]
    static class ProvokePatch
    {
        static void Postfix(CareerSimModeManager __instance, Ship hitShip) => Errors.Guard("scavengers provoke", () =>
        {
            if (!IsScavenger(hitShip) || Run.Current is not { } run || run.Game.Sim != __instance.Sim) return;
            var source = __instance.Sim.CurrentActionSourcePlayer;
            if (source is null || source.IsForgivable || !__instance.GameMode.IsHumanPlayerIndex(source.PlayerId)) return;
            bool was = Provoked(run);
            run.State.Scavenge.ProvokedUntil = run.State.GameSeconds + run.T.Scavenge.ProvokedSeconds;
            if (was) return;
            Log.Info($"scavengers: provoked by player {source.PlayerId} (hit {hitShip.Metadata.PlainShipName}) until {run.State.Scavenge.ProvokedUntil:0}");
            Ux.UxKit.ChatLine(run.Game, Strings.FormatText("HotCargoScavenge/ProvokedFmt", hitShip.Metadata.XmlShipName));
        });
    }

    /// <summary>
    /// Spec 1341: when the provocation ends, the scavengers and the players drop the attack orders and turret targets they have on each
    /// other, like the vanilla truce (ClearAggressivePlayerActsToAndFromShip). Sim tick only.
    /// </summary>
    internal static void CheckPeace(Run run, SimRoot sim)
    {
        var s = run.State.Scavenge;
        if (s.ProvokedUntil <= 0 || Provoked(run)) return;
        s.ProvokedUntil = 0;
        foreach (int human in run.Game.HumanPlayersIndexes)
        {
            foreach (var ship in sim.Ships.GetShipsForPlayer(PlayerIndex))
            {
                ship.Weapons.ClearAllWeaponTargetsAtPlayerIndex(human);
                ship.Commands.RemoveAll(c => c is IAttackCommand { Target: { } t } && t.Metadata.PlayerIndex == human);
            }
            foreach (var ship in sim.Ships.GetShipsForPlayer(human))
            {
                ship.Weapons.ClearAllWeaponTargetsAtPlayerIndex(PlayerIndex);
                ship.Commands.RemoveAll(c => c is IAttackCommand { Target: { } t } && IsScavenger(t));
            }
        }
        Log.Info("scavengers: no longer hostile to the players");
        Ux.UxKit.ChatLine(run.Game, Strings.GetText("HotCargoScavenge/Peace"));
    }
}
