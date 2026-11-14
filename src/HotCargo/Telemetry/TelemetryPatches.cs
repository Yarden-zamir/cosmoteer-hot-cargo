using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Cosmoteer.Data;
using Cosmoteer.Game;
using Cosmoteer.Game.Multiplayer;
using Cosmoteer.Modes.Career;
using Cosmoteer.Modes.Career.Missions;
using Cosmoteer.Ships;
using Cosmoteer.Ships.AI;
using Cosmoteer.Ships.Crew.Jobs;
using Cosmoteer.Ships.Parts;
using Cosmoteer.Ships.Parts.Resources;
using Cosmoteer.Simulation;
using Halfling.Serialization.Generic;
using HarmonyLib;
using HotCargo.Core;
using HotCargo.Hunters;
using HotCargo.Journey;
using HotCargo.Tech;

namespace HotCargo.Telemetry;

/// <summary>
/// Read-only Harmony patches and game event handlers that feed the telemetry session. None of them changes game state or a result:
/// prefixes return nothing, postfixes take no ref arguments. A failure reports once (Errors.Report) and the game goes on.
/// </summary>
static class TelemetryPatches
{
    // ---- Callers: who changed money, fame, heat or tech. A stack walk finds the first mod or game frame outside the plumbing.

    static long _lookupWindow;
    static int _lookups;

    /// <summary>Frame names that are plumbing between the change and its cause.</summary>
    static readonly string[] SkipFrames =
    [
        "HotCargo.Telemetry", "PlayerState.AddMoney", "EventHub.", "Run.AddHeat", "set_PlayerFame", "Delegate", "Invoke",
        "Errors.Guard", "Run+Hooks", "Hooks.Each", "Hooks.Raise", "EnqueueDeterministic", "System.",
    ];

    /// <summary>Separates the frames in a caller text.</summary>
    internal const string FrameSeparator = " < ";

    /// <summary>
    /// Up to 3 frames that caused the current change, innermost first, joined with " &lt; ".
    /// "rate_limited" above Telemetry.CallerLookupsPerSecond lookups in one real second.
    /// </summary>
    internal static string Caller()
    {
        long now = Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        if (Interlocked.Exchange(ref _lookupWindow, now) != now) Interlocked.Exchange(ref _lookups, 0);
        int limit = Run.Current?.T.Telemetry.CallerLookupsPerSecond ?? 0;
        if (Interlocked.Increment(ref _lookups) > limit) return "rate_limited";
        var frames = new StackTrace(2, false).GetFrames();
        var found = new List<string>(3);
        foreach (var f in frames)
        {
            var m = f.GetMethod();
            if (m is null) continue;
            // A method that Harmony patched runs as a replacement named "<original>_Patch<n>".
            string method = m.Name;
            int patch = method.IndexOf("_Patch", StringComparison.Ordinal);
            if (patch > 0) method = method[..patch];
            string full = m.DeclaringType?.FullName ?? "";
            if (SkipFrames.Any($"{full}.{method}".Contains)) continue;
            // Short form: Type.Method, without the namespace. Nested types keep their outer type ("Outer+Inner").
            int dot = full.LastIndexOf('.');
            string type = dot >= 0 ? full[(dot + 1)..] : full;
            found.Add(type.Length > 0 ? $"{type}.{method}" : method);
            if (found.Count == 3) break;
        }
        return found.Count == 0 ? "unknown" : string.Join(FrameSeparator, found);
    }

    /// <summary>Caller frame text to source name. The first rule whose key the caller text contains wins. Order: specific before general.</summary>
    static readonly (string Key, string Source)[] Sources =
    [
        ("Autotest", "test"), ("DebugKeys", "debug"), ("GameDebugTools", "debug"),
        ("Run.OnFameChanged", "fame"), ("Run.ForceNextStage", "force_stage"),
        ("ContractBook.SellOut", "informant"), ("ContractBook.OnAccepted", "contract_accept"), ("ContractBook.OnTick", "trail"),
        ("ContractBonusReward", "contract_bonus"), ("HeatReward", "escort_cover"),
        ("EventRunner.PayCost", "event_cost"), ("EventRunner", "event"), ("ChainRunner", "chain"),
        ("StartingVault", "starting_vault"), ("Buyer", "buyer"),
        ("MoneyReward", "mission_reward"), ("FameReward", "mission_reward"), ("ScaledReward", "mission_reward"),
        ("HoldOutObjective", "mission_reward"), ("OnShipJunked", "ship_kill"), ("CheckIfTruceBroken", "truce_break"),
        ("ResourceTransferJob", "trade"), ("ConstructionManager", "build_refund"), ("ResourceManager", "build_refund"),
        ("MPUpdateShipInput", "build"), ("CommHireTab", "crew_hire"), ("CommTechsTab", "tech_unlock"),
        ("MPShipSurrenderedInput", "surrender"), ("MPTransferMoneyInput", "transfer"),
        ("FleeStrategy", "flee_jump"), ("AIExitSectorModule", "exit_sector"), ("AIDeleteSelfModule", "delete_self"),
        ("AITradeShipModule", "trade_route"),
        ("FameCapHeat", "fame_cap"), ("Deals", "diplomacy"), ("Scout.Report", "scout_report"), ("Hacker.Apply", "hacker"), ("TracerBeacon", "tracer"), ("StartJobs.Pick", "start_job"), ("PatrolListen", "patrol_listen"),
    ];

    /// <summary>
    /// The source of a change: the context label when one is set, else the rule of the innermost caller frame that matches one.
    /// Without a match it is the type of the innermost frame.
    /// </summary>
    internal static string Source(string? context, string caller)
    {
        if (context is not null) return context;
        var frames = caller.Split(FrameSeparator);
        foreach (string frame in frames)
            foreach (var (key, src) in Sources)
                if (frame.Contains(key)) return src;
        int dot = frames[0].IndexOf('.');
        return dot > 0 ? frames[0][..dot] : frames[0];
    }

    /// <summary>True for a source that a rule names (or a tick context, or a rate-limited lookup), false for a caller type fallback.</summary>
    internal static bool IsKnownSource(string source) =>
        source is "time" or "trail" or "rate_limited" || Sources.Any(s => s.Source == source);

    // ---- Heat

    /// <summary>
    /// Run.OnTick and ContractBook.OnTick add heat every tick, too often for a stack walk. These two context patches mark them.
    /// AddHeat then checks the amount against the formula of that caller, so another call from the same tick gets a stack walk.
    /// </summary>
    [HarmonyPatch]
    static class TickContexts
    {
        static IEnumerable<MethodBase> TargetMethods() =>
        [
            AccessTools.Method(typeof(Run), "OnTick") ?? throw new MissingMethodException("Run", "OnTick"),
            AccessTools.Method(typeof(Contracts.ContractBook), "OnTick") ?? throw new MissingMethodException("ContractBook", "OnTick"),
        ];

        static void Prefix(MethodBase __originalMethod) => Tele.Push(__originalMethod.DeclaringType == typeof(Run) ? "Run.OnTick" : "ContractBook.OnTick");

        static Exception? Finalizer(Exception? __exception)
        {
            Tele.Pop();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Run), nameof(Run.AddHeat))]
    static class AddHeatPatch
    {
        static void Prefix(Run __instance, out float __state) => __state = __instance.State.Heat;

        static void Postfix(Run __instance, float amount, SimRoot sim, float __state)
        {
            if (Session.Current is not { } s || s.Run != __instance || !Tele.On(Cat.Heat)) return;
            long t0 = Stats.Enter();
            try
            {
                string? context = Tele.Top switch
                {
                    "Run.OnTick" when amount == __instance.TimeHeatPerMinute(__instance.State.FleetTech) * (float)(sim.FixedUpdater.Interval.Seconds / 60.0) => "time",
                    "ContractBook.OnTick" when amount == Contracts.ContractBook.TrailPerMinute(__instance, Contracts.ContractBook.OpenWeight(__instance))
                        * (float)(sim.FixedUpdater.Interval.Seconds / 60.0) => "trail",
                    _ => null,
                };
                string? caller = context is null ? Caller() : null;
                string source = Source(context, caller ?? "");
                s.HeatChanged(source, caller, amount, __state, __instance.State.Heat);
                Dev.Invariants.HeatSource(source, caller, __instance.State.Heat - __state);
            }
            catch (Exception ex) { Errors.Report("telemetry heat", ex.Message, ex); }
            finally { Stats.Exit(t0); }
        }
    }

    // ---- Money: EventHub.PlayerMoneyChanged fires inside PlayerState.AddMoney, so the stack still holds the cause.

    internal static void OnMoneyChanged(object? sender, PlayerState p)
    {
        if (Session.Current is not { } s || p.Game != s.Game || !Tele.On(Cat.Economy)) return;
        long t0 = Stats.Enter();
        try
        {
            int before = s.KnownMoney(p.Index);
            if (before == p.Money) return;
            string caller = Caller();
            s.MoneyChanged(p.Index, p.Money - before, before, p.Money, Source(null, caller), caller);
        }
        catch (Exception ex) { Errors.Report("telemetry money", ex.Message, ex); }
        finally { Stats.Exit(t0); }
    }

    // ---- Tech

    /// <summary>Vault grid adds and removes (events, debug keys, the starting vault, tests). Gives the next tech.count event its cause.</summary>
    [HarmonyPatch]
    static class VaultChangePatch
    {
        static IEnumerable<MethodBase> TargetMethods() =>
        [
            AccessTools.Method(typeof(Vaults), nameof(Vaults.Add), [typeof(IEnumerable<TypedResourceGrid>), typeof(int)]) ?? throw new MissingMethodException("Vaults", "Add"),
            AccessTools.Method(typeof(Vaults), nameof(Vaults.Remove), [typeof(SimRoot), typeof(int)]) ?? throw new MissingMethodException("Vaults", "Remove"),
        ];

        static void Postfix(int __result)
        {
            if (__result == 0 || Session.Current is not { } s || !Tele.On(Cat.Tech)) return;
            long t0 = Stats.Enter();
            try { s.AddTechHint(Source(null, Caller())); }
            finally { Stats.Exit(t0); }
        }
    }

    /// <summary>
    /// Hits on a Tech Vault that cost tech (only the hit that destroys it, spec 65): damage, health and the tech it cost.
    /// Hits that cost nothing only add to the vault damage sums (combat.damage, side "vault"), because fire and repair loops make many of them.
    /// </summary>
    [HarmonyPatch(typeof(Part), "OnHealthChanged")]
    static class VaultHitPatch
    {
        static TypedResourceGrid? Grid(Part p) => p.Components.OfType<TypedResourceGrid>().FirstOrDefault(g => g.ResourceType == Vaults.TechId);

        [HarmonyPriority(Priority.First)]
        static void Prefix(Part __instance, out int __state)
        {
            __state = -1;
            if (__instance.Rules.ID != Vaults.VaultId || Session.Current is null) return;
            __state = Grid(__instance)?.Resources ?? -1;
        }

        [HarmonyPriority(Priority.Last)]
        static void Postfix(Part __instance, int oldHealth, int newHealth, int __state)
        {
            if (__state < 0 || newHealth == oldHealth || Session.Current is not { } s || !Tele.On(Cat.Tech)) return;
            if (newHealth > oldHealth) return;
            int after = newHealth == 0 ? 0 : Grid(__instance)?.Resources ?? 0;
            AddDamage("vault", oldHealth - newHealth, newHealth == 0);
            if (after == __state && newHealth > 0) return;
            string cause = newHealth == 0 ? "vault_destroyed" : "vault_damage";
            s.AddTechHint(cause);
            Tele.Emit(Cat.Tech, "tech.vault_hit", w =>
            {
                w.WriteString("ship", __instance.Ship?.Metadata.PlainShipName);
                w.WriteNumber("player", __instance.Ship?.Metadata.PlayerIndex ?? -1);
                w.WriteNumber("damage", oldHealth - newHealth);
                w.WriteNumber("health", newHealth);
                w.WriteNumber("max_health", __instance.Rules.MaxHealth);
                w.WriteNumber("tech_before", __state);
                w.WriteNumber("tech_after", after);
                w.WriteBoolean("destroyed", newHealth == 0);
            });
        }
    }

    // ---- Trades

    internal static void OnTradeDelivery(object? sender, ResourceTradeEventArgs e) => Trade("econ.trade", e);

    internal static void OnTradePickup(object? sender, ResourceTradeEventArgs e)
    {
        if (e.ResourceType == Vaults.TechId) Trade("econ.trade_pickup", e);
    }

    static void Trade(string name, ResourceTradeEventArgs e)
    {
        if (Session.Current is not { } s || !Tele.On(Cat.Economy)) return;
        long t0 = Stats.Enter();
        try
        {
            bool tech = e.ResourceType == Vaults.TechId;
            bool buyer = e.SinkShip is { } sink && Buyer.IsBuyer(sink);
            if (tech && name == "econ.trade")
                s.AddTechHint(buyer ? "buyer" : e.TradeType switch { TradeType.Sell => "sale", TradeType.Buy => "buyback", _ => "trade" });
            // The job's price is the price of the order. Without the job (it can end with the delivery) the mode's current price stands in.
            var mode = s.Game.Sim.Mode;
            (int? price, string priceSrc) = (e.Crew.Job, e.TradeType) switch
            {
                (ResourceTransferJob job, _) => (job.PricePerResource, "order"),
                (_, TradeType.Sell) => (mode.GetResourceSellRefund(e.ResourceType, e.SinkShip?.Metadata.PlayerIndex), "current"),
                (_, TradeType.Buy) => (mode.GetResourceBuyCost(e.ResourceType, e.SourceShip?.Metadata.PlayerIndex), "current"),
                _ => ((int?)null, ""),
            };
            Tele.Emit(Cat.Economy, name, w =>
            {
                w.WriteString("trade", e.TradeType.ToString());
                w.WriteString("resource", e.ResourceType.ToString());
                w.WriteNumber("amount", e.Amount);
                if (price is { } pp) { w.WriteNumber("unit_price", pp); w.WriteNumber("total", (long)pp * e.Amount); w.WriteString("price_src", priceSrc); }
                w.WriteBoolean("is_tech", tech);
                w.WriteBoolean("buyer", buyer);
                w.WriteNumber("crew_player", e.Crew.PlayerIndex);
                if (e.SourceShip is { } src) { w.WriteString("from", src.Metadata.PlainShipName); w.WriteString("from_side", Session.Side(src)); }
                if (e.SinkShip is { } dst) { w.WriteString("to", dst.Metadata.PlainShipName); w.WriteString("to_side", Session.Side(dst)); }
                if (tech) w.WriteNumber("tech_value", TechPrice.Sell(s.Run));
            });
        }
        catch (Exception ex) { Errors.Report("telemetry trade", ex.Message, ex); }
        finally { Stats.Exit(t0); }
    }

    // ---- Combat

    [HarmonyPatch(typeof(CareerSimModeManager), "OnShipJunked")]
    static class JunkedPatch
    {
        static void Postfix(Ship ship, int oldAllegiance)
        {
            if (Session.Current is not { } s || ship.Sim != s.Game.Sim || !Tele.On(Cat.Combat)) return;
            long t0 = Stats.Enter();
            try
            {
                bool human = s.Game.HumanPlayersIndexes.Contains(oldAllegiance);
                if (human) s.AddTechHint("ship_lost");
                Tele.Emit(Cat.Combat, "combat.destroyed", w =>
                {
                    w.WriteString("ship", ship.UniqueID.ToString());
                    w.WriteString("name", ship.Metadata.PlainShipName);
                    w.WriteNumber("player", oldAllegiance);
                    w.WriteString("side", human ? "player" : ship.HasTag(HunterSpawner.HunterTag) ? "hunter" : IsHostile(s, oldAllegiance) ? "hostile" : "neutral");
                    if (Cosmoteer.Factions.FactionRules.GetFaction(oldAllegiance) is { } f) w.WriteString("faction", f.ID.ToString());
                    w.WriteBoolean("hunter", ship.HasTag(HunterSpawner.HunterTag));
                    w.WriteNumber("value", ship.BaseBlueprintCost);
                    if (ship.Tags is { } tags) { w.WriteStartArray("tags"); foreach (var t in tags) w.WriteStringValue(t.ToString()); w.WriteEndArray(); }
                });
            }
            catch (Exception ex) { Errors.Report("telemetry junked", ex.Message, ex); }
            finally { Stats.Exit(t0); }
        }
    }

    /// <summary>A ship that leaves the system for good (not into stasis): a hunter that jumps out, a trader that exits, a test removal.</summary>
    [HarmonyPatch(typeof(SimShipsManager), nameof(SimShipsManager.Remove))]
    static class RemovedPatch
    {
        static void Prefix(SimShipsManager __instance, Ship ship, bool dispose, bool doingLocalJump)
        {
            if (!dispose || doingLocalJump || ship.IsJunk || ship.IsHumanPlayerShip) return;
            if (Session.Current is not { } s || __instance.Sim != s.Game.Sim || !Tele.On(Cat.Combat)) return;
            long t0 = Stats.Enter();
            try
            {
                string caller = Caller();
                // A sim that closes removes every ship; that is no event.
                if (caller.Contains("SimRoot.Dispose")) return;
                Tele.Emit(Cat.Combat, "combat.removed", w =>
                {
                    Session.WriteShip(w, ship);
                    w.WriteString("src", Source(null, caller));
                    w.WriteString("caller", caller);
                });
            }
            catch (Exception ex) { Errors.Report("telemetry removed", ex.Message, ex); }
            finally { Stats.Exit(t0); }
        }
    }

    internal static void OnCrewKilled(object? sender, Cosmoteer.Crew.CrewDeathArgs e)
    {
        if (Session.Current is not { } s || !Tele.On(Cat.Combat)) return;
        var crew = e.Crew;
        if (!s.Game.HumanPlayersIndexes.Contains(crew.PlayerIndex)) return;
        int tech = crew.CarriedResource.QuantityOfType(Vaults.TechId);
        if (tech > 0) s.AddTechHint("crew_death");
        Tele.Emit(Cat.Combat, "combat.crew_died", w =>
        {
            w.WriteNumber("player", crew.PlayerIndex);
            w.WriteString("cause", e.Cause.ToString());
            w.WriteNumber("carried_tech", tech);
        });
    }

    // Damage sums by the side of the damaged ship, flushed every AccrualSeconds. Hits come from parallel part updates, so a lock guards them.
    static readonly Lock DamageGate = new();
    static readonly Dictionary<string, (long Damage, long Repaired, int PartsLost)> Damage = [];
    static readonly Dictionary<int, bool> HostileByPlayer = [];

    static bool IsHostile(Session s, int playerIndex)
    {
        lock (HostileByPlayer)
        {
            if (HostileByPlayer.TryGetValue(playerIndex, out bool hostile)) return hostile;
            hostile = s.Game.Sim.Mode is { } mode && s.Game.HumanPlayersIndexes.Any(h => mode.AreEnemies(playerIndex, h));
            HostileByPlayer[playerIndex] = hostile;
            return hostile;
        }
    }

    [HarmonyPatch(typeof(Part), "OnHealthChanged")]
    static class DamagePatch
    {
        static void Postfix(Part __instance, int oldHealth, int newHealth)
        {
            if (oldHealth == newHealth || !Tele.On(Cat.Combat) || Session.Current is not { } s || __instance.Ship is not { } ship || ship.IsJunk) return;
            long t0 = Stats.Enter();
            try
            {
                string side = ship.IsHumanPlayerShip ? "player" : ship.HasTag(HunterSpawner.HunterTag) ? "hunter" : IsHostile(s, ship.Metadata.PlayerIndex) ? "hostile" : "neutral";
                AddDamage(side, oldHealth - newHealth, newHealth == 0);
            }
            finally { Stats.Exit(t0); }
        }
    }

    /// <summary>Adds a health change to the sums of a side. Negative damage is a repair.</summary>
    static void AddDamage(string side, int damage, bool partLost)
    {
        lock (DamageGate)
        {
            var d = Damage.GetValueOrDefault(side);
            if (damage > 0) { d.Damage += damage; if (partLost) d.PartsLost++; }
            else d.Repaired -= damage;
            Damage[side] = d;
        }
    }

    internal static void FlushDamage()
    {
        KeyValuePair<string, (long Damage, long Repaired, int PartsLost)>[] sums;
        lock (DamageGate)
        {
            sums = [.. Damage];
            Damage.Clear();
        }
        // Allegiances change (wanted states, surrender), so the hostility cache starts over each window.
        lock (HostileByPlayer) HostileByPlayer.Clear();
        if (sums.Length == 0) return;
        Tele.Emit(Cat.Combat, "combat.damage", w =>
        {
            Tele.Num(w, "window_s", Run.Current?.T.Telemetry.AccrualSeconds ?? 0);
            foreach (var (side, d) in sums)
            {
                w.WriteStartObject(side);
                w.WriteNumber("damage", d.Damage);
                w.WriteNumber("repaired", d.Repaired);
                w.WriteNumber("parts_lost", d.PartsLost);
                w.WriteEndObject();
            }
        });
    }

    // ---- AI: the strategy module that won each AI tick (the loop stops at the first module that returns true), and active tactics.

    static readonly Lock AiGate = new();
    static readonly Dictionary<ObjectID, (string Module, double At)> AiCurrent = [];
    static readonly Dictionary<string, (long Ticks, long Switches)> AiSums = [];
    static long _aiWindow;
    static int _aiEvents;
    static long _focusEvals, _focusBoosted;

    internal static IEnumerable<MethodBase> ModuleMethods(Type baseType, params string[] names)
    {
        var assemblies = new[] { typeof(ShipAI).Assembly, typeof(TelemetryPatches).Assembly };
        foreach (var type in assemblies.SelectMany(a => a.GetTypes()))
        {
            if (type.IsAbstract || !baseType.IsAssignableFrom(type)) continue;
            foreach (string name in names)
                if (type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is { } m) yield return m;
        }
    }

    [HarmonyPatch]
    static class StrategyPatch
    {
        static IEnumerable<MethodBase> TargetMethods() => ModuleMethods(typeof(AIStrategyModule), nameof(AIStrategyModule.OnAITick));

        static void Postfix(AIStrategyModule __instance, Ship __0, bool __result)
        {
            if (__result && Tele.On(Cat.Ai)) Decided(__0, __instance.GetType().Name);
        }
    }

    [HarmonyPatch]
    static class TacticPatch
    {
        static IEnumerable<MethodBase> TargetMethods() =>
            ModuleMethods(typeof(AITacticsModule), nameof(AITacticsModule.UpdateAttackAngle), nameof(AITacticsModule.UpdateAttackRadiusAndRotation));

        static void Postfix(AITacticsModule __instance, bool __result)
        {
            if (!__result || !Tele.On(Cat.Ai)) return;
            string key = "tactic:" + __instance.GetType().Name;
            lock (AiGate)
            {
                var sum = AiSums.GetValueOrDefault(key);
                AiSums[key] = (sum.Ticks + 1, sum.Switches);
            }
        }
    }

    [HarmonyPatch(typeof(CareerSimModeManager), nameof(CareerSimModeManager.ModifyAIAttackValue))]
    static class FocusCountPatch
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(ref float value, out float __state) => __state = value;

        [HarmonyPriority(Priority.Last)]
        static void Postfix(ref float value, float __state)
        {
            if (!Tele.On(Cat.Ai)) return;
            Interlocked.Increment(ref _focusEvals);
            if (value > __state) Interlocked.Increment(ref _focusBoosted);
        }
    }

    static void Decided(Ship ship, string module)
    {
        if (Session.Current is not { } s || ship.Sim != s.Game.Sim) return;
        long t0 = Stats.Enter();
        try
        {
            var t = s.Run.T.Telemetry;
            double now = s.SimSeconds;
            string? from = null;
            bool emit = false;
            lock (AiGate)
            {
                var sum = AiSums.GetValueOrDefault(module);
                bool changed = !AiCurrent.TryGetValue(ship.UniqueID, out var cur) || cur.Module != module;
                AiSums[module] = (sum.Ticks + 1, sum.Switches + (changed ? 1 : 0));
                if (!changed) return;
                from = cur.Module;
                if (cur.Module is null || now - cur.At >= t.AiSwitchMinSeconds)
                {
                    long minute = Stopwatch.GetTimestamp() / Stopwatch.Frequency / 60;
                    if (_aiWindow != minute) { _aiWindow = minute; _aiEvents = 0; }
                    emit = ++_aiEvents <= t.AiEventsPerMinute;
                }
                AiCurrent[ship.UniqueID] = (module, now);
            }
            if (!emit) return;
            Tele.Emit(Cat.Ai, "ai.switch", w =>
            {
                w.WriteString("ship", ship.UniqueID.ToString());
                w.WriteString("name", ship.Metadata.PlainShipName);
                w.WriteString("side", Session.Side(ship));
                w.WriteString("ai", ship.AI?.Rules.ID.ToString());
                w.WriteString("from", from);
                w.WriteString("to", module);
                int blueprint = ship.BaseBlueprintCost;
                Tele.Num(w, "integrity", blueprint > 0 ? (double)ship.BasePhysicalValue / blueprint : 1);
                if (SimQueries.FleetCenter(ship.Sim!) is { } c) Tele.Num(w, "dist", ship.DetWorldCenter.DistanceTo(c));
            });
        }
        catch (Exception ex) { Errors.Report("telemetry ai", ex.Message, ex); }
        finally { Stats.Exit(t0); }
    }

    /// <summary>Cumulative AI sums of the session for the snapshot: ticks and switches per strategy module, ticks per tactic, focus counts.</summary>
    internal static void WriteAiSums(Utf8JsonWriter w)
    {
        KeyValuePair<string, (long Ticks, long Switches)>[] sums;
        lock (AiGate) sums = [.. AiSums.OrderBy(kv => kv.Key)];
        w.WriteStartObject("ai");
        foreach (var (k, v) in sums)
        {
            w.WriteStartObject(k);
            w.WriteNumber("ticks", v.Ticks);
            if (!k.StartsWith("tactic:")) w.WriteNumber("switches", v.Switches);
            w.WriteEndObject();
        }
        w.WriteNumber("focus_evals", Interlocked.Read(ref _focusEvals));
        w.WriteNumber("focus_boosted", Interlocked.Read(ref _focusBoosted));
        w.WriteEndObject();
    }

    internal static void ResetSession()
    {
        lock (AiGate) { AiCurrent.Clear(); AiSums.Clear(); }
        lock (DamageGate) Damage.Clear();
        lock (HostileByPlayer) HostileByPlayer.Clear();
        Interlocked.Exchange(ref _focusEvals, 0);
        Interlocked.Exchange(ref _focusBoosted, 0);
    }

    // ---- Co-op

    [HarmonyPatch(typeof(BaseMPManager), "OnOutOfSyncRpc")]
    static class DesyncPatch
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(BaseMPManager __instance)
        {
            if (__instance._isOutOfSync) return;
            Tele.Emit(Cat.Coop, "mp.desync", w =>
            {
                w.WriteNumber("input_tick", __instance._curInputTick);
                w.WriteBoolean("host", __instance.IsHost);
                w.WriteBoolean("expected", Dev.MpAutotest.ExpectResync);
            });
        }
    }

    // ---- Lifecycle: save, load, game start, system switch

    const string SavePath = "HotCargoTelemetry";

    [HarmonyPatch(typeof(CareerGameModeManager))]
    static class LifecyclePatches
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(nameof(CareerGameModeManager.OnGameStarting))]
        static void OnGameStarting(CareerGameModeManager __instance, bool loaded) =>
            Errors.Guard("telemetry start", () => { ResetSession(); Session.Start(__instance, loaded); });

        [HarmonyPostfix, HarmonyPatch(MethodType.Constructor, typeof(GenericSerialReader))]
        static void LoadConstructor(GenericSerialReader reader) => Errors.Guard("telemetry load", () =>
        {
            Session.PendingLoaded = reader.TryReadFromPath<TelemetrySaveData>(SavePath, out var data) ? data : null;
        });

        [HarmonyPostfix, HarmonyPatch("WriteTo")]
        static void WriteTo(CareerGameModeManager __instance, GenericSerialWriter writer) => Errors.Guard("telemetry save", () =>
        {
            if (Session.Current is not { } s || s.Run.Mode != __instance) return;
            writer.WriteToPath(SavePath, new TelemetrySaveData { RunId = s.RunId, Sessions = s.SessionNumber });
            Tele.Emit(Cat.Run, "run.save", w => w.WriteString("caller", Caller()));
        });

        [HarmonyPrefix, HarmonyPriority(Priority.Last), HarmonyPatch(nameof(CareerGameModeManager.OnSwitchingSimulation))]
        static void BeforeSwitch(CareerGameModeManager __instance) => Errors.Guard("telemetry jump", () =>
        {
            if (Session.Current is { } s && s.Run.Mode == __instance) s.BeforeSwitch();
        });
    }
}
