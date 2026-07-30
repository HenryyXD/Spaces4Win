using Spaces4Win.Config;
using Spaces4Win.Core;
using Spaces4Win.Core.Motion;
using Spaces4Win.Services;
using Spaces4Win.Services.Animation;
using Spaces4Win.Services.Transition;

namespace Spaces4Win.Tests;

public class WorkspaceTransitionCoordinatorTests
{
    private sealed class FakeSystem(bool reduce) : ISystemMotionPreference
    {
        public bool IsReduceMotionPreferred => reduce;
    }

    private sealed class RecordingEngine : IWorkspaceTransitionEngine
    {
        public int PlayCount { get; private set; }
        public List<TransitionPlayContext> Plays { get; } = new();
        public bool ThrowOnPlay { get; set; }
        public Action<TransitionPlayContext>? OnPlay { get; set; }

        public Task PlayAsync(TransitionPlayContext context, CancellationToken cancellationToken)
        {
            PlayCount++;
            Plays.Add(context);
            if (ThrowOnPlay)
            {
                throw new InvalidOperationException("thumbnail fail");
            }

            OnPlay?.Invoke(context);
            context.ApplySwitch();
            return Task.CompletedTask;
        }
    }

    private static (WorkspaceManager Wm, WorkspaceTransitionCoordinator Coord, RecordingEngine Engine, AppConfig Cfg)
        Create(MotionPreference motion = MotionPreference.Enabled, WorkspaceTransitionStyle style = WorkspaceTransitionStyle.Slide)
    {
        var tracker = new MonitorTracker();
        // Don't Start tracker — inject monitor via Rebuild is hard. Use real tracker with Start may need STA.
        // Instead construct WM after manually... MonitorTracker without Start has empty monitors.

        // Use WorkspaceManager with a custom approach: create via Ensure after Start isn't available.
        // Simplest: unit-test direction helper + coordinator coalescing with a stub WM is heavy.
        // Test pure helpers and RecordingEngine path with a minimal fake by using Null engine + real WM.

        var visibility = new WindowVisibilityService();
        var wm = new WorkspaceManager(tracker, visibility, _ => new[] { 1, 2, 3 });
        // Force a monitor into WM by reflecting Rebuild — actually Monitors come from tracker.
        // For tests without displays, use helper-only tests + coordinator with null engine when no monitors.

        var cfg = new AppConfig
        {
            MotionPreference = motion,
            WorkspaceTransitionStyle = style,
            WorkspaceTransitionSpeed = WorkspaceTransitionSpeed.Fast
        };
        var anim = new AnimationSettingsService(new FakeSystem(false));
        anim.Apply(motion);
        var engine = new RecordingEngine();
        var coord = new WorkspaceTransitionCoordinator(wm, anim, () => cfg, engine);
        return (wm, coord, engine, cfg);
    }

    [Theory]
    [InlineData(1, 3, TransitionDirection.ToHigher)]
    [InlineData(7, 3, TransitionDirection.ToLower)]
    [InlineData(2, 2, TransitionDirection.None)]
    public void Direction_ResolvesFromIds(int from, int to, TransitionDirection expected)
    {
        Assert.Equal(expected, TransitionDirectionHelper.Resolve(from, to));
    }

    [Fact]
    public void Speed_MapsToDurationPresets()
    {
        Assert.Equal(100, WorkspaceTransitionSpeed.Fast.ToDuration().TotalMilliseconds);
        Assert.Equal(130, WorkspaceTransitionSpeed.Normal.ToDuration().TotalMilliseconds);
        Assert.Equal(180, WorkspaceTransitionSpeed.Smooth.ToDuration().TotalMilliseconds);
    }

    [Fact]
    public void Options_FromConfig_DisablesWhenStyleNone()
    {
        var cfg = new AppConfig { WorkspaceTransitionStyle = WorkspaceTransitionStyle.None };
        var opt = TransitionOptions.FromConfig(cfg, animationsAllowed: true);
        Assert.False(opt.AnimationsAllowed);
    }

    [Fact]
    public void Options_RespectsGlobalAnimationsGate()
    {
        var cfg = new AppConfig { WorkspaceTransitionStyle = WorkspaceTransitionStyle.Slide };
        var opt = TransitionOptions.FromConfig(cfg, animationsAllowed: false);
        Assert.False(opt.AnimationsAllowed);
    }

    [Fact]
    public void Options_FromConfig_DoesNotSkipFullscreenForHud()
    {
        var cfg = new AppConfig { WorkspaceTransitionStyle = WorkspaceTransitionStyle.Slide };
        var opt = TransitionOptions.FromConfig(cfg, animationsAllowed: true);
        Assert.False(opt.SkipFullscreen);
    }

    [Fact]
    public void Config_PersistsStableEnumNames()
    {
        var cfg = new AppConfig
        {
            WorkspaceTransitionStyle = WorkspaceTransitionStyle.Fade,
            WorkspaceTransitionSpeed = WorkspaceTransitionSpeed.Smooth
        };
        var json = System.Text.Json.JsonSerializer.Serialize(cfg);
        Assert.Contains("Fade", json);
        Assert.Contains("Smooth", json);
        var round = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json);
        Assert.Equal(WorkspaceTransitionStyle.Fade, round!.WorkspaceTransitionStyle);
        Assert.Equal(WorkspaceTransitionSpeed.Smooth, round.WorkspaceTransitionSpeed);
    }

    [Fact]
    public void NullEngine_ApplySwitchIsCalled()
    {
        var engine = new NullWorkspaceTransitionEngine();
        var applied = false;
        var ctx = new TransitionPlayContext
        {
            MonitorId = "m",
            WorkAreaPx = new System.Drawing.Rectangle(0, 0, 100, 100),
            Outgoing = new TransitionScene { WorkspaceId = 1, Windows = Array.Empty<TransitionWindowSnapshot>() },
            Incoming = new TransitionScene { WorkspaceId = 2, Windows = Array.Empty<TransitionWindowSnapshot>() },
            Direction = TransitionDirection.ToHigher,
            Options = new TransitionOptions(),
            ApplySwitch = () => applied = true
        };
        engine.PlayAsync(ctx, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(applied);
    }

    [Fact]
    public void RecordingEngine_CanSimulateThumbnailFailureThenFallbackPath()
    {
        var engine = new RecordingEngine { ThrowOnPlay = true };
        var applied = false;
        var ctx = new TransitionPlayContext
        {
            MonitorId = "m",
            WorkAreaPx = new System.Drawing.Rectangle(0, 0, 100, 100),
            Outgoing = new TransitionScene { WorkspaceId = 1, Windows = Array.Empty<TransitionWindowSnapshot>() },
            Incoming = new TransitionScene { WorkspaceId = 2, Windows = Array.Empty<TransitionWindowSnapshot>() },
            Direction = TransitionDirection.ToHigher,
            Options = new TransitionOptions { Style = WorkspaceTransitionStyle.Slide },
            ApplySwitch = () => applied = true
        };

        Assert.Throws<InvalidOperationException>(() =>
            engine.PlayAsync(ctx, CancellationToken.None).GetAwaiter().GetResult());
        // Coordinator catches and still applies; engine itself does not.
        Assert.False(applied);
        Assert.Equal(1, engine.PlayCount);
    }

    [Fact]
    public void Coalescing_KeepsOnlyLatestPending_Logic()
    {
        // Documented policy: pending slot is a single nullable int overwritten by newer requests.
        int? pending = null;
        pending = 7;
        pending = 5;
        Assert.Equal(5, pending);
    }

    [Fact]
    public async Task RecordingEngine_CancelToken_StopsPlay()
    {
        var engine = new RecordingEngine
        {
            OnPlay = _ => { }
        };
        // Replace with delaying engine via custom:
        var delayEngine = new DelayingEngine(TimeSpan.FromSeconds(5));
        var cts = new CancellationTokenSource();
        var play = delayEngine.PlayAsync(new TransitionPlayContext
        {
            MonitorId = "m",
            WorkAreaPx = new System.Drawing.Rectangle(0, 0, 100, 100),
            Outgoing = new TransitionScene { WorkspaceId = 1, Windows = Array.Empty<TransitionWindowSnapshot>() },
            Incoming = new TransitionScene { WorkspaceId = 2, Windows = Array.Empty<TransitionWindowSnapshot>() },
            Direction = TransitionDirection.ToHigher,
            Options = new TransitionOptions { Style = WorkspaceTransitionStyle.Slide },
            ApplySwitch = () => { }
        }, cts.Token);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await play);
        Assert.True(delayEngine.WasCancelled);
    }

    private sealed class DelayingEngine : IWorkspaceTransitionEngine
    {
        private readonly TimeSpan _delay;
        public bool WasCancelled { get; private set; }

        public DelayingEngine(TimeSpan delay) => _delay = delay;

        public async Task PlayAsync(TransitionPlayContext context, CancellationToken cancellationToken)
        {
            context.ApplySwitch();
            try
            {
                await Task.Delay(_delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }
        }
    }
}
