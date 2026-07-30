using System.Text.Json.Serialization;

namespace Spaces4Win.Config;

/// <summary>How the floating indicator represents an active pinned (sticky) window.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PinnedWindowIndicatorMode
{
    /// <summary>Double ring on the active workspace dot only (default).</summary>
    RingOnly = 0,

    /// <summary>Double ring plus an accessibility “G” mark after the dots.</summary>
    RingPlusG = 1
}
