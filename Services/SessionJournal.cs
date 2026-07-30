using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spaces4Win.Services;

public sealed class SessionJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public SessionJournal()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Spaces4Win");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "session-journal.json");
    }

    public string FilePath => _path;

    public void Write(SessionJournalDocument document)
    {
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, json);
        File.Copy(temp, _path, overwrite: true);
        File.Delete(temp);
    }

    public SessionJournalDocument? TryLoad()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<SessionJournalDocument>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch
        {
            // best effort
        }
    }
}

public sealed class SessionJournalDocument
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool CleanShutdown { get; set; }
    public List<JournalWindowEntry> Windows { get; set; } = new();
}

public sealed class JournalWindowEntry
{
    public long Hwnd { get; set; }
    public uint ProcessId { get; set; }
    public string MonitorId { get; set; } = string.Empty;
    public int Workspace { get; set; }
    public bool HiddenBySpaces4Win { get; set; }
    public VisibilityOwnership Ownership { get; set; }
}
