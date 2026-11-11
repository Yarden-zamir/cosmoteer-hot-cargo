namespace HotCargo;

public static class Log
{
    static string? _path;
    static readonly Lock Gate = new();

    public static void Init(string modDir)
    {
        _path = Path.Combine(modDir, "hot_cargo_log.txt");
        File.WriteAllText(_path, $"Hot Cargo log {DateTime.Now:O}\n");
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : $"{message}\n{ex}");

    static void Write(string level, string message)
    {
        if (_path is null) return;
        lock (Gate) File.AppendAllText(_path, $"{DateTime.Now:HH:mm:ss.fff} {level} {message}\n");
    }
}
