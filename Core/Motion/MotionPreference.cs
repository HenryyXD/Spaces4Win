using System.Text.Json.Serialization;

namespace Spaces4Win.Core.Motion;

/// <summary>Global motion preference for the whole app.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MotionPreference
{
    /// <summary>Respect Windows reduce-motion / animation settings.</summary>
    FollowSystem = 0,

    /// <summary>Always allow Spaces4Win animations.</summary>
    Enabled = 1,

    /// <summary>Never animate; snap to end states.</summary>
    Disabled = 2
}
