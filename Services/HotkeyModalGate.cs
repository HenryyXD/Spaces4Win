namespace Spaces4Win.Services;

/// <summary>Which Caps overlay currently owns the keyboard.</summary>
public enum HotkeyModalKind
{
    None = 0,
    WorkspaceSwitcher,
    WindowSwitcher,
    Overview,
    PresetBrowser
}

/// <summary>Role of a registered Caps chord for modal gating.</summary>
public enum HotkeyRole
{
    General = 0,
    WorkspaceSwitcher,
    WindowSwitcher,
    Overview,
    PresetBrowser
}

/// <summary>
/// While Caps+Tab / Caps+Q / Caps+` / Caps+P overlays are open, only that overlay's own
/// advance/toggle chord may fire; all other Spaces4Win hotkeys are swallowed.
/// </summary>
public static class HotkeyModalGate
{
    public static bool IsAllowed(HotkeyModalKind modal, HotkeyRole role)
    {
        if (modal == HotkeyModalKind.None)
        {
            return true;
        }

        return modal switch
        {
            HotkeyModalKind.WorkspaceSwitcher => role == HotkeyRole.WorkspaceSwitcher,
            HotkeyModalKind.WindowSwitcher => role == HotkeyRole.WindowSwitcher,
            HotkeyModalKind.Overview => role == HotkeyRole.Overview,
            HotkeyModalKind.PresetBrowser => role == HotkeyRole.PresetBrowser,
            _ => false
        };
    }
}
