using Cosmoteer;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Gui;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Localization;
using Cosmoteer.Modes.Career;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Simulation;
using Halfling;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Gui;
using Halfling.Gui.Components.Text;
using Halfling.Network;
using Halfling.Serialization;
using HarmonyLib;
using HotCargo.Core;

namespace HotCargo.Events;

/// <summary>A running patrol listen (spec 1122). Saved in EventsState.</summary>
[ReflectiveSerialization(Explicit = true)]
public sealed class PatrolListenState
{
    /// <summary>The listening ship and the cell of its sensor array.</summary>
    [Serialize] public ObjectID Ship;
    [Serialize] public IntVector2 Cell;
    /// <summary>The faction ship that the sensor array listens to.</summary>
    [Serialize] public ObjectID Target;
    [Serialize] public double StartSeconds;
}

/// <summary>
/// Spec 1122, 1123: a working sensor array listens to a faction ship for Events.ListenSeconds. The sim tick runs it, so co-op stays in step.
/// Success lowers heat; failure (they notice) raises it. The chance falls with heat.
/// </summary>
public static class PatrolListen
{
    public static readonly ID<PartRules> SensorArrayId = new("cosmoteer.sensor_array");

    /// <summary>Test only (ev_listen_force): the next result is a success (true) or a failure (false). Set on every machine in co-op.</summary>
    internal static bool? Forced;

    static string Text(string key, params object?[] args) => Strings.FormatText("HotCargoListen/" + key, args);

    /// <summary>
    /// A built vanilla sensor array that is not destroyed. Limit: it listens without power and crew, because the design asks only for
    /// the part. Revisit when listening should cost crew (check its Sensor component's IsOperational then).
    /// </summary>
    public static bool Works(Part part) => part.Rules.ID == SensorArrayId && !part.IsDestroyed;

    static float Distance(Ship a, Ship b) => a.DetWorldBoundingCircle.DistanceTo(b.DetWorldBoundingCircle);

    static bool IsTarget(Ship candidate, Ship listener) =>
        !candidate.IsJunk && EventHail.TargetOf(candidate, listener) == HailTarget.FactionShip;

    /// <summary>The nearest faction ship that this ship can listen to now: in range and not heard yet. Sim state only.</summary>
    public static Ship? TargetFor(Run run, SimRoot sim, Ship listener)
    {
        var heard = run.State.Events.Listened;
        float range = run.T.Events.ListenRange;
        return sim.Ships.Where(s => !heard.Contains(s.UniqueID) && IsTarget(s, listener) && Distance(listener, s) <= range)
            .OrderBy(s => Distance(listener, s)).ThenBy(s => s.UniqueID).FirstOrDefault();
    }

    /// <summary>The sensor array of a running listen, or null when it is gone.</summary>
    public static Part? SensorOf(SimRoot sim, PatrolListenState l) =>
        sim.Ships[l.Ship] is { } ship && ship.Parts[l.Cell] is { } p && p.Location == l.Cell && p.Rules.ID == SensorArrayId ? p : null;

    /// <summary>Sim logic, from the MP input. Starts a listen from the sensor array, or does nothing when it cannot start.</summary>
    internal static void Start(Run run, SimRoot sim, Part sensor)
    {
        var s = run.State.Events;
        if (s.Listen is not null || !Works(sensor) || sensor.Ship is not { } ship) return;
        if (TargetFor(run, sim, ship) is not { } target) return;
        s.Listen = new PatrolListenState { Ship = ship.UniqueID, Cell = sensor.Location, Target = target.UniqueID, StartSeconds = run.State.GameSeconds };
        Log.Info($"patrol listen: {ship.Metadata.PlainShipName} listens to {target.Metadata.PlainShipName}");
        // UI only.
        Ux.UxKit.ChatLine(run.Game, Text("StartFmt", ship.Metadata.XmlShipName, target.Metadata.XmlShipName));
    }

    /// <summary>Sim tick (Run.Hooks): breaks the listen, or ends it with a roll from the sim random generator.</summary>
    internal static void OnTick(Run run, SimRoot sim, double dt) => Errors.Guard("patrol listen tick", () =>
    {
        if (sim != run.Game.Sim || run.State.Events.Listen is not { } l) return;
        var s = run.State.Events;
        var t = run.T.Events;
        if (BreakReason(run, sim, l) is { } reason)
        {
            s.Listen = null;
            Log.Info($"patrol listen broke: {reason}");
            Say(run.Game, reason);
            return;
        }
        if (run.State.GameSeconds - l.StartSeconds < t.ListenSeconds) return;
        s.Listen = null;
        s.Listened.Add(l.Target);
        var target = sim.Ships[l.Target]!;
        float chance = PatrolListenModel.SuccessChance(run.State.Heat, HeatModel.MaxHeat, t.ListenChanceAtNoHeat, t.ListenChanceAtMaxHeat);
        float roll = sim.Rand.Single();
        bool? forced = Forced;
        Forced = null;
        bool success = forced ?? roll < chance;
        float before = run.State.Heat;
        run.AddHeat((success ? t.ListenSuccessHeat : t.ListenFailHeat) * t.HeatScale, sim);
        string heat = (run.State.Heat - before).ToString("+0;-0;0");
        Log.Info($"patrol listen on {target.Metadata.PlainShipName}: roll {roll:0.000} vs {chance:0.000}, forced {forced?.ToString() ?? "no"} -> {(success ? "success" : "failure")}, heat {heat}");
        Say(run.Game, Text(success ? "SuccessFmt" : "FailFmt", target.Metadata.XmlShipName, heat));
    });

    /// <summary>Why a running listen stops early, or null while it can go on.</summary>
    static string? BreakReason(Run run, SimRoot sim, PatrolListenState l)
    {
        var t = run.T.Events;
        if (SensorOf(sim, l) is not { } sensor || !Works(sensor) || sensor.Ship is not { } ship) return Text("BrokeSensor");
        if (sim.Ships[l.Target] is not { } target || !IsTarget(target, ship)) return Text("BrokeChannel");
        if (Distance(ship, target) > t.ListenRange) return Text("BrokeRangeFmt", target.Metadata.XmlShipName);
        double now = sim.LogicalTime;
        bool Fired(Ship x) => PatrolListenModel.FiredRecently(now, x.Weapons.LastWeaponFireTime, t.ListenCombatSeconds);
        if (Fired(ship) || sim.Ships.Any(x => x.IsEnemiesWith(ship) && Fired(x) && Distance(ship, x) <= t.ListenCombatRadius)) return Text("BrokeCombat");
        return null;
    }

    /// <summary>UI only: every machine shows the same line in the center and in the chat.</summary>
    static void Say(GameRoot game, string xml)
    {
        Ux.UxKit.CenterText(game, xml);
        Ux.UxKit.ChatLine(game, xml);
    }

    // ---- Progress bar (UI only): above the sensor array, every frame, for every player.

    static SimRoot? _sim;

    /// <summary>Run.Hooks.Frame: hooks the overlay of the current sim again after a system jump or a load.</summary>
    internal static void OnFrame(Run run)
    {
        var sim = run.Game.Sim;
        if (sim == _sim) return;
        if (_sim is not null) _sim.Overlays.DrawingCustomOverlays -= OnDrawOverlays;
        _sim = sim;
        if (sim is not null) sim.Overlays.DrawingCustomOverlays += OnDrawOverlays;
    }

    static readonly Color BarColor = new(0.35f, 0.8f, 1f);

    /// <summary>The listen's progress from 0 to 1, or null while no listen runs. Also for the autotest.</summary>
    public static float? Progress(Run run) =>
        run.State.Events.Listen is { } l ? PatrolListenModel.Progress(run.State.GameSeconds, l.StartSeconds, run.T.Events.ListenSeconds) : null;

    static void OnDrawOverlays(object? sender, EventArgs e) => Ux.UxKit.Guard("patrol listen bar", () =>
    {
        if (Run.Current is not { } run || _sim is null || run.Game.Sim != _sim || run.State.Events.Listen is not { } l) return;
        if (SensorOf(_sim, l) is not { Ship: { } ship } part || Progress(run) is not { } progress) return;
        // The layout of the vanilla part status bars (SimOverlayRenderer.DrawStatusBarsFor), larger and placed above the part.
        var viewport = _sim.Viewport;
        Rect box = viewport.TransformScreenShapeFromWorld(ship.TransformShapeToWorld((RotRect)part.Rect)).BoundingRect;
        if (viewport.Region is { } region) box.Location -= (Vector2)region.Location;
        float scale = Mathx.Pow(1f / viewport.Camera.Node.WorldUniformScale, 0.25f) * GuiRoot.GlobalUIScale;
        float width = 72f * scale, height = 9f * scale, border = 1f * GuiRoot.GlobalUIScale;
        var rect = new Rect(box.X + box.Width / 2f - width / 2f, box.Top - height - 16f * scale, width, height);
        using (App.Graphics.PushTransform(Matrix.ProjectionOrthographic(App.Graphics)))
        {
            App.Graphics.SetToMaterial(null);
            App.Graphics.Draw<Quad>(new Quad(rect, Color.Black));
            Rect fill = rect.Inflate(-border, -border);
            fill.Width *= progress;
            App.Graphics.Draw<Quad>(new Quad(fill, BarColor));
        }
    });
}

/// <summary>A player starts a patrol listen from a sensor array (spec 1122). Runs in lockstep on every machine.</summary>
[ReflectiveSerialization(Explicit = true)]
[SerialDerivedType(TypeName = "HotCargoPatrolListen")]
internal sealed class PatrolListenInput : MPInput
{
    [Serialize] public Part? Sensor { get; private set; }

    public PatrolListenInput(Part sensor) => Sensor = sensor ?? throw new ArgumentNullException(nameof(sensor));

    PatrolListenInput() { }

    public override void Execute(GameRoot game, int playerIndex, MessengerID playerID, bool force = false) => Errors.Guard("patrol listen", () =>
    {
        if (Run.Current is not { } run || run.Game != game || game.Sim is not { } sim) return;
        if (Sensor?.Ship is not { } ship || !Sensor.ExistsInSim(sim) || !ship.Commands.IsCommandableBy(playerIndex)) return;
        PatrolListen.Start(run, sim, Sensor);
    });
}

[HarmonyPatch(typeof(CareerGameModeManager), nameof(CareerGameModeManager.AllowMPInput))]
static class AllowPatrolListenPatch
{
    static void Postfix(MPInput mpInput, ref bool __result)
    {
        if (mpInput is PatrolListenInput) __result = true;
    }
}

/// <summary>
/// Spec 1122: the "Listen to patrol chatter" button in the vanilla parts card while a sensor array is selected. It is greyed out,
/// with the reason in the tooltip, while it cannot start. GUI only: a click sends the MP input.
/// </summary>
sealed class PatrolListenButton : ImageButton
{
    readonly BaseCommandCard _card;
    string _tip = "";

    public PatrolListenButton(BaseCommandCard card, PartRules sensorArray) : base(sensorArray.EditorIcon, ShipGui.SHIP_COMMAND_BUTTON)
    {
        _card = card;
        SelfActive = false;
        Clicked += (_, _) => Errors.Guard("patrol listen click", OnClick);
        card.BeforeInputWhileActive((_, _) => Errors.Guard("patrol listen button", Refresh));
    }

    /// <summary>The first selected sensor array, by selection order, on a ship that the local player commands.</summary>
    internal static Part? Selected(GameRoot game) =>
        game.Sim.PlayerInput.SelectedParts.FirstOrDefault(p => p.Rules.ID == PatrolListen.SensorArrayId && p.Ship is { } s && s.Commands.IsCommandableByLocalPlayer);

    /// <summary>The reason that the selected sensor array cannot start a listen, or null when it can.</summary>
    internal static string? Unmet(Run run, Part sensor)
    {
        string Text(string key) => Strings.GetText("HotCargoListen/" + key);
        if (run.State.Events.Listen is not null) return Text("NeedIdle");
        if (!PatrolListen.Works(sensor)) return Text("NeedWorking");
        if (PatrolListen.TargetFor(run, run.Game.Sim, sensor.Ship!) is null) return Text("NeedTarget");
        return null;
    }

    void Refresh()
    {
        var part = Selected(_card.Game);
        SelfActive = part is not null;
        if (part is null || Run.Current is not { } run) return;
        string? unmet = Unmet(run, part);
        SelfInputActive = unmet is null;
        string tip = $"{Strings.GetText("HotCargoListen/Name")}\n{Strings.GetText("HotCargoListen/Tip")}" + (unmet is null ? "" : $"\n{unmet}");
        if (tip == _tip) return;
        _tip = tip;
        ToolTips.TextProvider = new SimpleTextProvider(tip, null);
    }

    void OnClick()
    {
        if (Selected(_card.Game) is not { } part || Run.Current is not { } run || Unmet(run, part) is not null) return;
        _card.Game.NetManager.EnqueueInput(new PatrolListenInput(part));
    }
}
