using Spaces4Win.Overview;

namespace Spaces4Win.Tests;

public sealed class OverviewSearchFilterTests
{
    [Fact]
    public void Empty_query_matches_all()
    {
        var info = new WindowThumbnailInfo
        {
            Hwnd = new IntPtr(1),
            WorkspaceId = 1,
            Title = "Rider",
            AppName = "rider64"
        };

        Assert.True(OverviewSearchFilter.Matches(info, null));
        Assert.True(OverviewSearchFilter.Matches(info, "   "));
    }

    [Fact]
    public void Matches_title_or_app_case_insensitive()
    {
        var info = new WindowThumbnailInfo
        {
            Hwnd = new IntPtr(1),
            WorkspaceId = 1,
            Title = "My Document — Notepad",
            AppName = "notepad"
        };

        Assert.True(OverviewSearchFilter.Matches(info, "note"));
        Assert.True(OverviewSearchFilter.Matches(info, "DOCUMENT"));
        Assert.False(OverviewSearchFilter.Matches(info, "chrome"));
    }
}
