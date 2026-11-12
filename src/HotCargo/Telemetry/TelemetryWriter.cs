using System.Diagnostics;
using System.Text;

namespace HotCargo.Telemetry;

/// <summary>
/// Writes the events of one session to JSONL files on a background thread. Game threads only add lines to a list under a short lock,
/// so a slow disk never blocks a frame or a sim tick. The thread writes and flushes once per second, and at once on Close.
/// A file that reaches MaxFileMegabytes continues in "&lt;name&gt;.partN.jsonl"; each part starts with a "file.part" line.
/// Limit: a hard kill of the game loses the lines of the last second. Revisit if crash analysis needs them.
/// </summary>
sealed class TelemetryWriter
{
    /// <summary>Lines that wait for the writer. Above this, new lines are dropped and counted (Stats.Dropped).</summary>
    const int MaxPendingLines = 200_000;

    static volatile TelemetryWriter? _current;

    readonly Lock _gate = new();
    readonly string _folder, _name, _partHeader;
    readonly long _maxFileBytes;
    readonly Thread _thread;
    readonly AutoResetEvent _wake = new(false);
    List<byte[]> _pending = [], _spare = [];
    FileStream? _file;
    long _fileBytes;
    int _part = 1;
    volatile bool _stopping;

    public string FilePath { get; private set; }

    TelemetryWriter(string folder, string name, long maxFileBytes, string partHeader)
    {
        _folder = folder;
        _name = name;
        _maxFileBytes = maxFileBytes;
        _partHeader = partHeader;
        FilePath = Path.Combine(folder, name + ".jsonl");
        _file = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
        _thread = new Thread(Loop) { IsBackground = true, Name = "HotCargo telemetry", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    /// <summary>Closes the current writer and opens a new file. partHeader is a JSON object line that starts each later part.</summary>
    public static TelemetryWriter Open(string folder, string name, int maxFileMegabytes, int maxFolderMegabytes, string partHeader)
    {
        Close();
        Directory.CreateDirectory(folder);
        TrimFolder(folder, (long)maxFolderMegabytes << 20);
        var w = new TelemetryWriter(folder, name, (long)maxFileMegabytes << 20, partHeader);
        _current = w;
        return w;
    }

    /// <summary>Writes every queued line and closes the file. Waits at most 2 seconds for the writer thread.</summary>
    public static void Close()
    {
        if (_current is not { } w) return;
        _current = null;
        w._stopping = true;
        w._wake.Set();
        if (!w._thread.Join(2000)) Log.Error("telemetry writer did not stop within 2 s; the last lines can be missing");
    }

    public static void Enqueue(byte[] line)
    {
        if (_current is not { } w) return;
        lock (w._gate)
        {
            if (w._pending.Count >= MaxPendingLines) { Interlocked.Increment(ref Stats.Dropped); return; }
            w._pending.Add(line);
        }
        Interlocked.Increment(ref Stats.Events);
    }

    public static int PendingCount
    {
        get
        {
            if (_current is not { } w) return 0;
            lock (w._gate) return w._pending.Count;
        }
    }

    void Loop()
    {
        try
        {
            while (!_stopping)
            {
                _wake.WaitOne(1000);
                Drain();
            }
            Drain();
        }
        catch (Exception ex) { Log.Error("telemetry writer failed; telemetry stops for this session", ex); }
        finally
        {
            _file?.Dispose();
            _file = null;
        }
    }

    void Drain()
    {
        long start = Stopwatch.GetTimestamp();
        List<byte[]> batch;
        lock (_gate)
        {
            batch = _pending;
            _pending = _spare;
            _spare = batch;
        }
        if (batch.Count == 0 || _file is null) { batch.Clear(); return; }
        foreach (var line in batch)
        {
            if (_fileBytes + line.Length + 1 > _maxFileBytes && _fileBytes > 0) NextPart();
            _file.Write(line);
            _file.WriteByte((byte)'\n');
            _fileBytes += line.Length + 1;
            Interlocked.Add(ref Stats.Bytes, line.Length + 1);
        }
        batch.Clear();
        _file.Flush();
        Interlocked.Add(ref Stats.WriterTicks, Stopwatch.GetTimestamp() - start);
    }

    void NextPart()
    {
        _file!.Dispose();
        _part++;
        FilePath = Path.Combine(_folder, $"{_name}.part{_part}.jsonl");
        _file = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
        byte[] header = Encoding.UTF8.GetBytes(_partHeader.Replace("\"part\":0", $"\"part\":{_part}"));
        _file.Write(header);
        _file.WriteByte((byte)'\n');
        _fileBytes = header.Length + 1;
    }

    /// <summary>Deletes the oldest .jsonl files until the folder holds less than maxBytes.</summary>
    static void TrimFolder(string folder, long maxBytes)
    {
        var files = new DirectoryInfo(folder).GetFiles("*.jsonl").OrderBy(f => f.LastWriteTimeUtc).ToList();
        long total = files.Sum(f => f.Length);
        foreach (var f in files)
        {
            if (total < maxBytes) break;
            total -= f.Length;
            f.Delete();
            Log.Info($"telemetry: deleted {f.Name} to keep the folder below {maxBytes >> 20} MB");
        }
    }
}
