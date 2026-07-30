using Spaces4Win.Services;

namespace Spaces4Win.Tests;

public class WindowLayoutMatcherTests
{
    [Fact]
    public void Match_PrefersHwndAndProcessId()
    {
        var layout = new WindowLayoutDocument
        {
            Monitors =
            {
                new LayoutMonitorEntry
                {
                    MonitorId = @"\\.\DISPLAY1",
                    Windows =
                    {
                        new LayoutWindowEntry
                        {
                            Hwnd = 42,
                            ProcessId = 100,
                            ProcessPath = @"C:\Apps\a.exe",
                            Title = "Old",
                            Workspace = 3
                        }
                    }
                }
            }
        };

        var open = new[]
        {
            new WindowIdentity
            {
                Hwnd = new IntPtr(42),
                ProcessId = 100,
                ProcessPath = @"C:\Apps\a.exe",
                Title = "New title",
                ClassName = "X"
            }
        };

        var matches = WindowLayoutMatcher.Match(layout, open);
        Assert.True(matches.ContainsKey(new IntPtr(42)));
        Assert.Equal(3, matches[new IntPtr(42)].Entry.Workspace);
    }

    [Fact]
    public void Match_FallsBackToPathAndTitle()
    {
        var layout = new WindowLayoutDocument
        {
            Monitors =
            {
                new LayoutMonitorEntry
                {
                    MonitorId = "M1",
                    Windows =
                    {
                        new LayoutWindowEntry
                        {
                            Hwnd = 1,
                            ProcessId = 1,
                            ProcessPath = @"C:\Apps\code.exe",
                            Title = "main.rs — Spaces4Win",
                            Workspace = 2
                        }
                    }
                }
            }
        };

        var open = new[]
        {
            new WindowIdentity
            {
                Hwnd = new IntPtr(99),
                ProcessId = 555,
                ProcessPath = @"C:\Apps\code.exe",
                Title = "main.rs — Spaces4Win",
                ClassName = "Chrome_WidgetWin_1"
            }
        };

        var matches = WindowLayoutMatcher.Match(layout, open);
        Assert.Equal(2, matches[new IntPtr(99)].Entry.Workspace);
    }

    [Fact]
    public void Match_UniquePath_OnlyWhenSingleCandidate()
    {
        var layout = new WindowLayoutDocument
        {
            Monitors =
            {
                new LayoutMonitorEntry
                {
                    MonitorId = "M1",
                    Windows =
                    {
                        new LayoutWindowEntry
                        {
                            ProcessPath = @"C:\Apps\note.exe",
                            Title = "A",
                            Workspace = 4
                        }
                    }
                }
            }
        };

        var single = new[]
        {
            new WindowIdentity
            {
                Hwnd = new IntPtr(7),
                ProcessPath = @"C:\Apps\note.exe",
                Title = "Other"
            }
        };
        Assert.Equal(4, WindowLayoutMatcher.Match(layout, single)[new IntPtr(7)].Entry.Workspace);

        var ambiguous = new[]
        {
            new WindowIdentity { Hwnd = new IntPtr(7), ProcessPath = @"C:\Apps\note.exe", Title = "A" },
            new WindowIdentity { Hwnd = new IntPtr(8), ProcessPath = @"C:\Apps\note.exe", Title = "B" }
        };
        // Title pass claims hwnd 7 for entry Title A; unique-path not needed.
        // Change titles so only unique-path could apply — two candidates → no match.
        layout.Monitors[0].Windows[0].Title = "Gone";
        var matches = WindowLayoutMatcher.Match(layout, ambiguous);
        Assert.Empty(matches);
    }

    [Fact]
    public void Match_DoesNotInventWindows_OnlyMapsOpenOnes()
    {
        var layout = new WindowLayoutDocument
        {
            Monitors =
            {
                new LayoutMonitorEntry
                {
                    MonitorId = "M1",
                    Windows =
                    {
                        new LayoutWindowEntry
                        {
                            ProcessPath = @"C:\Apps\closed.exe",
                            Title = "Closed",
                            Workspace = 5
                        }
                    }
                }
            }
        };

        var open = new[]
        {
            new WindowIdentity
            {
                Hwnd = new IntPtr(1),
                ProcessPath = @"C:\Apps\other.exe",
                Title = "Live"
            }
        };

        Assert.Empty(WindowLayoutMatcher.Match(layout, open));
    }
}
