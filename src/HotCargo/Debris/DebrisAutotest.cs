using System.Globalization;
using Cosmoteer.Data;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Dev;

namespace HotCargo.Debris;

/// <summary>Test commands and fields of the Debris area. Commands that change the sim run through "mp" in co-op.</summary>
static class DebrisAutotest
{
    static Run R() => Autotest.RequireRun();

    internal static void Register()
    {
        Autotest.Commands["debris_set"] = arg => Autotest.SetField(R().T.Debris, arg);
        // debris_spawn <count> [parts]: loose junk pieces near the acting player's first ship, each a row of [parts] armor blocks (default 3),
        // with no crew and no resources. Fixed seeds and sim positions, so every co-op machine makes the same pieces.
        Autotest.Commands["debris_spawn"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int count = int.Parse(p[0], CultureInfo.InvariantCulture);
            int parts = p.Length > 1 ? int.Parse(p[1], CultureInfo.InvariantCulture) : 3;
            var sim = R().Game.Sim;
            var home = Autotest.ActingShip(sim) ?? throw new InvalidOperationException("no ship of the acting player");
            var armor = home.Rules.PartsByID[new ID<PartRules>("cosmoteer.armor")];
            for (int i = 0; i < count; i++)
            {
                var piece = new Ship(home.Rules, "Test debris", -3, randSeed: 7700UL + (ulong)i);
                using (var sui = ShipUpdateInfo.Alloc(piece))
                {
                    for (int x = 0; x < parts; x++)
                        if (!sui.TryAddBlueprintPart(armor, new IntVector2(x, 0), 0, flipX: false, mirrorPhysical: false,
                                allowOverwrite: false, addDoors: false, hireCrew: false, out var reasons, force: true))
                            throw new InvalidOperationException($"cannot add armor: {reasons}");
                    sui.Apply();
                }
                piece.MatchPhysicalToBlueprints();
                var near = home.DetWorldCenter + Vector2.FromAngle(i * 2.4f) * (home.DetWorldBoundingCircle.Radius + 300f + 40f * i);
                piece.WorldCenter = Autotest.ClearSpotNear(sim, near, 0f, 60f);
                sim.Ships.Add(piece);
            }
            Log.Info($"AUTOTEST debris_spawn: {count} pieces of {parts} parts");
        };
        Autotest.Fields["junk"] = () => R().Game.Sim.Ships.Count(s => s.IsJunk && !s.Rules.IsAsteroid);
        Autotest.Fields["debris_tracked"] = () => R().State.Debris.Since.Count;
        Autotest.Fields["debris_removed"] = () => R().State.Debris.Removed;
        Autotest.Fields["debris_glowing"] = () =>
        {
            var run = R();
            var t = run.T.Debris;
            return run.State.Debris.Since.Values.Count(s => DebrisModel.Phase(run.State.GameSeconds - s, t.DecaySeconds, t.GlowSeconds) == DebrisPhase.Glowing);
        };
        // Junk pieces that may decay now (spec 1201), for checks that a protection holds.
        Autotest.Fields["debris_candidates"] = () =>
        {
            var run = R();
            var sim = run.Game.Sim;
            var reach = DebrisArea.TractorReach(sim, run.T.Debris.TractorMargin);
            return sim.Ships.Count(s => DebrisArea.CanDecay(run, s, run.T.Debris, reach));
        };
    }
}
