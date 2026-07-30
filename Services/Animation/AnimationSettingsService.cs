using Spaces4Win.Core.Motion;

namespace Spaces4Win.Services.Animation;

public interface ISystemMotionPreference
{
    /// <summary>True when the OS asks for reduced motion / disabled animations.</summary>
    bool IsReduceMotionPreferred { get; }
}

public interface IAnimationSettingsService
{
    MotionPreference Preference { get; }
    bool AnimationsEnabled { get; }
    event EventHandler? Changed;

    void Apply(MotionPreference preference);
    void RefreshSystemPreference();
}

/// <summary>Reads HKCU\Control Panel\Accessibility\Blind Access / SPI animations loosely.</summary>
public sealed class WindowsSystemMotionPreference : ISystemMotionPreference
{
    public bool IsReduceMotionPreferred
    {
        get
        {
            try
            {
                // ClientAnimation / MinAnimate under Control Panel\Desktop\WindowMetrics or Accessibility.
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Control Panel\Accessibility\Blind Access");
                if (key?.GetValue("On") is int on && on == 1)
                {
                    return true;
                }

                using var desktop = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop\WindowMetrics");
                if (desktop?.GetValue("MinAnimate") is string s && s == "0")
                {
                    return true;
                }
            }
            catch
            {
                // Fallback: allow animations.
            }

            return false;
        }
    }
}

public sealed class AnimationSettingsService : IAnimationSettingsService
{
    private readonly ISystemMotionPreference _system;
    private MotionPreference _preference = MotionPreference.FollowSystem;
    private bool _systemReduce;

    public AnimationSettingsService(ISystemMotionPreference? system = null)
    {
        _system = system ?? new WindowsSystemMotionPreference();
        _systemReduce = _system.IsReduceMotionPreferred;
    }

    public MotionPreference Preference => _preference;

    public bool AnimationsEnabled => _preference switch
    {
        MotionPreference.Enabled => true,
        MotionPreference.Disabled => false,
        _ => !_systemReduce
    };

    public event EventHandler? Changed;

    public void Apply(MotionPreference preference)
    {
        if (_preference == preference)
        {
            RefreshSystemPreference();
            return;
        }

        _preference = preference;
        RefreshSystemPreference();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshSystemPreference()
    {
        var next = _system.IsReduceMotionPreferred;
        if (next == _systemReduce && _preference != MotionPreference.FollowSystem)
        {
            return;
        }

        var before = AnimationsEnabled;
        _systemReduce = next;
        if (before != AnimationsEnabled)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
