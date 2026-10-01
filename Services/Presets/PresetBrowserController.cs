using System.Windows.Threading;
using Spaces4Win.Core;
using Spaces4Win.Localization;
using Spaces4Win.Services;

namespace Spaces4Win.Services.Presets;

/// <summary>Owns Caps+P preset browser lifecycle and apply/save orchestration.</summary>
public sealed class PresetBrowserController : IDisposable
{
    private readonly WorkspaceManager _workspaceManager;
    private readonly MonitorTracker _monitorTracker;
    private readonly PresetStore _store;
    private readonly PresetApplyService _apply;
    private readonly Func<ILocalizationService>? _localization;
    private readonly Action<string?, string> _toast;
    private PresetBrowserOverlayWindow? _overlay;
    private bool _busy;
    private bool _disposed;

    public PresetBrowserController(
        WorkspaceManager workspaceManager,
        MonitorTracker monitorTracker,
        PresetStore store,
        PresetApplyService apply,
        Action<string?, string> toast,
        Func<ILocalizationService>? localization = null)
    {
        _workspaceManager = workspaceManager;
        _monitorTracker = monitorTracker;
        _store = store;
        _apply = apply;
        _toast = toast;
        _localization = localization;
    }

    public bool IsActive => _overlay is not null;

    public HotkeyModalKind ActiveModalKind =>
        IsActive ? HotkeyModalKind.PresetBrowser : HotkeyModalKind.None;

    public void Open(int? focusSlot = null)
    {
        if (_disposed)
        {
            return;
        }

        var monitor = _monitorTracker.FindMonitorUnderCursor()
                      ?? _monitorTracker.Monitors.FirstOrDefault();
        if (monitor is null)
        {
            return;
        }

        CloseOverlay();
        var slots = _store.LoadOrCreate().Slots;
        var overlay = new PresetBrowserOverlayWindow
        {
            Localization = _localization,
            OutsideClicked = CloseOverlay,
            LoadConfirmed = slot => _ = ApplySlotAsync(slot),
            RenameCommitted = (slot, name) => _store.Rename(slot, name)
        };
        _overlay = overlay;
        overlay.Closed += (_, _) =>
        {
            if (ReferenceEquals(_overlay, overlay))
            {
                _overlay = null;
            }
        };
        overlay.ShowForMonitor(monitor.Bounds, slots, focusSlot ?? 1);
    }

    public void CloseOverlay()
    {
        var overlay = _overlay;
        _overlay = null;
        try
        {
            overlay?.Close();
        }
        catch
        {
            // ignore
        }
    }

    public void SaveSlot(int slot)
    {
        if (_disposed || _busy)
        {
            return;
        }

        slot = PresetStore.NormalizeSlot(slot);
        var layout = _workspaceManager.CaptureLayout();
        var saved = _store.Save(slot, layout, keepExistingName: true);
        var loc = _localization?.Invoke();
        var msg = loc?.Format("Preset.Toast.Saved", saved.Name)
                  ?? $"“{saved.Name}” saved";
        _toast(_workspaceManager.ResolveMonitorIdForHotkeys(), msg);
    }

    public void OpenFocused(int slot) => Open(PresetStore.NormalizeSlot(slot));

    public bool TryHandleKey(uint vk, bool capsHeld)
    {
        if (!IsActive || _overlay is null)
        {
            return false;
        }

        var overlay = _overlay;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return HandleKeyOnUi(overlay, vk);
        }

        var handled = false;
        dispatcher.Invoke(() => handled = HandleKeyOnUi(overlay, vk));
        return handled;
    }

    private bool HandleKeyOnUi(PresetBrowserOverlayWindow overlay, uint vk)
    {
        // While renaming, only Esc is intercepted by us; typing goes to the TextBox.
        if (overlay.IsRenaming)
        {
            if (vk == Native.NativeMethods.VK_ESCAPE)
            {
                overlay.TryEscape();
                return true;
            }

            return false;
        }

        if (vk == Native.NativeMethods.VK_ESCAPE)
        {
            if (!overlay.TryEscape())
            {
                CloseOverlay();
            }

            return true;
        }

        if (vk == Native.NativeMethods.VK_RETURN)
        {
            overlay.TryConfirmOrLoad();
            return true;
        }

        if (vk == Native.NativeMethods.VK_F2)
        {
            overlay.TryBeginRename();
            return true;
        }

        if (vk == Native.NativeMethods.VK_UP)
        {
            overlay.MoveSelection(-1);
            return true;
        }

        if (vk == Native.NativeMethods.VK_DOWN)
        {
            overlay.MoveSelection(1);
            return true;
        }

        if (vk is >= Native.NativeMethods.VK_0 and <= Native.NativeMethods.VK_9)
        {
            overlay.SelectSlot((int)(vk - Native.NativeMethods.VK_0));
            return true;
        }

        if (vk is >= Native.NativeMethods.VK_NUMPAD0 and <= Native.NativeMethods.VK_NUMPAD9)
        {
            overlay.SelectSlot((int)(vk - Native.NativeMethods.VK_NUMPAD0));
            return true;
        }

        return false;
    }

    private async Task ApplySlotAsync(int slot)
    {
        if (_busy || _disposed)
        {
            return;
        }

        var preset = _store.TryGetSlot(slot);
        if (preset is null || !preset.HasContent)
        {
            return;
        }

        _busy = true;
        CloseOverlay();
        try
        {
            var result = await _apply.ApplyAsync(preset).ConfigureAwait(true);
            var loc = _localization?.Invoke();
            string msg;
            if (result.LaunchFailures > 0)
            {
                msg = loc?.Format("Preset.Toast.LoadedPartial", preset.Name, result.LaunchFailures)
                      ?? $"“{preset.Name}” loaded ({result.LaunchFailures} app(s) failed)";
            }
            else if (result.Launched > 0)
            {
                msg = loc?.Format("Preset.Toast.LoadedLaunched", preset.Name, result.Launched)
                      ?? $"“{preset.Name}” loaded ({result.Launched} app(s) opened)";
            }
            else
            {
                msg = loc?.Format("Preset.Toast.Loaded", preset.Name)
                      ?? $"“{preset.Name}” loaded";
            }

            _toast(_workspaceManager.ResolveMonitorIdForHotkeys(), msg);
        }
        catch (Exception ex)
        {
            _toast(_workspaceManager.ResolveMonitorIdForHotkeys(), ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CloseOverlay();
    }
}
