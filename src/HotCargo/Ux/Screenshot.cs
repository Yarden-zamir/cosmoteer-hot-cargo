using Halfling;
using Halfling.Graphics;
using HarmonyLib;

namespace HotCargo.Ux;

/// <summary>
/// Screen capture for autotests, without a visible window and without focus (UX.md, "Screenshots").
/// The scripts start the game minimized. The D3D11 graphics manager skips all drawing while the window is minimized
/// (CanRender => !Window.IsMinimized), so "render on" forces CanRender. The game then draws into the swap chain
/// back buffer, which keeps its last size, and Present still skips because the window is occluded.
/// "screenshot NAME" copies the back buffer at the end of a frame into Mods/hot_cargo/screenshots/NAME.png.
/// Headless test slots have no graphics device, so this works only in the player's slot (main).
/// </summary>
public static class Screenshot
{
    static bool _forceRender;

    internal static void Register(Harmony harmony)
    {
        // The WDX platform assembly is not referenced at build time, so the patch target is found by name.
        var type = AccessTools.TypeByName("Halfling.Graphics.D3D11.D3D11GraphicsManager");
        if (type is null)
        {
            Log.Info("ux: D3D11GraphicsManager not found (headless?); screenshots are off");
            return;
        }
        var getter = AccessTools.PropertyGetter(type, "CanRender")
            ?? throw new MissingMemberException(type.FullName, "CanRender");
        harmony.Patch(getter, postfix: new HarmonyMethod(typeof(Screenshot), nameof(CanRenderPostfix)));
    }

    static void CanRenderPostfix(ref bool __result)
    {
        if (_forceRender) __result = true;
    }

    public static void SetForceRender(bool on)
    {
        _forceRender = on;
        Log.Info($"ux: force render {(on ? "on" : "off")}; window minimized={App.Window.IsMinimized} size={App.Window.Size}");
    }

    public static async void Capture(string name)
    {
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"bad screenshot name '{name}'");
        string dir = Path.Combine(Mod.ModRoot, "screenshots");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name + ".png");
        try
        {
            using TextureData td = await App.Graphics.GetEndOfFrameScreenShot();
            td.SaveTo(path, ImageFileFormat.Png);
            Log.Info($"ux: screenshot {name}.png {td.PixelSize}");
        }
        catch (Exception ex)
        {
            Log.Error($"ux: screenshot {name} failed", ex);
        }
    }
}
