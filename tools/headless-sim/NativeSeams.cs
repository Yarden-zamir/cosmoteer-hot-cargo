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
        // FreeImage (TeximpNet): image encoding. Co-op setup sends the ship icon as PNG bytes (GameSetupScreen add-ship RPC).
        harmony.Patch(AccessTools.Method(typeof(TextureData), nameof(TextureData.SaveTo), [typeof(Stream), typeof(ImageFileFormat), typeof(int)]),
            prefix: new HarmonyMethod(typeof(NativeSeams), nameof(SaveBlankPng)));
        // nvtt (TeximpNet): GPU texture compression. The null graphics device never uploads textures.
        harmony.Patch(AccessTools.Method(typeof(TextureData), nameof(TextureData.Compress)),
            prefix: new HarmonyMethod(typeof(NativeSeams), nameof(Skip)));
        // Not native, but PNG-only: these list PNG folders that exist only on the PC. Roof art is visual only.
        harmony.Patch(AccessTools.Method(typeof(Cosmoteer.Ships.ShipRoofRules), nameof(Cosmoteer.Ships.ShipRoofRules.LoadRoofTextures)),
            prefix: new HarmonyMethod(typeof(NativeSeams), nameof(PlaceholderRoof)));
        harmony.Patch(AccessTools.Method(typeof(Cosmoteer.Ships.ShipRoofRules), nameof(Cosmoteer.Ships.ShipRoofRules.BuildRoofDecalsAtlas)),
            prefix: new HarmonyMethod(typeof(NativeSeams), nameof(Skip)));
        // Ship decals (paint) look up that skipped atlas. Decals are cosmetic.
        harmony.Patch(AccessTools.Method(typeof(Cosmoteer.Ships.Paint.DecalsManager), nameof(Cosmoteer.Ships.Paint.DecalsManager.LoadSaveData)),
            prefix: new HarmonyMethod(typeof(NativeSeams), nameof(Skip)));
        harmony.Patch(AccessTools.Method(typeof(Halfling.Audio.SoundStream), nameof(Halfling.Audio.SoundStream.FromFile)),
            prefix: new HarmonyMethod(typeof(NativeSeams), nameof(SilentIfMissing)));
    }

    static bool Skip() => false;

    /// <summary>Writes a blank PNG of the texture's size. The sim never reads icon pixels, and LoadFromStream decodes any PNG.</summary>
    static bool SaveBlankPng(TextureData __instance, Stream stream)
    {
        var size = __instance.PixelSize;
        using var image = new Image<Rgba32>(Math.Max(1, size.X), Math.Max(1, size.Y));
        image.SaveAsPng(stream);
        return false;
    }

    /// <summary>
    /// Only the default roof texture gets a placeholder: ship metadata reads it, and saves write its file name.
    /// It loads through the texture factory, so its file path is the real one (the file itself is missing).
    /// </summary>
    static bool PlaceholderRoof(Cosmoteer.Ships.ShipRoofRules __instance)
    {
        if (__instance is { DefaultRoofTextureID: { } id, RoofTexturesFolders: [var folder, ..] })
            __instance.RoofTextures[id] = Halfling.App.Graphics.Textures.Load(folder / $"{id}.png")!;
        return false;
    }

    static readonly byte[] SilentWav = MakeSilentWav();

    // Stereo 44.1 kHz: music streams require the format in Data/music/music.rules (ChannelCount, Frequency). The rules are
    // still loading when the first sound loads, so they cannot be read here. Revisit if music.rules changes its format.
    static byte[] MakeSilentWav()
    {
        var silence = new MemoryStream();
        using (var writer = new NAudio.Wave.WaveFileWriter(new NAudio.Utils.IgnoreDisposeStream(silence), new NAudio.Wave.WaveFormat(44100, 16, 2)))
            writer.Write(new byte[1764], 0, 1764);
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
