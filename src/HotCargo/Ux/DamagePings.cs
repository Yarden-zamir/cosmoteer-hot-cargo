using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation;
using Halfling;

namespace HotCargo.Ux;

/// <summary>
/// V8. Vanilla gives no sign when a ship that you cannot see takes damage.
/// This pings the off-screen blip of your ship and marks it "under fire" (ShipIntegrity.BlipText) when it takes damage:
/// a built part loses health, or a part is destroyed. Building, moving and removing parts do not count (the integrity ratio
/// also drops while a new part fills up, which read as a hit while building).
/// It polls integrity on the GUI frame instead of hooking the damage callbacks, which can run on sim worker threads.
/// Limit: damage that shields absorb does not count, because it does not lower part health.
/// </summary>
static class DamagePings
{
    /// <summary>Seconds between two pings of the same ship, so that a long fight does not ping every poll.</summary>
    const double PingInterval = 3.0;

    sealed class State { public Dictionary<Part, int> Health = []; public int Destroyed; public double HitUntil = double.MinValue; public double NextPing = double.MinValue; }

    static readonly Dictionary<Ship, State> Ships = [];
    static readonly HashSet<Ship> Seen = [];
    static SimRoot? _sim;
    static double _nextPoll;

    /// <summary>Pings since start. For autotests.</summary>
    internal static int Hits;

    internal static void OnFrame(GameRoot game)
    {
        if (game.Sim is not { } sim || UxKit.Switches is not { DamagePings: true } s) { Ships.Clear(); return; }
        double now = App.Clock.Time.Seconds;
        if (now < _nextPoll) return;
        _nextPoll = now + 0.25;
        // A jump or a load replaces the sim. Start again, so that the new sim does not read as a hit.
        if (sim != _sim) { Ships.Clear(); _sim = sim; }
        Seen.Clear();
        foreach (var ship in sim.Ships.LocalPlayerShips)
        {
            Seen.Add(ship);
            bool first = !Ships.TryGetValue(ship, out var st);
            if (first) Ships[ship] = st = new State();
            if (TookDamage(ship, st!) && !first)
            {
                st.HitUntil = now + s.DamagePingSeconds;
                if (now >= st.NextPing)
                {
                    st.NextPing = now + PingInterval;
                    Hits++;
                    game.Gui.GetIndicatorFor(ship.IndicatorSource, offScreen: true)?.ShowPing(sound: false, autoSlow: false);
                }
            }
        }
        foreach (var gone in Ships.Keys.Where(k => !Seen.Contains(k)).ToList()) Ships.Remove(gone);
    }

    /// <summary>
    /// True when a built part lost health or a part was destroyed since the last poll, and records the new state.
    /// Parts under construction fill up from low health, and removed or moved parts leave the list; neither counts.
    /// </summary>
    static bool TookDamage(Ship ship, State st)
    {
        bool hit = ship.DestroyedParts.Count > st.Destroyed;
        st.Destroyed = ship.DestroyedParts.Count;
        var next = new Dictionary<Part, int>(st.Health.Count);
        foreach (var part in ship.Parts)
        {
            if (part.IsUnderConstruction) continue;
            if (st.Health.TryGetValue(part, out int before) && part.Health < before) hit = true;
            next[part] = part.Health;
        }
        st.Health = next;
        return hit;
    }

    public static bool UnderFire(Ship ship) =>
        Ships.TryGetValue(ship, out var st) && App.Clock.Time.Seconds < st.HitUntil && UxKit.On(s => s.DamagePings);
}
