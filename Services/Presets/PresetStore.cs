using System.IO;
using System.Text.Json;

namespace Spaces4Win.Services.Presets;

/// <summary>
/// Named session presets (slots 0–9) under %APPDATA%\Spaces4Win\presets.json.
/// Independent of cold-start <c>window-layout.json</c>.
/// </summary>
public sealed class PresetStore
{
    public const int SlotCount = 10;
    public const int MaxNameLength = 40;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly object _sync = new();

    public PresetStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Spaces4Win");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "presets.json");
    }

    public string FilePath => _path;

    /// <summary>UI label 1–10 for storage slot 0–9 (slot 0 → 10).</summary>
    public static int SlotToDisplayNumber(int slot) =>
        slot is >= 0 and <= 9 ? (slot == 0 ? 10 : slot) : slot;

    /// <summary>Hotkey/UI digit 0–9 or 1–10 → storage slot 0–9.</summary>
    public static int NormalizeSlot(int digitOrSlot)
    {
        if (digitOrSlot == 10)
        {
            return 0;
        }

        if (digitOrSlot is >= 0 and <= 9)
        {
            return digitOrSlot;
        }

        return Math.Clamp(digitOrSlot, 0, 9);
    }

    public static string DefaultName(int slot) =>
        $"Preset {SlotToDisplayNumber(NormalizeSlot(slot))}";

    public static string SanitizeName(string? name, int slot)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return DefaultName(slot);
        }

        if (trimmed.Length > MaxNameLength)
        {
            trimmed = trimmed[..MaxNameLength].TrimEnd();
        }

        return trimmed;
    }

    public PresetDocument LoadOrCreate()
    {
        lock (_sync)
        {
            var doc = TryLoadUnlocked() ?? new PresetDocument();
            EnsureSlots(doc);
            return doc;
        }
    }

    public PresetSlot? TryGetSlot(int slot)
    {
        slot = NormalizeSlot(slot);
        var doc = LoadOrCreate();
        return doc.Slots.FirstOrDefault(s => s.Slot == slot);
    }

    public PresetSlot Save(int slot, WindowLayoutDocument layout, bool keepExistingName = true)
    {
        slot = NormalizeSlot(slot);
        lock (_sync)
        {
            var doc = TryLoadUnlocked() ?? new PresetDocument();
            EnsureSlots(doc);
            var entry = doc.Slots.First(s => s.Slot == slot);
            var name = keepExistingName && !string.IsNullOrWhiteSpace(entry.Name)
                ? SanitizeName(entry.Name, slot)
                : DefaultName(slot);

            entry.Name = name;
            entry.UpdatedAt = DateTimeOffset.UtcNow;
            entry.Layout = CloneLayout(layout);
            WriteUnlocked(doc);
            return CloneSlot(entry);
        }
    }

    public PresetSlot Rename(int slot, string name)
    {
        slot = NormalizeSlot(slot);
        lock (_sync)
        {
            var doc = TryLoadUnlocked() ?? new PresetDocument();
            EnsureSlots(doc);
            var entry = doc.Slots.First(s => s.Slot == slot);
            entry.Name = SanitizeName(name, slot);
            if (entry.HasContent)
            {
                entry.UpdatedAt = DateTimeOffset.UtcNow;
            }

            WriteUnlocked(doc);
            return CloneSlot(entry);
        }
    }

    public void Clear(int slot)
    {
        slot = NormalizeSlot(slot);
        lock (_sync)
        {
            var doc = TryLoadUnlocked() ?? new PresetDocument();
            EnsureSlots(doc);
            var entry = doc.Slots.First(s => s.Slot == slot);
            entry.Name = DefaultName(slot);
            entry.UpdatedAt = DateTimeOffset.MinValue;
            entry.Layout = new WindowLayoutDocument();
            WriteUnlocked(doc);
        }
    }

    private PresetDocument? TryLoadUnlocked()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<PresetDocument>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private void WriteUnlocked(PresetDocument document)
    {
        document.UpdatedAt = DateTimeOffset.UtcNow;
        EnsureSlots(document);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, json);
        File.Copy(temp, _path, overwrite: true);
        File.Delete(temp);
    }

    private static void EnsureSlots(PresetDocument doc)
    {
        doc.Slots ??= new List<PresetSlot>();
        for (var i = 0; i < SlotCount; i++)
        {
            var slot = i;
            if (doc.Slots.All(s => s.Slot != slot))
            {
                doc.Slots.Add(new PresetSlot
                {
                    Slot = slot,
                    Name = DefaultName(slot),
                    Layout = new WindowLayoutDocument()
                });
            }
        }

        foreach (var s in doc.Slots)
        {
            s.Slot = NormalizeSlot(s.Slot);
            if (string.IsNullOrWhiteSpace(s.Name))
            {
                s.Name = DefaultName(s.Slot);
            }

            s.Layout ??= new WindowLayoutDocument();
        }

        doc.Slots = doc.Slots
            .GroupBy(s => s.Slot)
            .Select(g => g.First())
            .OrderBy(s => SlotToDisplayNumber(s.Slot))
            .ToList();
    }

    private static WindowLayoutDocument CloneLayout(WindowLayoutDocument layout)
    {
        var json = JsonSerializer.Serialize(layout, JsonOptions);
        return JsonSerializer.Deserialize<WindowLayoutDocument>(json, JsonOptions)
               ?? new WindowLayoutDocument();
    }

    private static PresetSlot CloneSlot(PresetSlot slot) => new()
    {
        Slot = slot.Slot,
        Name = slot.Name,
        UpdatedAt = slot.UpdatedAt,
        Layout = CloneLayout(slot.Layout)
    };
}

public sealed class PresetDocument
{
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<PresetSlot> Slots { get; set; } = new();
}

public sealed class PresetSlot
{
    public int Slot { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public WindowLayoutDocument Layout { get; set; } = new();

    public bool HasContent => UpdatedAt > DateTimeOffset.MinValue;
}
