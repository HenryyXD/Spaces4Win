using Spaces4Win.Config;

namespace Spaces4Win.Services.Transition;

public enum TransitionDirection
{
    None = 0,
    /// <summary>Current exits left; destination enters from right (to higher workspace id).</summary>
    ToHigher = 1,
    /// <summary>Current exits right; destination enters from left (to lower workspace id).</summary>
    ToLower = 2
}

public enum TransitionResultKind
{
    Instant = 0,
    Animated = 1,
    FallbackInstant = 2,
    SkippedSameWorkspace = 3,
    Cancelled = 4,
    Failed = 5
}

public sealed class TransitionOptions
{
    public WorkspaceTransitionStyle Style { get; init; } = WorkspaceTransitionStyle.Slide;
    public WorkspaceTransitionSpeed Speed { get; init; } = WorkspaceTransitionSpeed.Normal;
    public bool AnimationsAllowed { get; init; } = true;
    public bool SkipFullscreen { get; init; } = true;

    public TimeSpan Duration => Speed.ToDuration();

    public static TransitionOptions FromConfig(AppConfig config, bool animationsAllowed) => new()
    {
        Style = config.WorkspaceTransitionStyle,
        Speed = config.WorkspaceTransitionSpeed,
        AnimationsAllowed = animationsAllowed && config.WorkspaceTransitionStyle != WorkspaceTransitionStyle.None,
        // HUD toast must still appear over borderless/fullscreen apps (e.g. Rider Ctrl+Shift+Enter).
        // Heavy curtain engines can opt into SkipFullscreen = true when wired again.
        SkipFullscreen = false
    };
}

public readonly record struct TransitionWindowSnapshot(
    IntPtr Hwnd,
    int LeftPx,
    int TopPx,
    int RightPx,
    int BottomPx,
    bool IsMinimized);

public sealed class TransitionScene
{
    public required int WorkspaceId { get; init; }
    public required IReadOnlyList<TransitionWindowSnapshot> Windows { get; init; }
}

public sealed class TransitionRequest
{
    public required string MonitorId { get; init; }
    public required int FromWorkspace { get; init; }
    public required int ToWorkspace { get; init; }
    public bool CreateIfMissing { get; init; }
}

public sealed class TransitionResult
{
    public required TransitionResultKind Kind { get; init; }
    public string? Message { get; init; }
    public TransitionDirection Direction { get; init; }

    public static TransitionResult Instant(string? msg = null) =>
        new() { Kind = TransitionResultKind.Instant, Message = msg };

    public static TransitionResult Animated(TransitionDirection dir) =>
        new() { Kind = TransitionResultKind.Animated, Direction = dir };

    public static TransitionResult Fallback(string msg) =>
        new() { Kind = TransitionResultKind.FallbackInstant, Message = msg };

    public static TransitionResult Skipped() =>
        new() { Kind = TransitionResultKind.SkippedSameWorkspace };

    public static TransitionResult Cancelled() =>
        new() { Kind = TransitionResultKind.Cancelled };

    public static TransitionResult Failed(string msg) =>
        new() { Kind = TransitionResultKind.Failed, Message = msg };
}

public static class TransitionDirectionHelper
{
    public static TransitionDirection Resolve(int fromWorkspace, int toWorkspace)
    {
        if (fromWorkspace == toWorkspace)
        {
            return TransitionDirection.None;
        }

        return toWorkspace > fromWorkspace
            ? TransitionDirection.ToHigher
            : TransitionDirection.ToLower;
    }
}
