using System.Text.Json.Serialization;

namespace Spaces4Win.Config;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkspaceTransitionStyle
{
    Slide = 0,
    Fade = 1,
    None = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkspaceTransitionSpeed
{
    Fast = 0,
    Normal = 1,
    Smooth = 2
}

public static class WorkspaceTransitionSpeedExtensions
{
    public static TimeSpan ToDuration(this WorkspaceTransitionSpeed speed) => speed switch
    {
        WorkspaceTransitionSpeed.Fast => TimeSpan.FromMilliseconds(100),
        WorkspaceTransitionSpeed.Smooth => TimeSpan.FromMilliseconds(180),
        _ => TimeSpan.FromMilliseconds(130)
    };

    /// <summary>How long the HUD stays fully visible before fading out.</summary>
    public static int HudHoldMilliseconds(this WorkspaceTransitionSpeed speed) => speed switch
    {
        WorkspaceTransitionSpeed.Fast => 90,
        WorkspaceTransitionSpeed.Smooth => 180,
        _ => 120
    };
}
