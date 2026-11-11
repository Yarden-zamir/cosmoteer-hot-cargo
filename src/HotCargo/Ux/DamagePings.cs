using Cosmoteer.Game;
using Cosmoteer.Ships;
using Cosmoteer.Simulation;
using Halfling;

namespace HotCargo.Ux;

/// <summary>
/// V8. Vanilla gives no sign when a ship that you cannot see takes damage.
/// This pings the off-screen blip of your ship and marks it "under fire" (ShipIntegrity.BlipText) when its hull integrity drops.
/// It polls integrity on the GUI frame instead of hooking the damage callbacks, which can run on sim worker threads.
/// Limit: damage that shields absorb does not count, because it does not lower part health.
/// </summary>
static class DamagePings
{
    /// <summary>The smallest integrity drop that counts as a hit, so that rounding noise does not ping.</summary>
    const float MinDrop = 0.0005f;
    /// <summary>Seconds between two pings of the same ship, so that a long fight does not ping every poll.</summary>
    const double PingInterval = 3.0;

    sealed class State { public float Integrity; public double HitUntil = double.MinValue; public double NextPing = double.MinValue; }

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
            float f = ShipIntegrity.Of(ship);
            if (!Ships.TryGetValue(ship, out var st)) { Ships[ship] = new State { Integrity = f }; continue; }
            if (f < st.Integrity - MinDrop)
            {
                st.HitUntil = now + s.DamagePingSeconds;
                if (now >= st.NextPing)
                {
                    st.NextPing = now + PingInterval;
                    Hits++;
                    game.Gui.GetIndicatorFor(ship.IndicatorSource, offScreen: true)?.ShowPing(sound: false, autoSlow: false);
                }
            }
            st.Integrity = f;
        }
        foreach (var gone in Ships.Keys.Where(k => !Seen.Contains(k)).ToList()) Ships.Remove(gone);
    }

    public static bool UnderFire(Ship ship) =>
        Ships.TryGetValue(ship, out var st) && App.Clock.Time.Seconds < st.HitUntil && UxKit.On(s => s.DamagePings);
}
