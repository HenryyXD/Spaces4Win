using System.IO;
using System.Text.Json;

namespace Spaces4Win.Services;

/// <summary>
/// Durable per-window workspace layout across Spaces4Win restarts.
/// Never launches apps — restore only remaps windows that are already open.
/// </summary>
public sealed class WindowLayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;

    public WindowLayoutStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Spaces4Win");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "window-layout.json");
    }

    public string FilePath => _path;

    public void Write(WindowLayoutDocument document)
    {
        document.UpdatedAt = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, json);
        File.Copy(temp, _path, overwrite: true);
        File.Delete(temp);
    }

    public WindowLayoutDocument? TryLoad()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<WindowLayoutDocument>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

public sealed class WindowLayoutDocument
{
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<LayoutMonitorEntry> Monitors { get; set; } = new();
}

public sealed class LayoutMonitorEntry
{
    public string MonitorId { get; set; } = string.Empty;
    public int ActiveWorkspace { get; set; } = 1;
    public int LastActiveWorkspace { get; set; } = 1;
    public List<LayoutWindowEntry> Windows { get; set; } = new();
}

public sealed class LayoutWindowEntry
{
    public long Hwnd { get; set; }
    public uint ProcessId { get; set; }
    public string ProcessPath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public int Workspace { get; set; } = 1;
    /// <summary>
    /// True only when the user had the window minimized at capture time
    /// (before shutdown reveal). Default false so older layouts restore normally.
    /// </summary>
    public bool IsMinimized { get; set; }
    /// <summary>
    /// True when Spaces4Win had the window in Caps+F borderless fullscreen at capture time.
    /// </summary>
    public bool IsFullscreen { get; set; }
}

/// <summary>Live window fingerprint used for matching against a saved layout.</summary>
public sealed class WindowIdentity
{
    public IntPtr Hwnd { get; init; }
    public uint ProcessId { get; init; }
    public string ProcessPath { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
}
