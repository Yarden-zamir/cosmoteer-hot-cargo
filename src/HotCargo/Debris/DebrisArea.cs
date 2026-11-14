using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Weapons;
using Cosmoteer.Simulation;
using Halfling.Geometry;
using HotCargo.Core;
using HotCargo.Events;

namespace HotCargo.Debris;

/// <summary>
/// Entry point of the Debris area (spec 1200 to 1219). Old loose junk glows, then burns up, so a long fight does not leave the
/// system full of pieces that the game simulates. The check runs in the sim tick, reads sim state only and walks the ships in sim
/// order. Each piece's age is run state, so every co-op machine removes the same pieces at the same tick (spec 1205).
/// </summary>
public static class DebrisArea
{
    public static void Register()
    {
        Run.Hooks.Tick += (run, sim, dt) => Errors.Guard("debris tick", () => OnTick(run, sim, dt));
        Run.Hooks.Frame += run => Ux.UxKit.Guard("debris glow", () => DebrisGlow.Update(run));
        DebrisAutotest.Register();
    }

    static void OnTick(Run run, SimRoot sim, double dt)
    {
        var t = run.T.Debris;
        var state = run.State.Debris;
        if (!t.Enabled)
        {
            state.Since.Clear();
            return;
        }
        if (sim.Tick % DebrisModel.CheckTicks(t.CheckSeconds, dt) != 0) return;
        Check(run, sim, t, state);
    }

    /// <summary>Spec 1202 to 1205: tracks every piece that can decay, forgets the others, and removes the pieces whose time is over.</summary>
    static void Check(Run run, SimRoot sim, DebrisTunables t, DebrisState state)
    {
        double now = run.State.GameSeconds;
        var reach = TractorReach(sim, t.TractorMargin);
        var live = new HashSet<ObjectID>();
        var burn = new List<Ship>();
        foreach (Ship ship in sim.Ships)
        {
            if (!CanDecay(run, ship, t, reach)) continue;
            live.Add(ship.UniqueID);
            if (!state.Since.TryGetValue(ship.UniqueID, out double since))
            {
                state.Since[ship.UniqueID] = now;
                continue;
            }
            if (DebrisModel.Phase(now - since, t.DecaySeconds, t.GlowSeconds) == DebrisPhase.Burning) burn.Add(ship);
        }
        // A piece that left the live ships (stasis, a jump) or became protected starts again from zero. Limit: junk in stasis never
        // decays, but the game does not simulate it either. Revisit if saves grow from junk far from the players.
        foreach (var id in state.Since.Keys.Where(id => !live.Contains(id)).ToList()) state.Since.Remove(id);
        foreach (var ship in burn)
        {
            state.Since.Remove(ship.UniqueID);
            // No lost-ship handling: it reads the local player and writes a local file, so it is not the same on every machine.
            sim.Ships.Remove(ship, dispose: true, handleLostShip: false);
            state.Removed++;
        }
        if (burn.Count > 0) Log.Info($"debris: {burn.Count} junk pieces burned up, {state.Since.Count} tracked");
    }

    /// <summary>
    /// Spec 1201: a junk piece can decay when it has no value and nothing holds it. Never: a ship that is not junk, an asteroid,
    /// a wreck with more than MaxParts parts, a piece with resources (tech, mission items, cargo) or crew aboard, a station or
    /// landmark, the relic, a vanilla abandoned ship, a piece that a mission names, a piece that crew salvage, a piece in reach of a
    /// player's tractor beam, or a piece that the game locks against removal. Tech crates are not ships, so they never decay.
    /// </summary>
    internal static bool CanDecay(Run run, Ship ship, DebrisTunables t, List<Circle> tractorReach)
    {
        if (!ship.IsJunk || ship.Rules.IsAsteroid || ship.Parts.Count == 0 || ship.Parts.Count > t.MaxParts) return false;
        if (ship.Crew.OnShip.Count > 0 || HasResources(ship) || ship.SpecialComponents.Count > 0) return false;
        if (ship.HasTag(Abandoned.RelicTag) || ship.HasLabel(Abandoned.Label)) return false;
        if (!ship.Sim.Mode.AllowCullShip(ship) || BeingSalvaged(ship)) return false;
        var circle = ship.DetWorldBoundingCircle;
        foreach (var r in tractorReach) if (r.IntersectsWith(circle)) return false;
        foreach (var mission in run.Mode.Missions) if (mission.IsShipAssociated(ship)) return false;
        return true;
    }

    /// <summary>Any resource stored, carried by crew or in transit on the piece.</summary>
    static bool HasResources(Ship ship)
    {
        var r = ship.Resources;
        foreach (var n in r._storedResourceTotals.Values) if (n > 0) return true;
        foreach (var n in r._heldResourceTotals.Values) if (n > 0) return true;
        foreach (var n in r._inTransitResourceTotals.Values) if (n > 0) return true;
        return false;
    }

    /// <summary>Crew salvage a part of the piece, or a salvage started on it (the part lost health to salvage).</summary>
    static bool BeingSalvaged(Ship ship)
    {
        foreach (var part in ship.Parts)
        {
            if (part.SalvageDamage > 0) return true;
            if (SalvageJob.s_jobsByPart.TryGetValue(part, out var jobs) && jobs.Count > 0) return true;
        }
        return false;
    }

    /// <summary>
    /// The area around each player ship whose tractor beam pulls something now: its hull circle grown by the longest stretch range of
    /// its pulling beams (vanilla tractor beam: 600) and the margin. A beam reports a stretch range only while it pulls (BeamEmitter
    /// .GetStretchRange), so modded beams count too. Limit: a piece is safe only while a beam pulls, not before; the glow warns first.
    /// </summary>
    internal static List<Circle> TractorReach(SimRoot sim, float margin)
    {
        var result = new List<Circle>();
        foreach (Ship ship in sim.Ships.HumanPlayerShips)
        {
            float reach = 0f;
            foreach (Part part in ship.Parts)
                foreach (var component in part.Components)
                    if (component is IEmitter emitter && emitter.GetStretchRange() is { } stretch && stretch > reach) reach = stretch;
            if (reach > 0f) result.Add(ship.DetWorldBoundingCircle.Inflate(reach + margin));
        }
        return result;
    }
}
