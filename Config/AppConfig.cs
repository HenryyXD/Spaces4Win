using System.Text.Json.Serialization;
using System.Windows.Input;
using Spaces4Win.Core.Motion;

namespace Spaces4Win.Config;

public enum ElevationPreference
{
    /// <summary>Prompt when an elevated window cannot be controlled.</summary>
    Ask = 0,

    /// <summary>Prefer elevated process: register Highest logon task when Start with Windows is on (no UAC at login). Does not force UAC on every manual start.</summary>
    AlwaysElevate = 1,

    /// <summary>Skip elevated windows; never prompt.</summary>
    NeverAsk = 2
}

public sealed class AppConfig
{
    public bool StartWithWindows { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ElevationPreference ElevationPreference { get; set; } = ElevationPreference.Ask;

    public bool ShowFloatingIndicators { get; set; } = true;

    /// <summary>0.3–1.0 capsule opacity.</summary>
    public double IndicatorOpacity { get; set; } = 0.88;

    /// <summary>0.75–1.5 scale factor for dots.</summary>
    public double IndicatorScale { get; set; } = 1.0;

    public bool IndicatorAnimations { get; set; } = true;

    /// <summary>How the floating indicator shows an active pinned window (default: double ring only).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PinnedWindowIndicatorMode PinnedWindowIndicatorMode { get; set; } = PinnedWindowIndicatorMode.RingOnly;

    /// <summary>Experimental: hide floating indicator over exclusive fullscreen (policy hook).</summary>
    public bool HideIndicatorInFullscreen { get; set; }

    /// <summary>
    /// When true, inactive workspace windows use SW_HIDE (leave Alt-Tab and taskbar)
    /// instead of DWM cloak. Taskbar jump-to-workspace for inactive spaces will not work.
    /// </summary>
    public bool HideInactiveFromSwitcher { get; set; }

    /// <summary>
    /// When true (default), at most one borderless-fullscreen window per workspace.
    /// Moving another window into a workspace that already has a fullscreen window
    /// exits that fullscreen so both remain visible on the taskbar.
    /// </summary>
    public bool EnforceSingleFullscreenPerWorkspace { get; set; } = true;

    /// <summary>System | Light | Dark</summary>
    public string Theme { get; set; } = "System";

    /// <summary>System | en | pt-BR</summary>
    public string Language { get; set; } = "System";

    /// <summary>Global animation preference for the whole app.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MotionPreference MotionPreference { get; set; } = MotionPreference.FollowSystem;

    /// <summary>Legacy flag; synced from MotionPreference on load/save.</summary>
    public bool ReduceMotion { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WorkspaceTransitionStyle WorkspaceTransitionStyle { get; set; } = WorkspaceTransitionStyle.Slide;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WorkspaceTransitionSpeed WorkspaceTransitionSpeed { get; set; } = WorkspaceTransitionSpeed.Normal;

    /// <summary>System or a named accent token.</summary>
    public string AccentColor { get; set; } = "System";

    public Dictionary<string, MonitorConfig> Monitors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<HotkeyBinding> SwitchWorkspaceHotkeys { get; set; } = CreateDefaultSwitchHotkeys();

    public List<HotkeyBinding> MoveWindowHotkeys { get; set; } = CreateDefaultMoveHotkeys();

    /// <summary>Move focused window to workspace N and switch there (default CapsLock+Ctrl+N).</summary>
    public List<HotkeyBinding> MoveWindowAndFollowHotkeys { get; set; } = CreateDefaultMoveAndFollowHotkeys();

    /// <summary>Toggle previous workspace on the monitor under the cursor (default CapsLock+Tab). Hold Caps and press Tab again to cycle.</summary>
    public HotkeyBinding ToggleLastWorkspaceHotkey { get; set; } = CreateDefaultToggleLastHotkey();

    /// <summary>Window switcher for the active workspace (default CapsLock+Q).</summary>
    public HotkeyBinding WindowSwitcherHotkey { get; set; } = CreateDefaultWindowSwitcherHotkey();

    /// <summary>Toggle sticky for the focused window (default CapsLock+S).</summary>
    public HotkeyBinding ToggleStickyHotkey { get; set; } = CreateDefaultToggleStickyHotkey();

    /// <summary>Densify sparse workspace ids to 1..N on the hotkey monitor (default CapsLock+R).</summary>
    public HotkeyBinding CompactWorkspacesHotkey { get; set; } = CreateDefaultCompactWorkspacesHotkey();

    /// <summary>Move focused window to a dedicated workspace and borderless fullscreen (default CapsLock+F).</summary>
    public HotkeyBinding FullscreenWorkspaceHotkey { get; set; } = CreateDefaultFullscreenWorkspaceHotkey();

    /// <summary>Caps+Shift+F — move focused window to a free workspace without following.</summary>
    public HotkeyBinding MoveToFreeWorkspaceHotkey { get; set; } = CreateDefaultMoveToFreeWorkspaceHotkey();

    /// <summary>Caps+Ctrl+F — move focused window to a free workspace and follow.</summary>
    public HotkeyBinding MoveToFreeWorkspaceFollowHotkey { get; set; } = CreateDefaultMoveToFreeWorkspaceFollowHotkey();

    /// <summary>Delete the current workspace on the monitor under the cursor (default CapsLock+Backspace).</summary>
    public HotkeyBinding DeleteWorkspaceHotkey { get; set; } = CreateDefaultDeleteWorkspaceHotkey();

    /// <summary>Open Mission Control-style overview on all monitors (default CapsLock+`).</summary>
    public HotkeyBinding OverviewHotkey { get; set; } = CreateDefaultOverviewHotkey();

    /// <summary>Previous existing workspace (default CapsLock+Left). Does not create.</summary>
    public HotkeyBinding PreviousWorkspaceHotkey { get; set; } = CreateDefaultPreviousWorkspaceHotkey();

    /// <summary>Next existing workspace (default CapsLock+Right). Does not create.</summary>
    public HotkeyBinding NextWorkspaceHotkey { get; set; } = CreateDefaultNextWorkspaceHotkey();

    /// <summary>Insert focused window left and renumber; stay on the former workspace (default CapsLock+Alt+Left).</summary>
    public HotkeyBinding ShiftWindowLeftHotkey { get; set; } = CreateDefaultShiftWindowLeftHotkey();

    /// <summary>Insert focused window right and renumber; stay on the former workspace (default CapsLock+Alt+Right).</summary>
    public HotkeyBinding ShiftWindowRightHotkey { get; set; } = CreateDefaultShiftWindowRightHotkey();

    /// <summary>Insert focused window left, renumber, and follow (default CapsLock+Ctrl+Alt+Left).</summary>
    public HotkeyBinding ShiftWindowFollowLeftHotkey { get; set; } = CreateDefaultShiftWindowFollowLeftHotkey();

    /// <summary>Insert focused window right, renumber, and follow (default CapsLock+Ctrl+Alt+Right).</summary>
    public HotkeyBinding ShiftWindowFollowRightHotkey { get; set; } = CreateDefaultShiftWindowFollowRightHotkey();

    /// <summary>Move focused window to previous existing workspace; no follow, no create (default CapsLock+Shift+Left).</summary>
    public HotkeyBinding MoveAdjacentLeftHotkey { get; set; } = CreateDefaultMoveAdjacentLeftHotkey();

    /// <summary>Move focused window to next existing workspace; no follow, no create (default CapsLock+Shift+Right).</summary>
    public HotkeyBinding MoveAdjacentRightHotkey { get; set; } = CreateDefaultMoveAdjacentRightHotkey();

    /// <summary>Move focused window to previous workspace and follow; create at left extreme (default CapsLock+Ctrl+Left).</summary>
    public HotkeyBinding MoveAdjacentFollowLeftHotkey { get; set; } = CreateDefaultMoveAdjacentFollowLeftHotkey();

    /// <summary>Move focused window to next workspace and follow; create at right extreme (default CapsLock+Ctrl+Right).</summary>
    public HotkeyBinding MoveAdjacentFollowRightHotkey { get; set; } = CreateDefaultMoveAdjacentFollowRightHotkey();

    /// <summary>Move cursor to previous monitor (default CapsLock+[).</summary>
    public HotkeyBinding FocusPreviousMonitorHotkey { get; set; } = CreateDefaultFocusPreviousMonitorHotkey();

    /// <summary>Move cursor to next monitor (default CapsLock+]).</summary>
    public HotkeyBinding FocusNextMonitorHotkey { get; set; } = CreateDefaultFocusNextMonitorHotkey();

    /// <summary>Open session-preset browser (default CapsLock+P).</summary>
    public HotkeyBinding PresetBrowserHotkey { get; set; } = CreateDefaultPresetBrowserHotkey();

    /// <summary>Save current layout to preset slot 0–9 (default CapsLock+Ctrl+Alt+digit).</summary>
    public List<HotkeyBinding> SavePresetHotkeys { get; set; } = CreateDefaultSavePresetHotkeys();

    /// <summary>Open preset browser focused on slot 0–9 (default CapsLock+Alt+digit).</summary>
    public List<HotkeyBinding> LoadPresetHotkeys { get; set; } = CreateDefaultLoadPresetHotkeys();

    public IReadOnlyList<int> GetWorkspaceIds(string monitorId)
    {
        if (Monitors.TryGetValue(monitorId, out var cfg))
        {
            return cfg.ResolveWorkspaceIds();
        }

        return new[] { 1 };
    }

    public static AppConfig CreateDefault() => new();

    public static List<HotkeyBinding> CreateDefaultSwitchHotkeys()
    {
        return Enumerable.Range(1, 9)
            .Select(i => new HotkeyBinding
            {
                Workspace = i,
                CapsLock = true,
                Modifiers = ModifierKeys.None,
                Key = IndexToKey(i)
            })
            .ToList();
    }

    public static List<HotkeyBinding> CreateDefaultMoveHotkeys()
    {
        return Enumerable.Range(1, 9)
            .Select(i => new HotkeyBinding
            {
                Workspace = i,
                CapsLock = true,
                Modifiers = ModifierKeys.Shift,
                Key = IndexToKey(i)
            })
            .ToList();
    }

    public static List<HotkeyBinding> CreateDefaultMoveAndFollowHotkeys()
    {
        return Enumerable.Range(1, 9)
            .Select(i => new HotkeyBinding
            {
                Workspace = i,
                CapsLock = true,
                Modifiers = ModifierKeys.Control,
                Key = IndexToKey(i)
            })
            .ToList();
    }

    public static HotkeyBinding CreateDefaultToggleLastHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.None,
        Key = Key.Tab
    };

    public static HotkeyBinding CreateDefaultWindowSwitcherHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.None,
        Key = Key.Q
    };

    public static HotkeyBinding CreateDefaultToggleStickyHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.None,
        Key = Key.S
    };

    public static HotkeyBinding CreateDefaultCompactWorkspacesHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.None,
        Key = Key.R
    };

    public static HotkeyBinding CreateDefaultFullscreenWorkspaceHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.None,
        Key = Key.F
    };

    public static HotkeyBinding CreateDefaultMoveToFreeWorkspaceHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.Shift,
        Key = Key.F
    };

    public static HotkeyBinding CreateDefaultMoveToFreeWorkspaceFollowHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.Control,
        Key = Key.F
    };

    public static HotkeyBinding CreateDefaultDeleteWorkspaceHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.None,
        Key = Key.Back
    };

    public static HotkeyBinding CreateDefaultOverviewHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.None,
        Key = Key.Oem3 // backtick / tilde key (`)
    };

    public static HotkeyBinding CreateDefaultPreviousWorkspaceHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.None,
        Key = Key.Left
    };

    public static HotkeyBinding CreateDefaultNextWorkspaceHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.None,
        Key = Key.Right
    };

    public static HotkeyBinding CreateDefaultShiftWindowLeftHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.Alt,
        Key = Key.Left
    };

    public static HotkeyBinding CreateDefaultShiftWindowRightHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.Alt,
        Key = Key.Right
    };

    public static HotkeyBinding CreateDefaultShiftWindowFollowLeftHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.Control | ModifierKeys.Alt,
        Key = Key.Left
    };

    public static HotkeyBinding CreateDefaultShiftWindowFollowRightHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.Control | ModifierKeys.Alt,
        Key = Key.Right
    };

    public static HotkeyBinding CreateDefaultMoveAdjacentLeftHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.Shift,
        Key = Key.Left
    };

    public static HotkeyBinding CreateDefaultMoveAdjacentRightHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.Shift,
        Key = Key.Right
    };

    public static HotkeyBinding CreateDefaultMoveAdjacentFollowLeftHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.Control,
        Key = Key.Left
    };

    public static HotkeyBinding CreateDefaultMoveAdjacentFollowRightHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.Control,
        Key = Key.Right
    };

    public static HotkeyBinding CreateDefaultFocusPreviousMonitorHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.None,
        Key = Key.OemOpenBrackets
    };

    public static HotkeyBinding CreateDefaultFocusNextMonitorHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.None,
        Key = Key.OemCloseBrackets
    };

    public static HotkeyBinding CreateDefaultPresetBrowserHotkey() => new()
    {
        Workspace = 0,
        CapsLock = true,
        Modifiers = ModifierKeys.None,
        Key = Key.P
    };

    public static List<HotkeyBinding> CreateDefaultSavePresetHotkeys() =>
        Enumerable.Range(0, 10)
            .Select(slot => new HotkeyBinding
            {
                Workspace = slot,
                CapsLock = true,
                Modifiers = ModifierKeys.Control | ModifierKeys.Alt,
                Key = SlotToKey(slot)
            })
            .ToList();

    public static List<HotkeyBinding> CreateDefaultLoadPresetHotkeys() =>
        Enumerable.Range(0, 10)
            .Select(slot => new HotkeyBinding
            {
                Workspace = slot,
                CapsLock = true,
                Modifiers = ModifierKeys.Alt,
                Key = SlotToKey(slot)
            })
            .ToList();

    /// <summary>
    /// True when config still has the old Alt / Alt+Shift scheme that conflicted with browsers.
    /// </summary>
    public static bool IsLegacyAltHotkeyScheme(AppConfig config)
    {
        if (config.SwitchWorkspaceHotkeys.Count == 0 || config.MoveWindowHotkeys.Count == 0)
        {
            return false;
        }

        var switchLegacy = config.SwitchWorkspaceHotkeys.All(h =>
            !h.CapsLock && h.Modifiers == ModifierKeys.Alt);

        var moveLegacy = config.MoveWindowHotkeys.All(h =>
            !h.CapsLock && h.Modifiers == (ModifierKeys.Alt | ModifierKeys.Shift));

        return switchLegacy && moveLegacy;
    }

    private static Key IndexToKey(int index) => index switch
    {
        1 => Key.D1,
        2 => Key.D2,
        3 => Key.D3,
        4 => Key.D4,
        5 => Key.D5,
        6 => Key.D6,
        7 => Key.D7,
        8 => Key.D8,
        9 => Key.D9,
        _ => Key.D1
    };

    private static Key SlotToKey(int slot) => slot switch
    {
        0 => Key.D0,
        1 => Key.D1,
        2 => Key.D2,
        3 => Key.D3,
        4 => Key.D4,
        5 => Key.D5,
        6 => Key.D6,
        7 => Key.D7,
        8 => Key.D8,
        9 => Key.D9,
        _ => Key.D1
    };
}

public sealed class MonitorConfig
{
    /// <summary>Stable sparse workspace ids for this monitor. Prefer this over WorkspaceCount.</summary>
    public List<int>? WorkspaceIds { get; set; }

    /// <summary>Legacy fixed count (migrated to 1..N once).</summary>
    public int WorkspaceCount { get; set; }

    public double? IndicatorLeftDip { get; set; }

    public double? IndicatorTopDip { get; set; }

    public IReadOnlyList<int> ResolveWorkspaceIds()
    {
        if (WorkspaceIds is { Count: > 0 })
        {
            return WorkspaceIds
                .Where(id => id is >= 1 and <= 9)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
        }

        if (WorkspaceCount is >= 1 and <= 9)
        {
            return Enumerable.Range(1, WorkspaceCount).ToList();
        }

        return new[] { 1 };
    }
}

public sealed class HotkeyBinding
{
    public int Workspace { get; set; }

    /// <summary>When true, CapsLock must be held (used as a modifier, not as lock toggle).</summary>
    public bool CapsLock { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ModifierKeys Modifiers { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Key Key { get; set; }

    public string Display
    {
        get
        {
            var parts = new List<string>();
            if (CapsLock)
            {
                parts.Add("CapsLock");
            }

            if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(FormatKey(Key));
            return string.Join("+", parts);
        }
    }

    private static string FormatKey(Key key) => key switch
    {
        Key.D0 => "0",
        Key.D1 => "1",
        Key.D2 => "2",
        Key.D3 => "3",
        Key.D4 => "4",
        Key.D5 => "5",
        Key.D6 => "6",
        Key.D7 => "7",
        Key.D8 => "8",
        Key.D9 => "9",
        Key.Tab => "Tab",
        Key.Space => "Space",
        Key.Back => "Backspace",
        Key.Oem3 or Key.OemTilde => "`",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        _ => key.ToString()
    };
}
