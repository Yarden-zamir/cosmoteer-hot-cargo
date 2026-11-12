using Halfling.Geometry;
using Halfling.Graphics;
using HarmonyLib;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

/// <summary>
/// Replaces game calls into Windows native libraries. Each patch names the native library it avoids.
/// </summary>
static class NativeSeams
{
    public static void Apply(Harmony harmony)
    {
        // FreeImage (TeximpNet): image decoding. ImageSharp is managed and already a game dependency.
        harmony.Patch(AccessTools.Method(typeof(TextureData), nameof(TextureData.LoadFrom), [typeof(Stream)]),
            prefix: new HarmonyMethod(typeof(NativeSeams), nameof(LoadFromStream)));
        harmony.Patch(AccessTools.Method(typeof(TextureData), nameof(TextureData.LoadFrom), [typeof(string)]),
            prefix: new HarmonyMethod(typeof(NativeSeams), nameof(LoadFromFile)));
        harmony.Patch(AccessTools.Method(typeof(TextureData), nameof(TextureData.LoadFrom), [typeof(string), typeof(PostProcessParams)]),
            prefix: new HarmonyMethod(typeof(NativeSeams), nameof(LoadFromFile)));
        // nvtt (TeximpNet): GPU texture compression. The null graphics device never uploads textures.
        harmony.Patch(AccessTools.Method(typeof(TextureData), nameof(TextureData.Compress)),
            prefix: new HarmonyMethod(typeof(NativeSeams), nameof(Skip)));
        // Not native, but PNG-only: these list PNG folders that exist only on the PC. Roof art is visual only.
        foreach (var name in new[] { nameof(Cosmoteer.Ships.ShipRoofRules.LoadRoofTextures), nameof(Cosmoteer.Ships.ShipRoofRules.BuildRoofDecalsAtlas) })
            harmony.Patch(AccessTools.Method(typeof(Cosmoteer.Ships.ShipRoofRules), name), prefix: new HarmonyMethod(typeof(NativeSeams), nameof(Skip)));
        // Not native: scripts/pull-game.sh copies only *.rules and *.txt, so the name generator files (*.markov) are missing.
        harmony.Patch(AccessTools.Constructor(typeof(Cosmoteer.Generators.Names.MarkovGenerator), [typeof(Halfling.Serialization.Generic.GenericSerialReader)]),
            prefix: new HarmonyMethod(typeof(NativeSeams), nameof(MarkovWithoutFile)));
        harmony.Patch(AccessTools.Method(typeof(Halfling.Audio.SoundStream), nameof(Halfling.Audio.SoundStream.FromFile)),
            prefix: new HarmonyMethod(typeof(NativeSeams), nameof(SilentIfMissing)));
        // Not native: ship designs are *.ship.png files and exist only on the PC. A missing ship file loads the vanilla
        // example mod ship instead, under its own name. Every built-in ship then has the same design.
        harmony.Patch(AccessTools.PropertyGetter(typeof(Cosmoteer.Ships.ShipFile), nameof(Cosmoteer.Ships.ShipFile.FilePath)),
            postfix: new HarmonyMethod(typeof(NativeSeams), nameof(FallbackShipFile)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(Cosmoteer.Ships.ShipFile), nameof(Cosmoteer.Ships.ShipFile.ShipName)),
            prefix: new HarmonyMethod(typeof(NativeSeams), nameof(OriginalShipName)));
    }

    static readonly Halfling.IO.AbsolutePath FallbackShip = new(Path.GetFullPath("StandardMods/example_mod/ships/Arclight.ship.png"));

    static void FallbackShipFile(ref Halfling.IO.AbsolutePath __result)
    {
        if (__result is not null && !File.Exists(__result)) __result = FallbackShip;
    }

    static bool OriginalShipName(Cosmoteer.Ships.ShipFile __instance, ref string __result)
    {
        var original = Traverse.Create(__instance).Field("<FilePath>k__BackingField").GetValue<Halfling.IO.AbsolutePath>();
        __result = Cosmoteer.Ships.Ship.GetNameFromFilename(original) ?? throw new InvalidOperationException($"no ship name in {original}");
        return false;
    }

    static bool MarkovWithoutFile(Cosmoteer.Generators.Names.MarkovGenerator __instance, Halfling.Serialization.Generic.GenericSerialReader reader)
    {
        var path = reader.ReadFromPath<Halfling.IO.AbsolutePath>("MarkovFile");
        if (File.Exists(path)) return true;
        reader.ReflectiveRead(__instance);
        var chain = new Halfling.Generation.MarkovChain<char>(2);
        foreach (var word in new[] { "alder", "brenna", "corvin", "dalia", "emrys", "fenna", "garrick", "halley", "isolde", "jorund" })
            chain.Learn((IReadOnlyList<char>)(word + '\0').ToCharArray());
        Traverse.Create(__instance).Field(nameof(__instance.MarkovChain)).SetValue(chain);
        return false;
    }

    static bool Skip() => false;

    static readonly byte[] SilentWav = MakeSilentWav();

    static byte[] MakeSilentWav()
    {
        var silence = new MemoryStream();
        using (var writer = new NAudio.Wave.WaveFileWriter(new NAudio.Utils.IgnoreDisposeStream(silence), new NAudio.Wave.WaveFormat(44100, 16, 1)))
            writer.Write(new byte[882], 0, 882);
        return silence.ToArray();
    }

    /// <summary>Sound and music files exist only on the PC. A missing one becomes 10 ms of silence.</summary>
    static bool SilentIfMissing(string path, ref Halfling.Audio.SoundStream __result)
    {
        if (File.Exists(path)) return true;
        __result = new Halfling.Audio.WavSoundStream(new MemoryStream(SilentWav), closeStreamOnDispose: true);
        return false;
    }

    static bool LoadFromStream(Stream stream, ref TextureData __result)
    {
        __result = Decode(stream);
        return false;
    }

    /// <summary>PNG files exist only on the PC. A missing image becomes a blank placeholder; the sim does not read texture pixels.</summary>
    static bool LoadFromFile(string filepath, ref TextureData __result)
    {
        __result = LoadOrPlaceholder(filepath, mipLevels: 1);
        return false;
    }

    /// <summary>
    /// Replaces GameApp's loader (TextureCache.LoadTexture: disk cache, mipmaps, compression). Call after GameApp creates App.Graphics.
    /// </summary>
    public static void PlaceholderTextures()
    {
        Halfling.App.Graphics.Textures.CustomTextureLoader = (string filepath, PostProcessParams p, out IntVector2 sourceSize) =>
        {
            var data = LoadOrPlaceholder(filepath, p.MipLevels);
            data.ApplyPostProcessParams(p);
            sourceSize = data.PixelSize;
            return data;
        };
    }

    /// <summary>A placeholder is just big enough for the requested mip levels (0 means a full chain, which 1x1 satisfies).</summary>
    static TextureData LoadOrPlaceholder(string filepath, int mipLevels)
    {
        if (!File.Exists(filepath)) return new TextureData(new IntVector2(1 << Math.Max(0, mipLevels - 1)));
        using var stream = File.OpenRead(filepath);
        return Decode(stream);
    }

    static TextureData Decode(Stream stream)
    {
        using var image = Image.Load<Rgba32>(stream);
        var data = new TextureData(new IntVector2(image.Width, image.Height));
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                var p = image[x, y];
                data[x, y] = new IntColor(p.R, p.G, p.B, p.A);
            }
        return data;
    }
}
