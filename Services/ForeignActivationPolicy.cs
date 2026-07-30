namespace Spaces4Win.Services;

/// <summary>
/// Decides what to do when a managed window on another workspace becomes foreground
/// (typically a taskbar click).
/// </summary>
public static class ForeignActivationPolicy
{
    public enum ActionKind
    {
        Ignore,
        SwitchToWindowWorkspace,
        MoveWindowToCurrentWorkspace
    }

    public static ActionKind Decide(
        bool isSticky,
        int windowWorkspace,
        int activeWorkspace,
        bool capsPhysicallyHeld)
    {
        if (isSticky || windowWorkspace == activeWorkspace)
        {
            return ActionKind.Ignore;
        }

        return capsPhysicallyHeld
            ? ActionKind.MoveWindowToCurrentWorkspace
            : ActionKind.SwitchToWindowWorkspace;
    }

    /// <summary>
    /// Caps+digit rapid switches queue FOREGROUND events while Caps is still held.
    /// Those must not be treated as Caps+taskbar "pull window here" moves.
    /// </summary>
    public static bool ShouldIgnoreEvent(
        bool isInternalTransition,
        VisibilityOwnership ownership,
        IntPtr reportedHwnd,
        IntPtr currentForeground)
    {
        if (isInternalTransition)
        {
            return true;
        }

        if (ownership is VisibilityOwnership.HiddenBySpaces4Win or VisibilityOwnership.ExternallyHidden)
        {
            return true;
        }

        if (reportedHwnd == IntPtr.Zero)
        {
            return true;
        }

        // Stale deferred hook callback: focus already moved elsewhere.
        if (currentForeground != IntPtr.Zero &&
            currentForeground != reportedHwnd)
        {
            return true;
        }

        return false;
    }
}
