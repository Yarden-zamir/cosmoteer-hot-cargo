using System.Diagnostics.CodeAnalysis;
using Halfling.Audio;
using Halfling.Audio.Null;
using Halfling.Geometry;
using Halfling.Graphics;
using Halfling.Graphics.Null;
using Halfling.Input;
using Halfling.Input.Null;
using Halfling.IO;
using Halfling.Platforms;
using Halfling.Windows;
using Halfling.Windows.Null;

/// <summary>
/// Replaces the Windows platform (HalflingPlatformWDX: Direct3D, XAudio2, Win32). Every device is the game's own
/// null device, the same ones that GameApp uses under --headless.
/// </summary>
sealed class HeadlessPlatform : Platform
{
    public override MachineID MachineID { get; } = new(1, 1);

    public override Window CreateWindow(WindowMode windowMode = WindowMode.ResizableWindow, IntVector2 windowedLocation = default,
        IntVector2 windowedSize = default, ScreenMode fullScreenMode = default, bool maximized = false, VSyncMode vSync = VSyncMode.Off,
        AntialiasMode antialiasMode = 0, int adapterIndex = 0, int monitorIndex = 0, string caption = "", string? iconFile = null,
        bool gfxDebug = false, bool autoHighPerfGpu = true, string? featureLevel = null) =>
        new NullWindow(windowMode, windowedLocation, windowedSize, fullScreenMode, maximized, vSync, antialiasMode, adapterIndex, monitorIndex, caption);

    public override GraphicsManager CreateGraphicsManager(Window window) => new NullGraphicsManager(window);
    public override InputManager CreateInputManager(Window window) => new NullInputManager();
    public override AudioManager CreateAudioManager() => new NullAudioManager();

    public override void ShowError(string message, string details, AbsolutePath? logFile = null) =>
        Console.Error.WriteLine($"{message}\n{details}");

    public override SystemInfo GetSystemInfo() =>
        new(System.Runtime.InteropServices.RuntimeInformation.OSDescription, System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(), Environment.OSVersion.VersionString,
            "", Environment.ProcessorCount, Environment.ProcessorCount, 0, "null", 0, "", null);

    public override string? GetClipboardText() => null;
    public override void SetClipboardText(string text) { }
    public override TextureData? GetClipboardImage() => null;
    public override void SetClipboardImage(TextureData image) { }

    public override bool TryGetSpecialFolder(SpecialFolder specialFolder, [MaybeNullWhen(false)] out AbsolutePath path)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        path = new AbsolutePath(Path.Combine(home, specialFolder.ToString()));
        return true;
    }

    public override string GetDebugText() => nameof(HeadlessPlatform);
}
