using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;

/// <summary>
/// The game ships win-x64 ReadyToRun images. CoreCLR on macOS arm64 refuses them ("The data is invalid").
/// This resolver rewrites each game assembly to an IL-only AnyCPU copy and loads that copy instead.
/// </summary>
static class GameAssemblies
{
    static readonly string Bin = typeof(GameAssemblies).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "CosmoteerBin").Value ?? throw new InvalidOperationException("CosmoteerBin metadata missing");

    static readonly string Cache = Path.Combine(AppContext.BaseDirectory, "il-only");

    public static void Install()
    {
        Directory.CreateDirectory(Cache);
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            var source = Path.Combine(Bin, name.Name + ".dll");
            return File.Exists(source) ? ctx.LoadFromAssemblyPath(IlOnlyCopy(source)) : null;
        };
    }

    static string IlOnlyCopy(string source)
    {
        var target = Path.Combine(Cache, Path.GetFileName(source));
        if (File.Exists(target) && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(source))
            return target;
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Bin);
        resolver.AddSearchDirectory(AppContext.BaseDirectory);
        using (var module = ModuleDefinition.ReadModule(source, new ReaderParameters { AssemblyResolver = resolver }))
        {
            module.Architecture = TargetArchitecture.I386;
            module.Attributes = ModuleAttributes.ILOnly;
            module.Write(target + ".tmp");
        }
        File.Move(target + ".tmp", target, overwrite: true);
        return target;
    }
}
