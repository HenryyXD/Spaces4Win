using Spaces4Win.Config;
using Spaces4Win.Core;
using Spaces4Win.Services;

namespace Spaces4Win.Tests;

public sealed class FullscreenWorkspacePolicyTests
{
    [Fact]
    public void Prefer_lowest_free_id_when_under_max()
    {
        var ids = new[] { 1, 2, 3 };
        var target = FullscreenWorkspacePolicy.ResolveFullscreenTargetWorkspace(
            ids,
            _ => 1,
            _ => false);
        Assert.Equal(4, target);
    }

    [Fact]
    public void Prefer_empty_when_all_nine_exist()
    {
        var ids = Enumerable.Range(1, 9).ToArray();
        var counts = new Dictionary<int, int>
        {
            [1] = 2, [2] = 1, [3] = 0, [4] = 3, [5] = 1, [6] = 1, [7] = 1, [8] = 1, [9] = 1
        };
        var target = FullscreenWorkspacePolicy.ResolveFullscreenTargetWorkspace(
            ids,
            id => counts[id],
            _ => true);
        Assert.Equal(3, target);
    }

    [Fact]
    public void Prefer_without_fullscreen_when_none_empty()
    {
        var ids = Enumerable.Range(1, 9).ToArray();
        var counts = ids.ToDictionary(id => id, _ => 2);
        var target = FullscreenWorkspacePolicy.ResolveFullscreenTargetWorkspace(
            ids,
            id => counts[id],
            id => id != 7);
        Assert.Equal(7, target);
    }

    [Fact]
    public void Prefer_fewest_windows_when_all_have_fullscreen()
    {
        var ids = Enumerable.Range(1, 9).ToArray();
        var counts = new Dictionary<int, int>
        {
            [1] = 5, [2] = 4, [3] = 3, [4] = 1, [5] = 2, [6] = 2, [7] = 2, [8] = 2, [9] = 2
        };
        var target = FullscreenWorkspacePolicy.ResolveFullscreenTargetWorkspace(
            ids,
            id => counts[id],
            _ => true);
        Assert.Equal(4, target);
    }

    [Fact]
    public void CreateDefault_includes_fullscreen_and_free_workspace_hotkeys()
    {
        var fs = AppConfig.CreateDefaultFullscreenWorkspaceHotkey();
        Assert.True(fs.CapsLock);
        Assert.Equal(System.Windows.Input.ModifierKeys.None, fs.Modifiers);
        Assert.Equal(System.Windows.Input.Key.F, fs.Key);

        var move = AppConfig.CreateDefaultMoveToFreeWorkspaceHotkey();
        Assert.True(move.CapsLock);
        Assert.Equal(System.Windows.Input.ModifierKeys.Shift, move.Modifiers);
        Assert.Equal(System.Windows.Input.Key.F, move.Key);

        var follow = AppConfig.CreateDefaultMoveToFreeWorkspaceFollowHotkey();
        Assert.True(follow.CapsLock);
        Assert.Equal(System.Windows.Input.ModifierKeys.Control, follow.Modifiers);
        Assert.Equal(System.Windows.Input.Key.F, follow.Key);

        var cfg = AppConfig.CreateDefault();
        Assert.Equal(System.Windows.Input.Key.F, cfg.FullscreenWorkspaceHotkey.Key);
        Assert.Equal(System.Windows.Input.ModifierKeys.Shift, cfg.MoveToFreeWorkspaceHotkey.Modifiers);
        Assert.Equal(System.Windows.Input.ModifierKeys.Control, cfg.MoveToFreeWorkspaceFollowHotkey.Modifiers);
    }
}

public sealed class LayoutFullscreenPersistenceTests
{
    [Fact]
    public void LayoutWindowEntry_IsFullscreen_defaults_false()
    {
        var entry = new LayoutWindowEntry();
        Assert.False(entry.IsFullscreen);
    }

    [Fact]
    public void LayoutWindowEntry_IsFullscreen_roundtrips_json()
    {
        var doc = new WindowLayoutDocument
        {
            Monitors =
            {
                new LayoutMonitorEntry
                {
                    MonitorId = "\\\\.\\DISPLAY1",
                    Windows =
                    {
                        new LayoutWindowEntry
                        {
                            ProcessPath = @"C:\App\app.exe",
                            Title = "App",
                            Workspace = 2,
                            IsFullscreen = true
                        }
                    }
                }
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(doc, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
        var loaded = System.Text.Json.JsonSerializer.Deserialize<WindowLayoutDocument>(json, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        Assert.NotNull(loaded);
        Assert.True(loaded!.Monitors[0].Windows[0].IsFullscreen);
    }
}
