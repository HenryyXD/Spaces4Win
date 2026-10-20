using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Threading;

namespace Spaces4Win.Services;

/// <summary>
/// Append-only diagnostic log under %APPDATA%\Spaces4Win\logs.
/// Records crashes, swallowed failures, and UI-thread stalls.
/// </summary>
public static class DiagnosticLog
{
    private const int BreadcrumbLimit = 40;
    private static readonly object Gate = new();
    private static readonly List<string> Breadcrumbs = new();
    private static DiagnosticLogWriter? _writer;
    private static UiHangWatchdog? _watchdog;
    private static string? _markerPath;
    private static bool _sessionOpen;

    public static string LogDirectory { get; private set; } = DefaultDirectory();

    public static string LogFilePath => Path.Combine(LogDirectory, "spaces4win.log");

    public static void Initialize()
    {
        try
        {
            lock (Gate)
            {
                if (_writer is not null)
                {
                    return;
                }

                LogDirectory = DefaultDirectory();
                Directory.CreateDirectory(LogDirectory);
                _markerPath = Path.Combine(LogDirectory, "session.pid");
                _writer = new DiagnosticLogWriter(LogFilePath);
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    public static void BeginSession()
    {
        Initialize();
        string? previous = null;
        try
        {
            lock (Gate)
            {
                if (_sessionOpen || _markerPath is null)
                {
                    return;
                }

                previous = DiagnosticLogWriter.ReadMarker(_markerPath);
                DiagnosticLogWriter.WriteMarker(
                    _markerPath,
                    $"pid={Environment.ProcessId} started={DateTimeOffset.Now:o} exe={Environment.ProcessPath}");
                _sessionOpen = true;
            }
        }
        catch
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(previous))
        {
            Warn("Previous session ended without a clean shutdown. Marker: " + previous.Trim());
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
        var elevated = false;
        try
        {
            elevated = ElevationService.IsCurrentProcessElevated();
        }
        catch
        {
            // ignore
        }

        Info($"Session started pid={Environment.ProcessId} version={version} elevated={elevated} exe={Environment.ProcessPath}");
    }

    public static void EndSession()
    {
        try
        {
            StopUiWatchdog();
            if (_sessionOpen)
            {
                Info("Session ended (clean shutdown)");
            }

            lock (Gate)
            {
                if (_markerPath is not null)
                {
                    DiagnosticLogWriter.ClearMarker(_markerPath);
                }

                _sessionOpen = false;
            }
        }
        catch
        {
            // ignore
        }
    }

    public static void StartUiWatchdog(Dispatcher dispatcher)
    {
        try
        {
            Initialize();
            lock (Gate)
            {
                if (_watchdog is not null)
                {
                    return;
                }

                _watchdog = new UiHangWatchdog(dispatcher, ReportUiHang, ReportUiRecovered);
                _watchdog.Start();
            }
        }
        catch
        {
            // ignore
        }
    }

    public static void StopUiWatchdog()
    {
        UiHangWatchdog? watchdog;
        lock (Gate)
        {
            watchdog = _watchdog;
            _watchdog = null;
        }

        watchdog?.Dispose();
    }

    public static void Breadcrumb(string message)
    {
        var line = $"{DateTimeOffset.Now:HH:mm:ss.fff} {message}";
        lock (Gate)
        {
            Breadcrumbs.Add(line);
            if (Breadcrumbs.Count > BreadcrumbLimit)
            {
                Breadcrumbs.RemoveAt(0);
            }
        }

        Write("TRC", message, exception: null, includeRecent: false);
    }

    public static void Info(string message) => Write("INF", message, null, false);

    public static void Warn(string message) => Write("WRN", message, null, true);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERR", message, exception, true);

    public static void Critical(string message, Exception? exception = null) =>
        Write("CRT", message, exception, true);

    private static void ReportUiHang(long stalledMs)
    {
        var threads = 0;
        long workingSet = 0;
        try
        {
            using var process = Process.GetCurrentProcess();
            threads = process.Threads.Count;
            workingSet = process.WorkingSet64;
        }
        catch
        {
            // ignore
        }

        Warn($"UI thread unresponsive for {stalledMs} ms. threads={threads} workingSet={workingSet}");
    }

    private static void ReportUiRecovered(long stalledMs) =>
        Info($"UI thread is responding again after {stalledMs} ms.");

    private static void Write(string level, string message, Exception? exception, bool includeRecent)
    {
        try
        {
            List<string>? recent = null;
            if (includeRecent)
            {
                lock (Gate)
                {
                    if (Breadcrumbs.Count > 0)
                    {
                        recent = Breadcrumbs.ToList();
                    }
                }
            }

            var text = DiagnosticLogWriter.Format(level, message, exception, recent);
            lock (Gate)
            {
                _writer?.Append(text);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string DefaultDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Spaces4Win",
            "logs");
}

internal sealed class DiagnosticLogWriter
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _archives;
    private readonly object _gate = new();

    public DiagnosticLogWriter(string path, long maxBytes = 1_048_576, int archives = 3)
    {
        _path = path;
        _maxBytes = maxBytes;
        _archives = archives;
    }

    public string Path => _path;

    public void Append(string text)
    {
        lock (_gate)
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            RotateIfNeeded(Encoding.UTF8.GetByteCount(text) + Environment.NewLine.Length);
            File.AppendAllText(_path, text + Environment.NewLine);
        }
    }

    public static string Format(string level, string message, Exception? exception, IReadOnlyList<string>? recent)
    {
        var builder = new StringBuilder();
        builder.Append(DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"));
        builder.Append(' ');
        builder.Append(level);
        builder.Append(" [");
        builder.Append(Environment.CurrentManagedThreadId);
        builder.Append("] ");
        builder.Append(message);
        if (exception is not null)
        {
            builder.AppendLine();
            builder.Append(exception);
        }

        if (recent is { Count: > 0 })
        {
            builder.AppendLine();
            builder.Append("recent:");
            foreach (var line in recent)
            {
                builder.AppendLine();
                builder.Append("  ");
                builder.Append(line);
            }
        }

        return builder.ToString();
    }

    public static string? ReadMarker(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }

    public static void WriteMarker(string path, string content)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content);
    }

    public static void ClearMarker(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best effort
        }
    }

    private void RotateIfNeeded(int incomingBytes)
    {
        if (!File.Exists(_path))
        {
            return;
        }

        if (new FileInfo(_path).Length + incomingBytes < _maxBytes)
        {
            return;
        }

        var oldest = ArchivePath(_archives);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var i = _archives - 1; i >= 1; i--)
        {
            var source = ArchivePath(i);
            if (!File.Exists(source))
            {
                continue;
            }

            File.Move(source, ArchivePath(i + 1), overwrite: true);
        }

        File.Move(_path, ArchivePath(1), overwrite: true);
    }

    private string ArchivePath(int index) => _path + "." + index;
}

internal sealed class UiHangWatchdog : IDisposable
{
    private const int PollMs = 2000;
    private const int HangMs = 8000;
    private const int RepeatMs = 30000;

    private readonly Dispatcher _dispatcher;
    private readonly Action<long> _onHang;
    private readonly Action<long> _onRecovered;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _stop = new(false);
    private long _lastUiTick = Environment.TickCount64;
    private int _pingPending;
    private int _hanging;
    private long _lastReportTick;

    public UiHangWatchdog(Dispatcher dispatcher, Action<long> onHang, Action<long> onRecovered)
    {
        _dispatcher = dispatcher;
        _onHang = onHang;
        _onRecovered = onRecovered;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "Spaces4Win.UiWatchdog"
        };
    }

    public void Start() => _thread.Start();

    public void Dispose()
    {
        _stop.Set();
        try
        {
            _thread.Join(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // ignore
        }

        _stop.Dispose();
    }

    private void Loop()
    {
        while (!_stop.Wait(PollMs))
        {
            try
            {
                Poll();
            }
            catch
            {
                // keep watching
            }
        }
    }

    private void Poll()
    {
        if (Interlocked.CompareExchange(ref _pingPending, 1, 0) == 0)
        {
            try
            {
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    var stalled = Environment.TickCount64 - Interlocked.Read(ref _lastUiTick);
                    Interlocked.Exchange(ref _lastUiTick, Environment.TickCount64);
                    Interlocked.Exchange(ref _pingPending, 0);
                    if (Interlocked.Exchange(ref _hanging, 0) == 1)
                    {
                        _onRecovered(stalled);
                    }
                }), DispatcherPriority.Normal);
            }
            catch (InvalidOperationException)
            {
                _stop.Set();
                Interlocked.Exchange(ref _pingPending, 0);
                return;
            }
        }

        var now = Environment.TickCount64;
        var quietFor = now - Interlocked.Read(ref _lastUiTick);
        if (Volatile.Read(ref _pingPending) != 1 || quietFor < HangMs)
        {
            return;
        }

        var sinceReport = now - Interlocked.Read(ref _lastReportTick);
        if (Volatile.Read(ref _hanging) == 1 && sinceReport < RepeatMs)
        {
            return;
        }

        Interlocked.Exchange(ref _hanging, 1);
        Interlocked.Exchange(ref _lastReportTick, now);
        _onHang(quietFor);
    }
}
