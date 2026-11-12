using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;
using TypeAttributes = Mono.Cecil.TypeAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;

/// <summary>
/// The game ships win-x64 ReadyToRun images. CoreCLR on macOS arm64 refuses them ("The data is invalid").
/// This resolver rewrites each game assembly to an IL-only AnyCPU copy and loads that copy instead.
/// It also serves stand-ins (standins/) for game dependencies whose exact version is not on NuGet (Steamworks.NET),
/// and the WindowsDesktop reference assemblies (WinForms) as stubs that throw.
/// A stand-in gets a throwing stub for each type and member that the game references but the stand-in lacks,
/// because the JIT compiles a whole method, also the branches that a runtime guard (Steam.IsInitialized) skips.
/// </summary>
static class GameAssemblies
{
    static readonly string Bin = typeof(GameAssemblies).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "CosmoteerBin").Value ?? throw new InvalidOperationException("CosmoteerBin metadata missing");

    static readonly string WindowsDesktopRef = typeof(GameAssemblies).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "WindowsDesktopRef").Value ?? throw new InvalidOperationException("WindowsDesktopRef metadata missing");

    static readonly string StandIns = Path.Combine(AppContext.BaseDirectory, "standins");
    static readonly string Cache = Path.Combine(AppContext.BaseDirectory, "il-only");
    static readonly string[] GameModules = ["Cosmoteer.dll", "HalflingCore.dll"];

    public static void Install()
    {
        Directory.CreateDirectory(Cache);
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            var source = Path.Combine(Bin, name.Name + ".dll");
            if (File.Exists(source)) return ctx.LoadFromAssemblyPath(IlOnlyCopy(source, standIn: false));
            var standIn = Path.Combine(StandIns, name.Name + ".dll");
            if (File.Exists(standIn)) return ctx.LoadFromAssemblyPath(IlOnlyCopy(standIn, standIn: true));
            var reference = Path.Combine(WindowsDesktopRef, name.Name + ".dll");
            return File.Exists(reference) ? ctx.LoadFromAssemblyPath(IlOnlyCopy(reference, standIn: false)) : null;
        };
    }

    static string IlOnlyCopy(string source, bool standIn)
    {
        var target = Path.Combine(Cache, Path.GetFileName(source));
        if (File.Exists(target) && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(source))
            return target;
        using (var resolver = Resolver())
        using (var module = ModuleDefinition.ReadModule(source, new ReaderParameters { AssemblyResolver = resolver }))
        {
            if (standIn) AddMissingStubs(module);
            // A reference assembly (bodies are "throw null") runs once it no longer says it is one.
            var marker = module.Assembly.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.ReferenceAssemblyAttribute");
            if (marker is not null) module.Assembly.CustomAttributes.Remove(marker);
            module.Architecture = TargetArchitecture.I386;
            module.Attributes = ModuleAttributes.ILOnly;
            // A temp name per process: parallel runs (scripts/autotest-mac.sh) can write the same copy at the same time.
            module.Write($"{target}.{Environment.ProcessId}.tmp");
        }
        File.Move($"{target}.{Environment.ProcessId}.tmp", target, overwrite: true);
        return target;
    }

    static DefaultAssemblyResolver Resolver()
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Bin);
        resolver.AddSearchDirectory(StandIns);
        resolver.AddSearchDirectory(WindowsDesktopRef);
        resolver.AddSearchDirectory(AppContext.BaseDirectory);
        return resolver;
    }

    static void AddMissingStubs(ModuleDefinition standIn)
    {
        string name = standIn.Assembly.Name.Name;
        using var resolver = Resolver();
        var game = GameModules.Select(m => ModuleDefinition.ReadModule(Path.Combine(Bin, m), new ReaderParameters { AssemblyResolver = resolver })).ToList();
        // Game references carry the game's version. Match them by name against the stand-in that is being written.
        TypeDefinition? Find(TypeReference t) => t.DeclaringType is { } outer
            ? Find(outer)?.NestedTypes.FirstOrDefault(n => n.Name == t.Name)
            : standIn.GetType(t.Namespace, t.Name);

        // A missing top-level type is added when a signature first uses it: only a signature says whether it is a value type.
        TypeDefinition FindOrAdd(TypeReference t)
        {
            if (Find(t) is { } found) return found;
            if (t.DeclaringType is not null) throw new InvalidOperationException($"stand-in {name} lacks nested type {t.FullName}");
            var stub = new TypeDefinition(t.Namespace, t.Name, TypeAttributes.Public | TypeAttributes.SequentialLayout | (t.IsValueType ? TypeAttributes.Sealed : 0),
                standIn.ImportReference(t.IsValueType ? typeof(ValueType) : typeof(object)));
            standIn.Types.Add(stub);
            Out.Line($"stand-in {name}: added {(t.IsValueType ? "struct" : "class")} {t.FullName}");
            return stub;
        }

        foreach (var r in game.SelectMany(m => m.GetMemberReferences()).OfType<MethodReference>())
        {
            if (r.DeclaringType is not { IsArray: false, IsGenericInstance: false } dt || dt.Scope?.Name != name) continue;
            var owner = Find(r.DeclaringType);
            if (owner is null || owner.HasGenericParameters || r.HasGenericParameters) continue;
            if (owner.Methods.Any(m => m.Name == r.Name && SameSignature(m, r))) continue;
            var stub = new MethodDefinition(r.Name, MethodAttributes.Public | (r.HasThis ? 0 : MethodAttributes.Static), Import(standIn, r.ReturnType, FindOrAdd));
            foreach (var p in r.Parameters) stub.Parameters.Add(new ParameterDefinition(Import(standIn, p.ParameterType, FindOrAdd)));
            var il = stub.Body.GetILProcessor();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Throw);
            owner.Methods.Add(stub);
            Out.Line($"stand-in {name}: added method {r.FullName}");
        }
        // Types that only appear outside method signatures (typeof, locals). Without a signature, assume a class.
        foreach (var t in game.SelectMany(m => m.GetTypeReferences()).Where(t => t.Scope.Name == name && t.DeclaringType is null))
            FindOrAdd(t);
        game.ForEach(m => m.Dispose());
    }

    static bool SameSignature(MethodDefinition m, MethodReference r) =>
        m.Parameters.Count == r.Parameters.Count && m.ReturnType.FullName == r.ReturnType.FullName
        && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(r.Parameters.Select(p => p.ParameterType.FullName));

    /// <summary>Maps a type from a game signature into the stand-in: its own types by name, everything else imported.</summary>
    static TypeReference Import(ModuleDefinition standIn, TypeReference t, Func<TypeReference, TypeDefinition> find) => t switch
    {
        ArrayType a => new ArrayType(Import(standIn, a.ElementType, find), a.Rank),
        ByReferenceType b => new ByReferenceType(Import(standIn, b.ElementType, find)),
        _ when t.Scope.Name == standIn.Assembly.Name.Name => find(t),
        _ => standIn.ImportReference(t),
    };
}
