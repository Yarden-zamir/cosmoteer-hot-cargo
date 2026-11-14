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
        // debris_spawn <count> [parts] [speed]: loose junk pieces near the acting player's first ship, each a row of [parts] armor blocks
        // (default 3), with no crew and no resources. [speed] (default 0) sends them drifting and spinning, like pieces of a destroyed ship.
        // Fixed seeds and sim positions, so every co-op machine makes the same pieces.
        Autotest.Commands["debris_spawn"] = arg =>
        {
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int count = int.Parse(p[0], CultureInfo.InvariantCulture);
            int parts = p.Length > 1 ? int.Parse(p[1], CultureInfo.InvariantCulture) : 3;
            float speed = p.Length > 2 ? float.Parse(p[2], CultureInfo.InvariantCulture) : 0f;
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
                piece.Physics.PhysicsWrapper.LinearVelocity = Vector2.FromAngle(i * 1.7f) * speed;
                piece.Physics.PhysicsWrapper.AngularVelocity = speed > 0 ? 0.3f + 0.1f * (i % 5) : 0f;
            }
            Log.Info($"AUTOTEST debris_spawn: {count} pieces of {parts} parts, speed {speed}");
        };
        // debris_focus: test only. The camera follows the acting player's ship and zooms out to the 3 nearest tracked junk pieces,
        // like "tech focus" (for screenshots).
        Autotest.Commands["debris_focus"] = _ =>
        {
            var run = R();
            var sim = run.Game.Sim;
            var home = Autotest.ActingShip(sim) ?? throw new InvalidOperationException("no ship of the acting player");
            var near = run.State.Debris.Since.Keys.Select(id => sim.Ships[id]).OfType<Ship>()
                .Select(s => s.DetWorldCenter.DistanceTo(home.DetWorldCenter)).Order().Take(3).ToList();
            if (near.Count == 0) throw new InvalidOperationException("no tracked junk piece");
            home.FocusCamera(playSound: false);
            var view = sim.Camera.ViewController;
            float size = (near.Max() + 120f) * 2f;
            // A minimized test slot window has no viewport size, so ZoomToFit zooms out all the way. Then the zoom fits the
            // screenshot height (1440 pixels in the test slots). Revisit if screens.sh uses another resolution.
            if (view._scene.Viewport.GetViewportRect().Size.Y > 1) view.ZoomToFit(new Vector2(size));
            else view.FRTargetZoomScale = view.FRZoomScale = Math.Clamp(view.TransformWorldScaleToFrame(size * view.Node.WorldUniformScale / 1440f),
                view.FRMinZoomScale, view.FRMaxZoomScale);
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
