using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Spaces4Win.Config;
using Spaces4Win.Core;
using Spaces4Win.Localization;
using Spaces4Win.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;

namespace Spaces4Win.Settings.ViewModels;

public sealed partial class WorkspaceDotVm : ObservableObject
{
    public int Id { get; init; }
    public string NumberText => Id.ToString();
    public bool IsActive { get; init; }

    public System.Windows.Media.Brush Fill => IsActive
        ? new SolidColorBrush(MediaColor.FromRgb(0x4C, 0xC2, 0xFF))
        : new SolidColorBrush(MediaColor.FromArgb(0x22, 0xFF, 0xFF, 0xFF));

    public System.Windows.Media.Brush Stroke => IsActive
        ? MediaBrushes.Transparent
        : new SolidColorBrush(MediaColor.FromArgb(0x88, 0xFF, 0xFF, 0xFF));

    public System.Windows.Media.Brush TextBrush => IsActive
        ? new SolidColorBrush(MediaColor.FromRgb(0x10, 0x10, 0x14))
        : new SolidColorBrush(MediaColor.FromArgb(0xEE, 0xFF, 0xFF, 0xFF));
}

public sealed partial class MonitorCardVm : ObservableObject
{
    public required string DeviceName { get; init; }
    public required string DisplayName { get; init; }
    public required string ResolutionText { get; init; }
    public required string RoleText { get; init; }
    public required bool IsPrimary { get; init; }
    public required int ActiveWorkspace { get; init; }
    public required string WorkspaceCountText { get; init; }
    public required string ActiveLabel { get; init; }
    public required string WindowCountText { get; init; }
    public required string StickyCountText { get; init; }
    public required double CardWidth { get; init; }
    public required double CardHeight { get; init; }
    public required ObservableCollection<WorkspaceDotVm> Dots { get; init; }
    public required IReadOnlyList<int> WorkspaceIds { get; init; }
    public int? DeleteFallbackId { get; init; }
    public bool CanDelete => DeleteFallbackId is not null;
}

/// <summary>Shared draft of settings edits across pages.</summary>
public sealed partial class SettingsSession : ObservableObject
{
    private readonly AppServices _services;
    private readonly ILocalizationService _loc;
    private AppConfig _draft;
    private AppConfig _baseline;

    public SettingsSession(AppServices services, ILocalizationService localization)
    {
        _services = services;
        _loc = localization;
        _baseline = CloneConfig(services.Config);
        _draft = CloneConfig(services.Config);
        RefreshMonitors();
        if (_services.WorkspaceManager is not null)
        {
            _services.WorkspaceManager.StateChanged += (_, _) =>
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(RefreshMonitors);
        }
    }

    public ILocalizationService Loc => _loc;
    public AppServices Services => _services;
    public AppConfig Draft => _draft;

    public ObservableCollection<MonitorCardVm> Monitors { get; } = new();

    [ObservableProperty]
    private bool _hasPendingChanges;

    [ObservableProperty]
    private string? _statusMessage;

    public void MarkDirty() => EvaluateDirty();

    public void RefreshFromLive()
    {
        _baseline = CloneConfig(_services.Config);
        _draft = CloneConfig(_services.Config);
        RefreshMonitors();
        EvaluateDirty();
        OnPropertyChanged(nameof(Draft));
    }

    public void Discard()
    {
        _draft = CloneConfig(_baseline);
        OnPropertyChanged(nameof(Draft));
        EvaluateDirty();
        StatusMessage = null;
    }

    public bool Save()
    {
        try
        {
            _services.SaveAndApply(_draft);
            _loc.SetLanguagePreference(_draft.Language);
            ThemeHelper.ApplyTheme(_draft.Theme, _draft.ReduceMotion);
            _baseline = CloneConfig(_draft);
            EvaluateDirty();
            StatusMessage = _loc.Get("Common.Saved");
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = _loc.Format("Common.SaveFailed", ex.Message);
            return false;
        }
    }

    public void ApplyImmediateSafe(Action<AppConfig> mutate)
    {
        mutate(_draft);
        // Safe reversible prefs: language, theme, indicator visuals, start with windows
        _services.SaveAndApply(_draft);
        _baseline = CloneConfig(_draft);
        EvaluateDirty();
        OnPropertyChanged(nameof(Draft));
    }

    /// <summary>
    /// Updates indicator visuals immediately and persists after a short idle
    /// (for continuous sliders — avoids rewriting config on every pixel).
    /// </summary>
    public void ApplyIndicatorVisualLive(Action<AppConfig> mutate)
    {
        mutate(_draft);
        _services.ApplyIndicatorVisuals(_draft);
        OnPropertyChanged(nameof(Draft));
        ScheduleDebouncedPersist();
    }

    private System.Windows.Threading.DispatcherTimer? _persistTimer;

    private void ScheduleDebouncedPersist()
    {
        _persistTimer ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(280)
        };
        _persistTimer.Tick -= OnPersistTimerTick;
        _persistTimer.Tick += OnPersistTimerTick;
        _persistTimer.Stop();
        _persistTimer.Start();
    }

    private void OnPersistTimerTick(object? sender, EventArgs e)
    {
        if (_persistTimer is not null)
        {
            _persistTimer.Stop();
            _persistTimer.Tick -= OnPersistTimerTick;
        }

        _services.SaveAndApply(_draft);
        _baseline = CloneConfig(_draft);
        EvaluateDirty();
    }

    public void RefreshMonitors()
    {
        Monitors.Clear();
        var wm = _services.WorkspaceManager;
        if (wm is null || wm.Monitors.Count == 0)
        {
            return;
        }

        var maxWidth = wm.Monitors.Max(m => (double)Math.Max(m.Bounds.Width, 1));
        foreach (var monitor in wm.Monitors)
        {
            var info = _services.MonitorTracker.FindByDeviceName(monitor.MonitorId);
            var scale = monitor.Bounds.Width / maxWidth;
            var cardWidth = Math.Clamp(160 + 160 * scale, 180, 320);
            var aspect = monitor.Bounds.Height / (double)Math.Max(monitor.Bounds.Width, 1);
            var cardHeight = Math.Clamp(cardWidth * aspect * 0.45, 64, 130);
            var windowCount = monitor.Workspaces.Values.Sum(s => s.Count);
            var sticky = monitor.StickyWindows.Count;
            var fallback = MonitorWorkspace.ResolveDeletionFallback(monitor.WorkspaceIds, monitor.ActiveWorkspace);

            Monitors.Add(new MonitorCardVm
            {
                DeviceName = monitor.MonitorId,
                DisplayName = ShortDevice(monitor.MonitorId),
                ResolutionText = _loc.Format("Settings.Overview.Resolution", monitor.Bounds.Width, monitor.Bounds.Height),
                RoleText = _loc.Get(info?.IsPrimary == true ? "Common.Primary" : "Common.Secondary"),
                IsPrimary = info?.IsPrimary == true,
                ActiveWorkspace = monitor.ActiveWorkspace,
                WorkspaceCountText = _loc.Plural(
                    "Settings.Overview.WorkspaceCount.One",
                    "Settings.Overview.WorkspaceCount.Many",
                    monitor.WorkspaceCount),
                ActiveLabel = _loc.Format("Settings.Overview.ActiveLabel", monitor.ActiveWorkspace),
                WindowCountText = _loc.Plural("Workspace.WindowCount.One", "Workspace.WindowCount.Many", windowCount),
                StickyCountText = _loc.Plural("Workspace.StickyCount.One", "Workspace.StickyCount.Many", sticky),
                CardWidth = cardWidth,
                CardHeight = cardHeight,
                WorkspaceIds = monitor.WorkspaceIds,
                DeleteFallbackId = fallback,
                Dots = new ObservableCollection<WorkspaceDotVm>(
                    monitor.WorkspaceIds.Select(id => new WorkspaceDotVm
                    {
                        Id = id,
                        IsActive = id == monitor.ActiveWorkspace
                    }))
            });
        }
    }

    private void EvaluateDirty()
    {
        HasPendingChanges = !ConfigsEqual(_baseline, _draft);
    }

    private static string ShortDevice(string deviceName)
        => deviceName.Replace(@"\\.\", string.Empty);

    private static AppConfig CloneConfig(AppConfig source)
    {
        // JSON round-trip keeps the clone simple and complete.
        var json = System.Text.Json.JsonSerializer.Serialize(source);
        return System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json) ?? AppConfig.CreateDefault();
    }

    private static bool ConfigsEqual(AppConfig a, AppConfig b)
    {
        var ja = System.Text.Json.JsonSerializer.Serialize(a);
        var jb = System.Text.Json.JsonSerializer.Serialize(b);
        return string.Equals(ja, jb, StringComparison.Ordinal);
    }
}

public static class ThemeHelper
{
    public static void ApplyTheme(string theme, bool reduceMotion)
    {
        var preference = theme?.ToLowerInvariant() switch
        {
            "light" => ApplicationTheme.Light,
            "dark" => ApplicationTheme.Dark,
            _ => GetSystemTheme()
        };

        // None: ApplicationThemeManager.Apply also updates MainWindow backdrop.
        // Mica on AllowsTransparency overlays (indicator/badge) kills layered chrome.
        // Settings FluentWindow keeps its own WindowBackdropType=Mica in XAML.
        ApplicationThemeManager.Apply(preference, WindowBackdropType.None, updateAccent: true);
        _ = reduceMotion;
        RestoreOverlayWindows();
    }

    /// <summary>
    /// WPF-UI theme/backdrop sweeps can leave floating overlays opaque and unresponsive.
    /// </summary>
    public static void RestoreOverlayWindows()
    {
        try
        {
            App.Services.IndicatorService?.RepairLayeredWindows();
        }
        catch (InvalidOperationException)
        {
            // Services not ready during very early startup.
        }
    }

    private static ApplicationTheme GetSystemTheme()
    {
        try
        {
            return ApplicationThemeManager.GetSystemTheme() switch
            {
                SystemTheme.Light => ApplicationTheme.Light,
                _ => ApplicationTheme.Dark
            };
        }
        catch
        {
            return ApplicationTheme.Dark;
        }
    }
}
