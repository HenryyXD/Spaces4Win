using System.IO;
using Spaces4Win.Services;
using Spaces4Win.Services.Presets;

namespace Spaces4Win.Tests;

public class PresetStoreTests
{
    [Fact]
    public void SlotToDisplayNumber_MapsZeroToTen()
    {
        Assert.Equal(10, PresetStore.SlotToDisplayNumber(0));
        Assert.Equal(1, PresetStore.SlotToDisplayNumber(1));
        Assert.Equal(9, PresetStore.SlotToDisplayNumber(9));
    }

    [Fact]
    public void NormalizeSlot_AcceptsTenAsZero()
    {
        Assert.Equal(0, PresetStore.NormalizeSlot(10));
        Assert.Equal(0, PresetStore.NormalizeSlot(0));
        Assert.Equal(5, PresetStore.NormalizeSlot(5));
    }

    [Fact]
    public void SanitizeName_TrimsAndDefaults()
    {
        Assert.Equal("Preset 3", PresetStore.SanitizeName("  ", 3));
        Assert.Equal("Work", PresetStore.SanitizeName("  Work  ", 1));
        Assert.Equal(40, PresetStore.SanitizeName(new string('a', 50), 1).Length);
    }

    [Fact]
    public void Save_Rename_Clear_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Spaces4WinPresetTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Point store at temp via reflection on _path — or use public API with env.
            // PresetStore always uses AppData; exercise via instance after swapping path.
            var store = new PresetStore();
            var pathField = typeof(PresetStore).GetField("_path", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var tempPath = Path.Combine(dir, "presets.json");
            pathField.SetValue(store, tempPath);

            var layout = new WindowLayoutDocument
            {
                Monitors =
                {
                    new LayoutMonitorEntry
                    {
                        MonitorId = @"\\.\DISPLAY1",
                        ActiveWorkspace = 2,
                        Windows =
                        {
                            new LayoutWindowEntry
                            {
                                ProcessPath = @"C:\Apps\Demo.exe",
                                Title = "Demo",
                                Workspace = 2
                            }
                        }
                    }
                }
            };

            var saved = store.Save(1, layout);
            Assert.True(saved.HasContent);
            Assert.Equal("Preset 1", saved.Name);

            var renamed = store.Rename(1, "Trabalho");
            Assert.Equal("Trabalho", renamed.Name);

            var again = store.Save(1, layout, keepExistingName: true);
            Assert.Equal("Trabalho", again.Name);

            store.Clear(1);
            var cleared = store.TryGetSlot(1)!;
            Assert.False(cleared.HasContent);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}

public class PresetApplyPlannerTests
{
    [Fact]
    public void Build_KeepsMatches_ClosesExtras_LaunchesMissing()
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
                        new LayoutWindowEntry { ProcessPath = @"C:\A.exe", Title = "A1", Workspace = 1 },
                        new LayoutWindowEntry { ProcessPath = @"C:\B.exe", Title = "B1", Workspace = 2 }
                    }
                }
            }
        };

        var open = new List<WindowIdentity>
        {
            new() { Hwnd = new IntPtr(1), ProcessPath = @"C:\A.exe", Title = "A1" },
            new() { Hwnd = new IntPtr(99), ProcessPath = @"C:\Extra.exe", Title = "X" }
        };

        var plan = PresetApplyPlanner.Build(layout, open);
        Assert.Single(plan.KeepByHwnd);
        Assert.True(plan.KeepByHwnd.ContainsKey(new IntPtr(1)));
        Assert.Equal(new IntPtr(99), Assert.Single(plan.Close));
        Assert.Single(plan.Launch);
        Assert.Equal(@"C:\B.exe", plan.Launch[0].Entry.ProcessPath);
    }
}

public class HotkeyModalGatePresetTests
{
    [Fact]
    public void PresetBrowser_AllowsOwnRoleOnly()
    {
        Assert.True(HotkeyModalGate.IsAllowed(HotkeyModalKind.PresetBrowser, HotkeyRole.PresetBrowser));
        Assert.False(HotkeyModalGate.IsAllowed(HotkeyModalKind.PresetBrowser, HotkeyRole.General));
        Assert.False(HotkeyModalGate.IsAllowed(HotkeyModalKind.Overview, HotkeyRole.PresetBrowser));
    }
}
