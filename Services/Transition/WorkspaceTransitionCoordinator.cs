using Spaces4Win.Config;
using Spaces4Win.Core;
using Spaces4Win.Native;
using Spaces4Win.Services.Animation;

namespace Spaces4Win.Services.Transition;

/// <summary>
/// Coordinates per-monitor workspace transitions.
/// Rapid switches cancel the in-flight HUD and start the latest target immediately.
/// </summary>
public sealed class WorkspaceTransitionCoordinator : IWorkspaceNavigator, IDisposable
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly IAnimationSettingsService _animationSettings;
    private readonly Func<AppConfig> _config;
    private readonly IWorkspaceTransitionEngine _engine;
    private readonly object _sync = new();
    private readonly Dictionary<string, MonitorTransitionState> _states =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public WorkspaceTransitionCoordinator(
        WorkspaceManager workspaceManager,
        IAnimationSettingsService animationSettings,
        Func<AppConfig> config,
        IWorkspaceTransitionEngine? engine = null)
    {
        _workspaceManager = workspaceManager;
        _animationSettings = animationSettings;
        _config = config;
        _engine = engine ?? new NullWorkspaceTransitionEngine();
    }

    public event EventHandler<TransitionResult>? TransitionCompleted;

    public void SwitchOrCreate(string monitorId, int workspaceId) =>
        Request(monitorId, workspaceId, createIfMissing: true);

    public void Switch(string monitorId, int workspaceId) =>
        Request(monitorId, workspaceId, createIfMissing: false);

    /// <summary>
    /// Switch to an existing workspace, then activate <paramref name="activateHwnd"/>
    /// immediately after the real switch (before/during HUD).
    /// </summary>
    public void SwitchAndActivate(string monitorId, int workspaceId, IntPtr activateHwnd) =>
        Request(monitorId, workspaceId, createIfMissing: false, activateHwnd);

    public void SwitchToLast(string monitorId)
    {
        var monitor = _workspaceManager.Monitors.FirstOrDefault(m =>
            string.Equals(m.MonitorId, monitorId, StringComparison.OrdinalIgnoreCase));
        if (monitor is null)
        {
            return;
        }

        var target = monitor.LastActiveWorkspace;
        if (target == monitor.ActiveWorkspace || !monitor.HasWorkspace(target))
        {
            _workspaceManager.SwitchToLastWorkspace(monitorId);
            TransitionCompleted?.Invoke(this, TransitionResult.Instant());
            return;
        }

        Request(monitorId, target, createIfMissing: false);
    }

    public void Request(
        string monitorId,
        int workspaceId,
        bool createIfMissing,
        IntPtr activateHwnd = default)
    {
        if (_disposed || string.IsNullOrWhiteSpace(monitorId))
        {
            return;
        }

        CancellationTokenSource? toCancel = null;
        bool startNow;
        MonitorTransitionState state;
        lock (_sync)
        {
            if (!_states.TryGetValue(monitorId, out state!))
            {
                state = new MonitorTransitionState();
                _states[monitorId] = state;
            }

            state.PendingActivateHwnd = activateHwnd;

            if (state.IsBusy)
            {
                // Latest target wins — interrupt current HUD/animation now.
                state.PendingWorkspace = workspaceId;
                state.PendingCreateIfMissing = createIfMissing;
                toCancel = state.PlayCts;
                startNow = false;
            }
            else
            {
                state.IsBusy = true;
                state.PendingWorkspace = null;
                startNow = true;
            }
        }

        try
        {
            toCancel?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // ignore
        }

        if (startNow)
        {
            _ = RunAsync(monitorId, workspaceId, createIfMissing, state);
        }
    }

    private async Task RunAsync(
        string monitorId,
        int workspaceId,
        bool createIfMissing,
        MonitorTransitionState state)
    {
        try
        {
            while (true)
            {
                var result = await ExecuteOnceAsync(monitorId, workspaceId, createIfMissing, state)
                    .ConfigureAwait(true);

                // Skip Completed noise for cancelled mid-flight; still notify others.
                if (result.Kind != TransitionResultKind.Cancelled)
                {
                    TransitionCompleted?.Invoke(this, result);
                }

                int? pending;
                bool pendingCreate;
                lock (_sync)
                {
                    pending = state.PendingWorkspace;
                    pendingCreate = state.PendingCreateIfMissing;
                    state.PendingWorkspace = null;
                    if (pending is null)
                    {
                        state.IsBusy = false;
                        return;
                    }
                }

                workspaceId = pending.Value;
                createIfMissing = pendingCreate;
            }
        }
        catch
        {
            lock (_sync)
            {
                state.IsBusy = false;
                state.PendingWorkspace = null;
                state.PlayCts = null;
            }

            try
            {
                ApplyInstant(monitorId, workspaceId, createIfMissing);
                TransitionCompleted?.Invoke(this, TransitionResult.Fallback("Unexpected error"));
            }
            catch
            {
                TransitionCompleted?.Invoke(this, new TransitionResult
                {
                    Kind = TransitionResultKind.Failed,
                    Message = "Switch failed"
                });
            }
        }
    }

    private async Task<TransitionResult> ExecuteOnceAsync(
        string monitorId,
        int workspaceId,
        bool createIfMissing,
        MonitorTransitionState state)
    {
        var monitor = _workspaceManager.Monitors.FirstOrDefault(m =>
            string.Equals(m.MonitorId, monitorId, StringComparison.OrdinalIgnoreCase));
        if (monitor is null)
        {
            return TransitionResult.Failed("Monitor not found");
        }

        if (createIfMissing && !monitor.HasWorkspace(workspaceId))
        {
            _workspaceManager.EnsureWorkspaceExists(monitorId, workspaceId);
            monitor = _workspaceManager.Monitors.FirstOrDefault(m =>
                string.Equals(m.MonitorId, monitorId, StringComparison.OrdinalIgnoreCase));
            if (monitor is null)
            {
                return TransitionResult.Failed("Monitor not found");
            }
        }

        if (!monitor.HasWorkspace(workspaceId))
        {
            return TransitionResult.Failed("Workspace missing");
        }

        var from = monitor.ActiveWorkspace;
        if (from == workspaceId)
        {
            ActivatePending(state);
            return TransitionResult.Skipped();
        }

        var options = TransitionOptions.FromConfig(_config(), _animationSettings.AnimationsEnabled);
        var direction = TransitionDirectionHelper.Resolve(from, workspaceId);

        if (!options.AnimationsAllowed ||
            options.Style == WorkspaceTransitionStyle.None ||
            direction == TransitionDirection.None)
        {
            ApplyInstant(monitorId, workspaceId, createIfMissing: false);
            ActivatePending(state);
            return TransitionResult.Instant();
        }

        if (options.SkipFullscreen && IsExclusiveFullscreenLikely(monitor))
        {
            ApplyInstant(monitorId, workspaceId, createIfMissing: false);
            ActivatePending(state);
            return TransitionResult.Fallback("Fullscreen — instant switch");
        }

        var outgoing = TransitionSceneBuilder.TryBuild(_workspaceManager, monitorId, from);
        var incoming = TransitionSceneBuilder.TryBuild(_workspaceManager, monitorId, workspaceId)
                       ?? new TransitionScene { WorkspaceId = workspaceId, Windows = Array.Empty<TransitionWindowSnapshot>() };

        if (outgoing is null)
        {
            ApplyInstant(monitorId, workspaceId, createIfMissing: false);
            ActivatePending(state);
            return TransitionResult.Fallback("Could not snapshot outgoing workspace");
        }

        var workArea = monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;
        var switchApplied = false;

        void ApplySwitch()
        {
            if (switchApplied)
            {
                return;
            }

            switchApplied = true;
            ApplyInstant(monitorId, workspaceId, createIfMissing: false);
            ActivatePending(state);
        }

        var playCts = new CancellationTokenSource();
        using var timeoutCts = new CancellationTokenSource(options.Duration + TimeSpan.FromSeconds(3));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(playCts.Token, timeoutCts.Token);
        lock (_sync)
        {
            state.PlayCts = playCts;
        }

        try
        {
            var context = new TransitionPlayContext
            {
                MonitorId = monitorId,
                WorkAreaPx = workArea,
                Outgoing = outgoing,
                Incoming = incoming,
                Direction = direction,
                Options = options,
                ApplySwitch = ApplySwitch,
                WorkspaceIds = monitor.WorkspaceIds,
                PrepareIncomingUnderCover = () => PrepareIncomingVisibility(incoming)
            };

            await _engine.PlayAsync(context, linkedCts.Token).ConfigureAwait(true);

            if (!switchApplied)
            {
                ApplySwitch();
            }

            return TransitionResult.Animated(direction);
        }
        catch (OperationCanceledException)
        {
            // Workspace switch may already have happened; finish if not.
            ApplySwitch();
            return TransitionResult.Cancelled();
        }
        catch (Exception ex)
        {
            ApplySwitch();
            return TransitionResult.Fallback(ex.Message);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(state.PlayCts, playCts))
                {
                    state.PlayCts = null;
                }
            }

            playCts.Dispose();
        }
    }

    private void PrepareIncomingVisibility(TransitionScene incoming)
    {
        foreach (var win in incoming.Windows)
        {
            if (!NativeMethods.IsWindow(win.Hwnd))
            {
                continue;
            }

            // Cloaked windows stay "visible" to Win32; do not SW_SHOWNA (shrinks maximize/snap).
            if (WindowClassifier.IsCloaked(win.Hwnd))
            {
                var uncloak = 0;
                NativeMethods.DwmSetWindowAttribute(
                    win.Hwnd,
                    NativeMethods.DWMWA_CLOAK,
                    ref uncloak,
                    sizeof(int));
                continue;
            }

            if (!NativeMethods.IsWindowVisible(win.Hwnd) || NativeMethods.IsIconic(win.Hwnd))
            {
                if (win.IsMinimized)
                {
                    NativeMethods.ShowWindow(win.Hwnd, NativeMethods.SW_SHOWMINNOACTIVE);
                }
                else
                {
                    NativeMethods.ShowWindow(win.Hwnd, NativeMethods.SW_SHOWNA);
                }
            }
        }
    }

    private void ApplyInstant(string monitorId, int workspaceId, bool createIfMissing)
    {
        if (createIfMissing)
        {
            _workspaceManager.SwitchOrCreateWorkspace(monitorId, workspaceId);
        }
        else
        {
            _workspaceManager.SwitchWorkspace(monitorId, workspaceId);
        }
    }

    private void ActivatePending(MonitorTransitionState state)
    {
        IntPtr hwnd;
        lock (state)
        {
            hwnd = state.PendingActivateHwnd;
            state.PendingActivateHwnd = IntPtr.Zero;
        }

        if (hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd))
        {
            _workspaceManager.ActivateManagedWindow(hwnd);
        }
    }

    private static bool IsExclusiveFullscreenLikely(MonitorWorkspace monitor)
    {
        try
        {
            var fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero || !NativeMethods.GetWindowRect(fg, out var rect))
            {
                return false;
            }

            var bounds = monitor.Bounds;
            var w = rect.Right - rect.Left;
            var h = rect.Bottom - rect.Top;
            return w >= bounds.Width - 2 && h >= bounds.Height - 2 &&
                   Math.Abs(rect.Left - bounds.Left) <= 2 &&
                   Math.Abs(rect.Top - bounds.Top) <= 2 &&
                   !NativeMethods.IsIconic(fg);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        lock (_sync)
        {
            foreach (var s in _states.Values)
            {
                s.PendingWorkspace = null;
                try
                {
                    s.PlayCts?.Cancel();
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    private sealed class MonitorTransitionState
    {
        public bool IsBusy;
        public int? PendingWorkspace;
        public bool PendingCreateIfMissing;
        public IntPtr PendingActivateHwnd;
        public CancellationTokenSource? PlayCts;
    }
}
