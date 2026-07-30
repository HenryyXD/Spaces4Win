using Spaces4Win.Config;

namespace Spaces4Win.Services.Transition;

public interface IWorkspaceTransitionEngine
{
    /// <summary>
    /// Plays visual feedback for a workspace switch. Engines must invoke
    /// <see cref="TransitionPlayContext.ApplySwitch"/> exactly once (typically first).
    /// Animation is optional feedback and must not own correctness of window state.
    /// </summary>
    Task PlayAsync(TransitionPlayContext context, CancellationToken cancellationToken);
}

public sealed class TransitionPlayContext
{
    public required string MonitorId { get; init; }
    public required System.Drawing.Rectangle WorkAreaPx { get; init; }
    public required TransitionScene Outgoing { get; init; }
    public required TransitionScene Incoming { get; init; }
    public required TransitionDirection Direction { get; init; }
    public required TransitionOptions Options { get; init; }
    public required Action ApplySwitch { get; init; }
    /// <summary>Existing workspace ids on this monitor (for HUD dots).</summary>
    public IReadOnlyList<int> WorkspaceIds { get; init; } = Array.Empty<int>();
    /// <summary>Show incoming HWNDs after the overlay is covering the monitor (no flash).</summary>
    public Action? PrepareIncomingUnderCover { get; init; }
}

/// <summary>Test/instant engine: applies the switch immediately with no visuals.</summary>
public sealed class NullWorkspaceTransitionEngine : IWorkspaceTransitionEngine
{
    public Task PlayAsync(TransitionPlayContext context, CancellationToken cancellationToken)
    {
        context.ApplySwitch();
        return Task.CompletedTask;
    }
}

public interface IWorkspaceNavigator
{
    void SwitchOrCreate(string monitorId, int workspaceId);
    void Switch(string monitorId, int workspaceId);
    void SwitchToLast(string monitorId);
}
