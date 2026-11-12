using System.Diagnostics;
using System.Reflection;
using HarmonyLib;

namespace HotCargo.Telemetry;

/// <summary>
/// Traces the methods in Telemetry.TraceMethods: one "trace" event per call with the arguments, the result and the duration.
/// It patches at startup with the default list, so a change of the list needs a game restart. A missing method is a warning, not an error,
/// because other areas rename their code while they work.
/// </summary>
static class Trace
{
    static readonly HashSet<string> Missing = [];
    internal static IReadOnlyCollection<string> MissingMethods => Missing;

    internal static void Install(Harmony harmony, string[] specs)
    {
        var prefix = new HarmonyMethod(AccessTools.Method(typeof(Trace), nameof(Prefix)));
        var postfixVoid = new HarmonyMethod(AccessTools.Method(typeof(Trace), nameof(PostfixVoid)));
        var postfixResult = new HarmonyMethod(AccessTools.Method(typeof(Trace), nameof(PostfixResult)));
        foreach (string spec in specs)
        {
            int colon = spec.IndexOf(':');
            string typeName = spec[..colon], methodName = spec[(colon + 1)..];
            var type = AccessTools.TypeByName(typeName);
            var methods = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == methodName && !m.IsAbstract && !m.ContainsGenericParameters).ToList() ?? [];
            if (methods.Count == 0)
            {
                Missing.Add(spec);
                Log.Info($"telemetry: trace target {spec} not found; it is skipped");
                continue;
            }
            foreach (var m in methods)
            {
                try
                {
                    harmony.Patch(m, prefix: prefix, postfix: m.ReturnType == typeof(void) ? postfixVoid : postfixResult);
                    Stats.TracePatched++;
                }
                catch (Exception ex)
                {
                    Missing.Add(spec);
                    Log.Error($"telemetry: trace patch of {spec} failed", ex);
                }
            }
        }
        Stats.TraceMissing = Missing.Count;
    }

    static void Prefix(out long __state) => __state = Tele.On(Cat.Trace) ? Stopwatch.GetTimestamp() : 0;

    static void PostfixVoid(MethodBase __originalMethod, object[] __args, long __state) => Emit(__originalMethod, __args, null, false, __state);

    static void PostfixResult(MethodBase __originalMethod, object[] __args, object? __result, long __state) => Emit(__originalMethod, __args, __result, true, __state);

    static void Emit(MethodBase method, object[] args, object? result, bool hasResult, long startedAt)
    {
        if (startedAt == 0) return;
        double ms = Stats.Ms(Stopwatch.GetTimestamp() - startedAt);
        long t0 = Stats.Enter();
        try
        {
            var parameters = method.GetParameters();
            Tele.Emit(Cat.Trace, "trace", w =>
            {
                w.WriteString("method", $"{method.DeclaringType?.Name}.{method.Name}");
                w.WriteStartObject("args");
                for (int i = 0; i < args.Length && i < parameters.Length; i++)
                {
                    string label = Reflect.Label(args[i]);
                    if (label != "-") w.WriteString(parameters[i].Name ?? $"arg{i}", label);
                }
                w.WriteEndObject();
                if (hasResult) w.WriteString("result", Reflect.Label(result));
                Tele.Num(w, "ms", ms);
                string ctx = Tele.Path;
                if (ctx.Length > 0) w.WriteString("ctx", ctx);
            });
        }
        finally { Stats.Exit(t0); }
    }
}
