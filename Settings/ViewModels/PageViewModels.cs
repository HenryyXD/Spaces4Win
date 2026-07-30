using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spaces4Win.Config;
using Spaces4Win.Localization;
using Spaces4Win.Services;

namespace Spaces4Win.Settings.ViewModels;

public abstract class PageViewModelBase : ObservableObject
{
    protected PageViewModelBase(SettingsSession session, ILocalizationService loc)
    {
        Session = session;
        Loc = loc;
        loc.CultureChanged += (_, _) => OnCultureChanged();
    }

    public SettingsSession Session { get; }
    public ILocalizationService Loc { get; }

    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    protected void SetHeader(string titleKey, string descriptionKey)
    {
        Title = Loc.Get(titleKey);
        Description = Loc.Get(descriptionKey);
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
    }

    protected virtual void OnCultureChanged()
    {
        Session.RefreshMonitors();
        OnPropertyChanged(nameof(Loc));
    }
}

public sealed partial class OverviewPageViewModel : PageViewModelBase
{
    public OverviewPageViewModel(SettingsSession session, ILocalizationService loc) : base(session, loc)
    {
        SetHeader("Settings.Overview.Title", "Settings.Overview.Description");
        LanguageOptions = loc.AvailableLanguages
            .Select(o => new LanguageChoice(o.Id, loc.Get(o.DisplayKey)))
            .ToList();
        SelectedLanguage = session.Draft.Language;
    }

    public IReadOnlyList<LanguageChoice> LanguageOptions { get; private set; }

    [ObservableProperty]
    private string _selectedLanguage = "System";

    partial void OnSelectedLanguageChanged(string value)
    {
        Session.ApplyImmediateSafe(c => c.Language = value);
        Loc.SetLanguagePreference(value);
        LanguageOptions = Loc.AvailableLanguages
            .Select(o => new LanguageChoice(o.Id, Loc.Get(o.DisplayKey)))
            .ToList();
        OnPropertyChanged(nameof(LanguageOptions));
    }

    public bool ShowIndicators
    {
        get => Session.Draft.ShowFloatingIndicators;
        set
        {
            if (Session.Draft.ShowFloatingIndicators == value) return;
            Session.ApplyImmediateSafe(c => c.ShowFloatingIndicators = value);
            OnPropertyChanged();
        }
    }

    public bool StartWithWindows
    {
        get => Session.Draft.StartWithWindows;
        set
        {
            if (Session.Draft.StartWithWindows == value) return;
            Session.ApplyImmediateSafe(c => c.StartWithWindows = value);
            OnPropertyChanged();
        }
    }

    protected override void OnCultureChanged()
    {
        base.OnCultureChanged();
        SetHeader("Settings.Overview.Title", "Settings.Overview.Description");
        LanguageOptions = Loc.AvailableLanguages
            .Select(o => new LanguageChoice(o.Id, Loc.Get(o.DisplayKey)))
            .ToList();
        OnPropertyChanged(nameof(LanguageOptions));
        OnPropertyChanged(nameof(ShowIndicators));
        OnPropertyChanged(nameof(StartWithWindows));
    }
}

public sealed record LanguageChoice(string Id, string Display);

public sealed partial class WorkspacesPageViewModel : PageViewModelBase
{
    public WorkspacesPageViewModel(SettingsSession session, ILocalizationService loc) : base(session, loc)
    {
        SetHeader("Settings.Workspaces.Title", "Settings.Workspaces.Description");
    }

    [RelayCommand]
    private void DeleteActive(string? deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return;
        var ok = Session.Services.WorkspaceManager?.DeleteCurrentWorkspace(deviceName) == true;
        Session.StatusMessage = Loc.Get(ok ? "Status.WorkspaceDeleted" : "Status.CannotDeleteOnly");
        Session.RefreshMonitors();
    }

    protected override void OnCultureChanged()
    {
        base.OnCultureChanged();
        SetHeader("Settings.Workspaces.Title", "Settings.Workspaces.Description");
    }
}

public sealed partial class HotkeyCommandVm : ObservableObject
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<string> Keycaps { get; init; }
    public bool HasConflict { get; init; }
}

public sealed partial class HotkeyCategoryVm : ObservableObject
{
    public required string Title { get; init; }
    public required ObservableCollection<HotkeyCommandVm> Commands { get; init; }
}

public sealed partial class HotkeysPageViewModel : PageViewModelBase
{
    public HotkeysPageViewModel(SettingsSession session, ILocalizationService loc) : base(session, loc)
    {
        SetHeader("Settings.Hotkeys.Title", "Settings.Hotkeys.Description");
        Rebuild();
    }

    public ObservableCollection<HotkeyCategoryVm> Categories { get; } = new();

    private void Rebuild()
    {
        Categories.Clear();
        var cfg = Session.Draft;

        var nav = new ObservableCollection<HotkeyCommandVm>();
        foreach (var hk in cfg.SwitchWorkspaceHotkeys.OrderBy(h => h.Workspace))
        {
            nav.Add(new HotkeyCommandVm
            {
                Name = Loc.Format("Settings.Hotkeys.Switch.Name", hk.Workspace),
                Description = Loc.Format("Settings.Hotkeys.Switch.Desc", hk.Workspace),
                Keycaps = ToKeycaps(hk)
            });
        }

        nav.Add(new HotkeyCommandVm
        {
            Name = Loc.Get("Settings.Hotkeys.Last.Name"),
            Description = Loc.Get("Settings.Hotkeys.Last.Desc"),
            Keycaps = ToKeycaps(cfg.ToggleLastWorkspaceHotkey)
        });
        nav.Add(new HotkeyCommandVm
        {
            Name = Loc.Get("Settings.Hotkeys.WindowSwitcher.Name"),
            Description = Loc.Get("Settings.Hotkeys.WindowSwitcher.Desc"),
            Keycaps = ToKeycaps(cfg.WindowSwitcherHotkey)
        });
        nav.Add(new HotkeyCommandVm
        {
            Name = Loc.Get("Settings.Hotkeys.Previous.Name"),
            Description = Loc.Get("Settings.Hotkeys.Previous.Desc"),
            Keycaps = ToKeycaps(cfg.PreviousWorkspaceHotkey)
        });
        nav.Add(new HotkeyCommandVm
        {
            Name = Loc.Get("Settings.Hotkeys.Next.Name"),
            Description = Loc.Get("Settings.Hotkeys.Next.Desc"),
            Keycaps = ToKeycaps(cfg.NextWorkspaceHotkey)
        });
        nav.Add(new HotkeyCommandVm
        {
            Name = Loc.Get("Settings.Hotkeys.FocusPrevMonitor.Name"),
            Description = Loc.Get("Settings.Hotkeys.FocusPrevMonitor.Desc"),
            Keycaps = ToKeycaps(cfg.FocusPreviousMonitorHotkey)
        });
        nav.Add(new HotkeyCommandVm
        {
            Name = Loc.Get("Settings.Hotkeys.FocusNextMonitor.Name"),
            Description = Loc.Get("Settings.Hotkeys.FocusNextMonitor.Desc"),
            Keycaps = ToKeycaps(cfg.FocusNextMonitorHotkey)
        });

        Categories.Add(new HotkeyCategoryVm
        {
            Title = Loc.Get("Settings.Hotkeys.Category.Navigation"),
            Commands = nav
        });

        var move = new ObservableCollection<HotkeyCommandVm>(
            cfg.MoveWindowHotkeys.OrderBy(h => h.Workspace).Select(hk => new HotkeyCommandVm
            {
                Name = Loc.Format("Settings.Hotkeys.Move.Name", hk.Workspace),
                Description = Loc.Format("Settings.Hotkeys.Move.Desc", hk.Workspace),
                Keycaps = ToKeycaps(hk)
            }));
        foreach (var hk in cfg.MoveWindowAndFollowHotkeys.OrderBy(h => h.Workspace))
        {
            move.Add(new HotkeyCommandVm
            {
                Name = Loc.Format("Settings.Hotkeys.MoveFollow.Name", hk.Workspace),
                Description = Loc.Format("Settings.Hotkeys.MoveFollow.Desc", hk.Workspace),
                Keycaps = ToKeycaps(hk)
            });
        }

        move.Add(new HotkeyCommandVm
        {
            Name = Loc.Get("Settings.Hotkeys.ShiftLeft.Name"),
            Description = Loc.Get("Settings.Hotkeys.ShiftLeft.Desc"),
            Keycaps = ToKeycaps(cfg.ShiftWindowLeftHotkey)
        });
        move.Add(new HotkeyCommandVm
        {
            Name = Loc.Get("Settings.Hotkeys.ShiftRight.Name"),
            Description = Loc.Get("Settings.Hotkeys.ShiftRight.Desc"),
            Keycaps = ToKeycaps(cfg.ShiftWindowRightHotkey)
        });
        move.Add(new HotkeyCommandVm
        {
            Name = Loc.Get("Settings.Hotkeys.ShiftFollowLeft.Name"),
            Description = Loc.Get("Settings.Hotkeys.ShiftFollowLeft.Desc"),
            Keycaps = ToKeycaps(cfg.ShiftWindowFollowLeftHotkey)
        });
        move.Add(new HotkeyCommandVm
        {
            Name = Loc.Get("Settings.Hotkeys.ShiftFollowRight.Name"),
            Description = Loc.Get("Settings.Hotkeys.ShiftFollowRight.Desc"),
            Keycaps = ToKeycaps(cfg.ShiftWindowFollowRightHotkey)
        });
        move.Add(new HotkeyCommandVm
        {
            Name = Loc.Get("Settings.Hotkeys.MoveAdjacentLeft.Name"),
            Description = Loc.Get("Settings.Hotkeys.MoveAdjacentLeft.Desc"),
            Keycaps = ToKeycaps(cfg.MoveAdjacentLeftHotkey)
        });
        move.Add(new HotkeyCommandVm
        {
            Name = Loc.Get("Settings.Hotkeys.MoveAdjacentRight.Name"),
            Description = Loc.Get("Settings.Hotkeys.MoveAdjacentRight.Desc"),
            Keycaps = ToKeycaps(cfg.MoveAdjacentRightHotkey)
        });
        move.Add(new HotkeyCommandVm
        {
            Name = Loc.Get("Settings.Hotkeys.MoveAdjacentFollowLeft.Name"),
            Description = Loc.Get("Settings.Hotkeys.MoveAdjacentFollowLeft.Desc"),
            Keycaps = ToKeycaps(cfg.MoveAdjacentFollowLeftHotkey)
        });
        move.Add(new HotkeyCommandVm
        {
            Name = Loc.Get("Settings.Hotkeys.MoveAdjacentFollowRight.Name"),
            Description = Loc.Get("Settings.Hotkeys.MoveAdjacentFollowRight.Desc"),
            Keycaps = ToKeycaps(cfg.MoveAdjacentFollowRightHotkey)
        });

        Categories.Add(new HotkeyCategoryVm
        {
            Title = Loc.Get("Settings.Hotkeys.Category.Move"),
            Commands = move
        });

        Categories.Add(new HotkeyCategoryVm
        {
            Title = Loc.Get("Settings.Hotkeys.Category.Sticky"),
            Commands = new ObservableCollection<HotkeyCommandVm>
            {
                new()
                {
                    Name = Loc.Get("Settings.Hotkeys.Sticky.Name"),
                    Description = Loc.Get("Settings.Hotkeys.Sticky.Desc"),
                    Keycaps = ToKeycaps(cfg.ToggleStickyHotkey)
                }
            }
        });

        Categories.Add(new HotkeyCategoryVm
        {
            Title = Loc.Get("Settings.Hotkeys.Category.Workspace"),
            Commands = new ObservableCollection<HotkeyCommandVm>
            {
                new()
                {
                    Name = Loc.Get("Settings.Hotkeys.Compact.Name"),
                    Description = Loc.Get("Settings.Hotkeys.Compact.Desc"),
                    Keycaps = ToKeycaps(cfg.CompactWorkspacesHotkey)
                },
                new()
                {
                    Name = Loc.Get("Settings.Hotkeys.Fullscreen.Name"),
                    Description = Loc.Get("Settings.Hotkeys.Fullscreen.Desc"),
                    Keycaps = ToKeycaps(cfg.FullscreenWorkspaceHotkey)
                },
                new()
                {
                    Name = Loc.Get("Settings.Hotkeys.MoveFree.Name"),
                    Description = Loc.Get("Settings.Hotkeys.MoveFree.Desc"),
                    Keycaps = ToKeycaps(cfg.MoveToFreeWorkspaceHotkey)
                },
                new()
                {
                    Name = Loc.Get("Settings.Hotkeys.MoveFreeFollow.Name"),
                    Description = Loc.Get("Settings.Hotkeys.MoveFreeFollow.Desc"),
                    Keycaps = ToKeycaps(cfg.MoveToFreeWorkspaceFollowHotkey)
                },
                new()
                {
                    Name = Loc.Get("Settings.Hotkeys.Delete.Name"),
                    Description = Loc.Get("Settings.Hotkeys.Delete.Desc"),
                    Keycaps = ToKeycaps(cfg.DeleteWorkspaceHotkey)
                }
            }
        });

        Categories.Add(new HotkeyCategoryVm
        {
            Title = Loc.Get("Settings.Hotkeys.Category.Overview"),
            Commands = new ObservableCollection<HotkeyCommandVm>
            {
                new()
                {
                    Name = Loc.Get("Settings.Hotkeys.Overview.Name"),
                    Description = Loc.Get("Settings.Hotkeys.Overview.Desc"),
                    Keycaps = ToKeycaps(cfg.OverviewHotkey)
                }
            }
        });
    }

    private static IReadOnlyList<string> ToKeycaps(HotkeyBinding hk)
    {
        var parts = new List<string>();
        if (hk.CapsLock) parts.Add("Caps Lock");
        if (hk.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) parts.Add("Ctrl");
        if (hk.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)) parts.Add("Alt");
        if (hk.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) parts.Add("Shift");
        if (hk.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(FormatKey(hk.Key));
        return parts;
    }

    private static string FormatKey(System.Windows.Input.Key key) => key switch
    {
        System.Windows.Input.Key.D0 => "0",
        System.Windows.Input.Key.D1 => "1",
        System.Windows.Input.Key.D2 => "2",
        System.Windows.Input.Key.D3 => "3",
        System.Windows.Input.Key.D4 => "4",
        System.Windows.Input.Key.D5 => "5",
        System.Windows.Input.Key.D6 => "6",
        System.Windows.Input.Key.D7 => "7",
        System.Windows.Input.Key.D8 => "8",
        System.Windows.Input.Key.D9 => "9",
        System.Windows.Input.Key.Tab => "Tab",
        System.Windows.Input.Key.Space => "Space",
        System.Windows.Input.Key.Back => "Backspace",
        System.Windows.Input.Key.Left => "←",
        System.Windows.Input.Key.Right => "→",
        System.Windows.Input.Key.Oem3 or System.Windows.Input.Key.OemTilde => "`",
        System.Windows.Input.Key.OemOpenBrackets => "[",
        System.Windows.Input.Key.OemCloseBrackets => "]",
        _ => key.ToString()
    };

    protected override void OnCultureChanged()
    {
        base.OnCultureChanged();
        SetHeader("Settings.Hotkeys.Title", "Settings.Hotkeys.Description");
        Rebuild();
    }
}

public sealed partial class IndicatorPageViewModel : PageViewModelBase
{
    public IndicatorPageViewModel(SettingsSession session, ILocalizationService loc) : base(session, loc)
    {
        SetHeader("Settings.Indicators.Title", "Settings.Indicators.Description");
        PreviewDots = new ObservableCollection<WorkspaceDotVm>
        {
            new() { Id = 1, IsActive = false },
            new() { Id = 3, IsActive = true },
            new() { Id = 7, IsActive = false }
        };

        if (session.Services.IndicatorService is { } indicators)
        {
            indicators.MoveModeChanged += OnMoveModeChanged;
        }

        SelectedPinnedMode = session.Draft.PinnedWindowIndicatorMode.ToString();
    }

    public ObservableCollection<WorkspaceDotVm> PreviewDots { get; }

    public string MoveActionLabel => Session.Services.IndicatorService?.IsMoveMode == true
        ? Loc.Get("Settings.Indicators.Finish")
        : Loc.Get("Settings.Indicators.Move");

    public IReadOnlyList<LanguageChoice> PinnedModeOptions =>
    [
        new(nameof(PinnedWindowIndicatorMode.RingOnly), Loc.Get("Settings.Indicators.PinnedMode.RingOnly")),
        new(nameof(PinnedWindowIndicatorMode.RingPlusG), Loc.Get("Settings.Indicators.PinnedMode.RingPlusG"))
    ];

    [ObservableProperty] private string _selectedPinnedMode = nameof(PinnedWindowIndicatorMode.RingOnly);

    public bool ShowIndicators
    {
        get => Session.Draft.ShowFloatingIndicators;
        set { Session.ApplyImmediateSafe(c => c.ShowFloatingIndicators = value); OnPropertyChanged(); }
    }

    public double Opacity
    {
        get => Session.Draft.IndicatorOpacity;
        set
        {
            Session.ApplyIndicatorVisualLive(c => c.IndicatorOpacity = Math.Clamp(value, 0.3, 1.0));
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreviewOpacity));
        }
    }

    public double Scale
    {
        get => Session.Draft.IndicatorScale;
        set
        {
            Session.ApplyIndicatorVisualLive(c => c.IndicatorScale = Math.Clamp(value, 0.75, 1.5));
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreviewScale));
        }
    }

    public bool Animations
    {
        get => Session.Draft.IndicatorAnimations && !Session.Draft.ReduceMotion;
        set { Session.ApplyImmediateSafe(c => c.IndicatorAnimations = value); OnPropertyChanged(); }
    }

    public bool HideInFullscreen
    {
        get => Session.Draft.HideIndicatorInFullscreen;
        set { Session.ApplyImmediateSafe(c => c.HideIndicatorInFullscreen = value); OnPropertyChanged(); }
    }

    public double PreviewOpacity => Opacity;
    public double PreviewScale => Scale;

    partial void OnSelectedPinnedModeChanged(string value)
    {
        if (!Enum.TryParse<PinnedWindowIndicatorMode>(value, out var mode))
        {
            return;
        }

        Session.ApplyImmediateSafe(c => c.PinnedWindowIndicatorMode = mode);
    }

    [RelayCommand]
    private void ToggleMoveIndicators()
    {
        Session.Services.ToggleMoveIndicators();
        OnPropertyChanged(nameof(MoveActionLabel));
        Session.StatusMessage = Session.Services.IndicatorService?.IsMoveMode == true
            ? Loc.Get("Status.MoveMode")
            : Loc.Get("Status.MoveFinished");
    }

    [RelayCommand]
    private void ResetPositions()
    {
        Session.Services.ResetIndicatorPositions();
        Session.StatusMessage = Loc.Get("Status.PositionsRestored");
    }

    private void OnMoveModeChanged(object? sender, EventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            OnPropertyChanged(nameof(MoveActionLabel)));
    }

    protected override void OnCultureChanged()
    {
        base.OnCultureChanged();
        SetHeader("Settings.Indicators.Title", "Settings.Indicators.Description");
        OnPropertyChanged(nameof(MoveActionLabel));
        OnPropertyChanged(nameof(PinnedModeOptions));
        OnPropertyChanged(nameof(ShowIndicators));
        OnPropertyChanged(nameof(Animations));
        OnPropertyChanged(nameof(HideInFullscreen));
        SelectedPinnedMode = Session.Draft.PinnedWindowIndicatorMode.ToString();
    }
}

public sealed partial class AppearancePageViewModel : PageViewModelBase
{
    public AppearancePageViewModel(SettingsSession session, ILocalizationService loc) : base(session, loc)
    {
        SetHeader("Settings.Appearance.Title", "Settings.Appearance.Description");
        RefreshLanguageOptions();
        SelectedLanguage = session.Draft.Language;
        SelectedTheme = session.Draft.Theme;
        SelectedMotion = session.Draft.MotionPreference.ToString();
        SelectedTransitionStyle = session.Draft.WorkspaceTransitionStyle.ToString();
        SelectedTransitionSpeed = session.Draft.WorkspaceTransitionSpeed.ToString();
    }

    public IReadOnlyList<LanguageChoice> LanguageOptions { get; private set; } = Array.Empty<LanguageChoice>();

    public IReadOnlyList<LanguageChoice> ThemeOptions =>
    [
        new("System", Loc.Get("Settings.Appearance.Theme.System")),
        new("Light", Loc.Get("Settings.Appearance.Theme.Light")),
        new("Dark", Loc.Get("Settings.Appearance.Theme.Dark"))
    ];

    public IReadOnlyList<LanguageChoice> MotionOptions =>
    [
        new(nameof(Core.Motion.MotionPreference.FollowSystem), Loc.Get("Settings.Appearance.Animations.FollowSystem")),
        new(nameof(Core.Motion.MotionPreference.Enabled), Loc.Get("Settings.Appearance.Animations.Enabled")),
        new(nameof(Core.Motion.MotionPreference.Disabled), Loc.Get("Settings.Appearance.Animations.Disabled"))
    ];

    public IReadOnlyList<LanguageChoice> TransitionStyleOptions =>
    [
        new(nameof(WorkspaceTransitionStyle.Slide), Loc.Get("Settings.Appearance.Transition.Style.Slide")),
        new(nameof(WorkspaceTransitionStyle.Fade), Loc.Get("Settings.Appearance.Transition.Style.Fade")),
        new(nameof(WorkspaceTransitionStyle.None), Loc.Get("Settings.Appearance.Transition.Style.None"))
    ];

    public IReadOnlyList<LanguageChoice> TransitionSpeedOptions =>
    [
        new(nameof(WorkspaceTransitionSpeed.Fast), Loc.Get("Settings.Appearance.Transition.Speed.Fast")),
        new(nameof(WorkspaceTransitionSpeed.Normal), Loc.Get("Settings.Appearance.Transition.Speed.Normal")),
        new(nameof(WorkspaceTransitionSpeed.Smooth), Loc.Get("Settings.Appearance.Transition.Speed.Smooth"))
    ];

    [ObservableProperty] private string _selectedLanguage = "System";
    [ObservableProperty] private string _selectedTheme = "System";
    [ObservableProperty] private string _selectedMotion = nameof(Core.Motion.MotionPreference.FollowSystem);
    [ObservableProperty] private string _selectedTransitionStyle = nameof(WorkspaceTransitionStyle.Slide);
    [ObservableProperty] private string _selectedTransitionSpeed = nameof(WorkspaceTransitionSpeed.Normal);

    partial void OnSelectedLanguageChanged(string value)
    {
        Session.ApplyImmediateSafe(c => c.Language = value);
        Loc.SetLanguagePreference(value);
        RefreshLanguageOptions();
    }

    partial void OnSelectedThemeChanged(string value)
    {
        Session.ApplyImmediateSafe(c => c.Theme = value);
        ThemeHelper.ApplyTheme(value, Session.Draft.ReduceMotion);
        OnPropertyChanged(nameof(ThemeOptions));
    }

    partial void OnSelectedMotionChanged(string value)
    {
        if (!Enum.TryParse<Core.Motion.MotionPreference>(value, out var pref))
        {
            return;
        }

        Session.ApplyImmediateSafe(c =>
        {
            c.MotionPreference = pref;
            c.ReduceMotion = pref == Core.Motion.MotionPreference.Disabled;
            if (pref == Core.Motion.MotionPreference.Disabled)
            {
                c.IndicatorAnimations = false;
            }
        });
        App.Services.AnimationSettings.Apply(pref);
    }

    partial void OnSelectedTransitionStyleChanged(string value)
    {
        if (Enum.TryParse<WorkspaceTransitionStyle>(value, out var style))
        {
            Session.ApplyImmediateSafe(c => c.WorkspaceTransitionStyle = style);
        }
    }

    partial void OnSelectedTransitionSpeedChanged(string value)
    {
        if (Enum.TryParse<WorkspaceTransitionSpeed>(value, out var speed))
        {
            Session.ApplyImmediateSafe(c => c.WorkspaceTransitionSpeed = speed);
        }
    }

    private void RefreshLanguageOptions()
    {
        LanguageOptions = Loc.AvailableLanguages
            .Select(o => new LanguageChoice(o.Id, Loc.Get(o.DisplayKey)))
            .ToList();
        OnPropertyChanged(nameof(LanguageOptions));
    }

    protected override void OnCultureChanged()
    {
        base.OnCultureChanged();
        SetHeader("Settings.Appearance.Title", "Settings.Appearance.Description");
        RefreshLanguageOptions();
        OnPropertyChanged(nameof(ThemeOptions));
        OnPropertyChanged(nameof(MotionOptions));
        OnPropertyChanged(nameof(TransitionStyleOptions));
        OnPropertyChanged(nameof(TransitionSpeedOptions));
        SelectedMotion = Session.Draft.MotionPreference.ToString();
        SelectedTransitionStyle = Session.Draft.WorkspaceTransitionStyle.ToString();
        SelectedTransitionSpeed = Session.Draft.WorkspaceTransitionSpeed.ToString();
    }
}

public sealed partial class BehaviorPageViewModel : PageViewModelBase
{
    public BehaviorPageViewModel(SettingsSession session, ILocalizationService loc) : base(session, loc)
        => SetHeader("Settings.Behavior.Title", "Settings.Behavior.Description");

    public bool HideInactiveFromSwitcher
    {
        get => Session.Draft.HideInactiveFromSwitcher;
        set
        {
            Session.ApplyImmediateSafe(c => c.HideInactiveFromSwitcher = value);
            OnPropertyChanged();
        }
    }

    public bool EnforceSingleFullscreenPerWorkspace
    {
        get => Session.Draft.EnforceSingleFullscreenPerWorkspace;
        set
        {
            Session.ApplyImmediateSafe(c => c.EnforceSingleFullscreenPerWorkspace = value);
            OnPropertyChanged();
        }
    }

    protected override void OnCultureChanged()
    {
        base.OnCultureChanged();
        SetHeader("Settings.Behavior.Title", "Settings.Behavior.Description");
        OnPropertyChanged(nameof(HideInactiveFromSwitcher));
        OnPropertyChanged(nameof(EnforceSingleFullscreenPerWorkspace));
    }
}

public sealed partial class StartupPageViewModel : PageViewModelBase
{
    public StartupPageViewModel(SettingsSession session, ILocalizationService loc) : base(session, loc)
    {
        SetHeader("Settings.Startup.Title", "Settings.Startup.Description");
        IsElevated = ElevationService.IsCurrentProcessElevated();
        // Assign backing field — avoid ApplyImmediateSafe/UAC while hydrating the page.
        _elevationPreference = session.Draft.ElevationPreference;
    }

    public bool IsElevated { get; }

    public string ElevationBadgeText => Loc.Get(IsElevated
        ? "Settings.Administration.Elevated"
        : "Settings.Administration.StandardUser");

    public bool StartWithWindows
    {
        get => Session.Draft.StartWithWindows;
        set { Session.ApplyImmediateSafe(c => c.StartWithWindows = value); OnPropertyChanged(); }
    }

    [ObservableProperty]
    private ElevationPreference _elevationPreference;

    public bool IsAsk
    {
        get => ElevationPreference == ElevationPreference.Ask;
        set { if (value) ElevationPreference = ElevationPreference.Ask; }
    }

    public bool IsAlways
    {
        get => ElevationPreference == ElevationPreference.AlwaysElevate;
        set { if (value) ElevationPreference = ElevationPreference.AlwaysElevate; }
    }

    public bool IsNever
    {
        get => ElevationPreference == ElevationPreference.NeverAsk;
        set { if (value) ElevationPreference = ElevationPreference.NeverAsk; }
    }

    partial void OnElevationPreferenceChanged(ElevationPreference value)
    {
        Session.ApplyImmediateSafe(c => c.ElevationPreference = value);
        OnPropertyChanged(nameof(IsAsk));
        OnPropertyChanged(nameof(IsAlways));
        OnPropertyChanged(nameof(IsNever));
    }

    [RelayCommand]
    private void RestartElevated()
    {
        if (IsElevated)
        {
            Session.StatusMessage = Loc.Get("Settings.Administration.AlreadyAdmin");
            return;
        }

        if (!Session.Services.RequestRestartElevated())
        {
            Session.StatusMessage = Loc.Get("Settings.Administration.ElevationFailed");
        }
    }

    protected override void OnCultureChanged()
    {
        base.OnCultureChanged();
        SetHeader("Settings.Startup.Title", "Settings.Startup.Description");
        OnPropertyChanged(nameof(ElevationBadgeText));
        OnPropertyChanged(nameof(StartWithWindows));
    }
}

public sealed partial class AboutPageViewModel : PageViewModelBase
{
    public AboutPageViewModel(SettingsSession session, ILocalizationService loc) : base(session, loc)
    {
        SetHeader("Settings.About.Title", "Settings.About.Description");
        var v = typeof(App).Assembly.GetName().Version;
        VersionText = Loc.Format("Settings.About.Version", v?.ToString(3) ?? "0.1.0");
    }

    public string VersionText { get; private set; }

    [RelayCommand]
    private void OpenRepository()
    {
        OpenUrl(Loc.Get("Settings.About.RepoUrl"));
    }

    [RelayCommand]
    private void OpenConfig()
    {
        var path = Session.Services.ConfigService.ConfigPath;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void OpenLogs()
    {
        var dir = System.IO.Path.GetDirectoryName(Session.Services.ConfigService.ConfigPath);
        if (dir is null) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = dir,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void CopyDiagnostics()
    {
        var text = $"Spaces4Win {VersionText}\nConfig: {Session.Services.ConfigService.ConfigPath}\nElevated: {ElevationService.IsCurrentProcessElevated()}\nLanguage: {Loc.LanguagePreference}\nTheme: {Session.Draft.Theme}";
        System.Windows.Clipboard.SetText(text);
        Session.StatusMessage = Loc.Get("Settings.About.DiagnosticsCopied");
    }

    private static void OpenUrl(string url)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    protected override void OnCultureChanged()
    {
        base.OnCultureChanged();
        SetHeader("Settings.About.Title", "Settings.About.Description");
        var v = typeof(App).Assembly.GetName().Version;
        VersionText = Loc.Format("Settings.About.Version", v?.ToString(3) ?? "0.1.0");
        OnPropertyChanged(nameof(VersionText));
    }
}
