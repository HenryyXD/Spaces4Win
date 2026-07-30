namespace Spaces4Win.Overview;

/// <summary>Pure title/app filter helpers for overview keyboard search.</summary>
public static class OverviewSearchFilter
{
    public static bool Matches(WindowThumbnailInfo info, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var q = query.Trim();
        return Contains(info.Title, q) || Contains(info.AppName, q);
    }

    public static bool Contains(string? haystack, string needle) =>
        !string.IsNullOrEmpty(haystack) &&
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
