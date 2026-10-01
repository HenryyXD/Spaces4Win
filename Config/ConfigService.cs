using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spaces4Win.Config;

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _configPath;

    public ConfigService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Spaces4Win");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "config.json");
    }

    public string ConfigPath => _configPath;

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                var defaults = AppConfig.CreateDefault();
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            if (config is null)
            {
                return AppConfig.CreateDefault();
            }

            var followMissing = config.MoveWindowAndFollowHotkeys is null ||
                                config.MoveWindowAndFollowHotkeys.Count == 0;

            EnsureHotkeyLists(config);
            MigrateMonitorWorkspaceIds(config);
            MigrateMotionPreference(config);
            var overviewMigrated = MigrateLegacyOverviewHotkey(config);

            // Migrate away from Alt+N / Alt+Shift+N (unreliable / conflicts with Chrome etc.).
            if (AppConfig.IsLegacyAltHotkeyScheme(config))
            {
                config.SwitchWorkspaceHotkeys = AppConfig.CreateDefaultSwitchHotkeys();
                config.MoveWindowHotkeys = AppConfig.CreateDefaultMoveHotkeys();
                config.MoveWindowAndFollowHotkeys = AppConfig.CreateDefaultMoveAndFollowHotkeys();
                Save(config);
            }
            else if (overviewMigrated || followMissing)
            {
                Save(config);
            }

            return config;
        }
        catch
        {
            return AppConfig.CreateDefault();
        }
    }

    public void Save(AppConfig config)
    {
        EnsureHotkeyLists(config);
        MigrateMonitorWorkspaceIds(config);
        SyncReduceMotionFlag(config);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(_configPath, json);
    }

    private static void MigrateMotionPreference(AppConfig config)
    {
        // Older builds only had ReduceMotion=true → map to Disabled once.
        if (config.ReduceMotion && config.MotionPreference == Core.Motion.MotionPreference.FollowSystem)
        {
            config.MotionPreference = Core.Motion.MotionPreference.Disabled;
        }

        SyncReduceMotionFlag(config);
    }

    /// <summary>
    /// CapsLock+Space conflicted with typing; migrate the old default to CapsLock+`.
    /// </summary>
    private static bool MigrateLegacyOverviewHotkey(AppConfig config)
    {
        var hk = config.OverviewHotkey;
        if (hk is null)
        {
            return false;
        }

        if (hk.CapsLock &&
            hk.Modifiers == System.Windows.Input.ModifierKeys.None &&
            hk.Key == System.Windows.Input.Key.Space)
        {
            config.OverviewHotkey = AppConfig.CreateDefaultOverviewHotkey();
            return true;
        }

        return false;
    }

    private static void SyncReduceMotionFlag(AppConfig config)
    {
        config.ReduceMotion = config.MotionPreference == Core.Motion.MotionPreference.Disabled;
    }

    private static void MigrateMonitorWorkspaceIds(AppConfig config)
    {
        foreach (var cfg in config.Monitors.Values)
        {
            if (cfg.WorkspaceIds is { Count: > 0 })
            {
                cfg.WorkspaceIds = cfg.ResolveWorkspaceIds().ToList();
                continue;
            }

            cfg.WorkspaceIds = cfg.ResolveWorkspaceIds().ToList();
        }
    }

    private static void EnsureHotkeyLists(AppConfig config)
    {
        if (config.SwitchWorkspaceHotkeys is null || config.SwitchWorkspaceHotkeys.Count == 0)
        {
            config.SwitchWorkspaceHotkeys = AppConfig.CreateDefaultSwitchHotkeys();
        }

        if (config.MoveWindowHotkeys is null || config.MoveWindowHotkeys.Count == 0)
        {
            config.MoveWindowHotkeys = AppConfig.CreateDefaultMoveHotkeys();
        }

        if (config.MoveWindowAndFollowHotkeys is null || config.MoveWindowAndFollowHotkeys.Count == 0)
        {
            config.MoveWindowAndFollowHotkeys = AppConfig.CreateDefaultMoveAndFollowHotkeys();
        }

        config.Monitors ??= new Dictionary<string, MonitorConfig>(StringComparer.OrdinalIgnoreCase);
        config.ToggleLastWorkspaceHotkey ??= AppConfig.CreateDefaultToggleLastHotkey();
        config.WindowSwitcherHotkey ??= AppConfig.CreateDefaultWindowSwitcherHotkey();
        config.ToggleStickyHotkey ??= AppConfig.CreateDefaultToggleStickyHotkey();
        config.CompactWorkspacesHotkey ??= AppConfig.CreateDefaultCompactWorkspacesHotkey();
        config.FullscreenWorkspaceHotkey ??= AppConfig.CreateDefaultFullscreenWorkspaceHotkey();
        config.MoveToFreeWorkspaceHotkey ??= AppConfig.CreateDefaultMoveToFreeWorkspaceHotkey();
        config.MoveToFreeWorkspaceFollowHotkey ??= AppConfig.CreateDefaultMoveToFreeWorkspaceFollowHotkey();
        config.DeleteWorkspaceHotkey ??= AppConfig.CreateDefaultDeleteWorkspaceHotkey();
        config.OverviewHotkey ??= AppConfig.CreateDefaultOverviewHotkey();
        config.PreviousWorkspaceHotkey ??= AppConfig.CreateDefaultPreviousWorkspaceHotkey();
        config.NextWorkspaceHotkey ??= AppConfig.CreateDefaultNextWorkspaceHotkey();
        config.ShiftWindowLeftHotkey ??= AppConfig.CreateDefaultShiftWindowLeftHotkey();
        config.ShiftWindowRightHotkey ??= AppConfig.CreateDefaultShiftWindowRightHotkey();
        config.ShiftWindowFollowLeftHotkey ??= AppConfig.CreateDefaultShiftWindowFollowLeftHotkey();
        config.ShiftWindowFollowRightHotkey ??= AppConfig.CreateDefaultShiftWindowFollowRightHotkey();
        config.MoveAdjacentLeftHotkey ??= AppConfig.CreateDefaultMoveAdjacentLeftHotkey();
        config.MoveAdjacentRightHotkey ??= AppConfig.CreateDefaultMoveAdjacentRightHotkey();
        config.MoveAdjacentFollowLeftHotkey ??= AppConfig.CreateDefaultMoveAdjacentFollowLeftHotkey();
        config.MoveAdjacentFollowRightHotkey ??= AppConfig.CreateDefaultMoveAdjacentFollowRightHotkey();
        config.FocusPreviousMonitorHotkey ??= AppConfig.CreateDefaultFocusPreviousMonitorHotkey();
        config.FocusNextMonitorHotkey ??= AppConfig.CreateDefaultFocusNextMonitorHotkey();
        config.PresetBrowserHotkey ??= AppConfig.CreateDefaultPresetBrowserHotkey();

        if (config.SavePresetHotkeys is null || config.SavePresetHotkeys.Count == 0)
        {
            config.SavePresetHotkeys = AppConfig.CreateDefaultSavePresetHotkeys();
        }

        if (config.LoadPresetHotkeys is null || config.LoadPresetHotkeys.Count == 0)
        {
            config.LoadPresetHotkeys = AppConfig.CreateDefaultLoadPresetHotkeys();
        }

        if (config.IndicatorOpacity is < 0.3 or > 1.0)
        {
            config.IndicatorOpacity = 0.88;
        }

        if (config.IndicatorScale is < 0.75 or > 1.5)
        {
            config.IndicatorScale = 1.0;
        }

        config.Language ??= "System";
        config.Theme ??= "System";
        config.AccentColor ??= "System";
    }
}
