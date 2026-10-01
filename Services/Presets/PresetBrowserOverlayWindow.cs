using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spaces4Win.Localization;
using Spaces4Win.Native;
using Spaces4Win.Services.Presets;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfKey = System.Windows.Input.Key;
using WpfPanel = System.Windows.Controls.Panel;

namespace Spaces4Win.Services.Presets;

/// <summary>Caps+P session-preset browser: preview, rename, confirm load.</summary>
public sealed class PresetBrowserOverlayWindow : Window
{
    private readonly Border _shell;
    private readonly TextBlock _title;
    private readonly TextBlock _hint;
    private readonly StackPanel _list;
    private readonly List<SlotCard> _cards = new();
    private int _selectedIndex;
    private bool _confirming;
    private bool _renaming;
    private WpfTextBox? _renameBox;
    private System.Drawing.Rectangle _monitorBoundsPx;

    public Func<ILocalizationService>? Localization { get; set; }
    public Action? OutsideClicked { get; set; }
    public Action<int>? LoadConfirmed { get; set; }
    public Action<int, string>? RenameCommitted { get; set; }

    private sealed class SlotCard
    {
        public required int Slot { get; init; }
        public required Border Root { get; init; }
        public required TextBlock TitleBlock { get; init; }
        public required string Name { get; set; }
        public required bool HasContent { get; init; }
    }

    public PresetBrowserOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = MediaBrushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        // Keyboard is routed via the LL hook (like Caps+Tab / Overview) so we never
        // fight foreground apps for focus — except briefly during F2 rename.
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        // Near-invisible hit-test fill (Switcher pattern). A heavy dim often goes opaque
        // when layered transparency glitches — avoid a solid black “frame”.
        var backdrop = new Border
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(0x01, 0x00, 0x00, 0x00)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
            IsHitTestVisible = true
        };

        _title = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = MediaBrushes.White,
            Margin = new Thickness(0, 0, 0, 4)
        };
        _hint = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(MediaColor.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        _list = new StackPanel { Orientation = WpfOrientation.Vertical };

        var scroll = new ScrollViewer
        {
            Content = _list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 520,
            Focusable = false
        };

        _shell = new Border
        {
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20),
            Background = new SolidColorBrush(MediaColor.FromArgb(0xF2, 0x1C, 0x1E, 0x26)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Children = { _title, _hint, scroll }
            },
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            MaxWidth = 720,
            MinWidth = 480,
            IsHitTestVisible = true
        };
        _shell.MouseLeftButtonDown += (_, e) => e.Handled = true;

        var root = new Grid();
        root.Children.Add(backdrop);
        root.Children.Add(_shell);
        Content = root;

        backdrop.MouseLeftButtonDown += (_, _) => OutsideClicked?.Invoke();
        SourceInitialized += (_, _) => ApplyToolWindowStyle();
        Loaded += (_, _) => ApplyToolWindowStyle();
    }

    public bool IsRenaming => _renaming;

    public void ShowForMonitor(System.Drawing.Rectangle monitorBoundsPx, IReadOnlyList<PresetSlot> slots, int focusSlot)
    {
        _monitorBoundsPx = monitorBoundsPx;
        var loc = Localization?.Invoke();
        _title.Text = loc?.Get("Preset.Browser.Title") ?? "Session presets";
        _hint.Text = loc?.Get("Preset.Browser.Hint") ?? "Enter load · F2 rename · Esc cancel · 0–9 select";
        _confirming = false;
        _renaming = false;
        _renameBox = null;

        _list.Children.Clear();
        _cards.Clear();

        foreach (var slot in slots.OrderBy(s => PresetStore.SlotToDisplayNumber(s.Slot)))
        {
            _cards.Add(BuildCard(slot, loc));
        }

        var focusDisplay = PresetStore.SlotToDisplayNumber(PresetStore.NormalizeSlot(focusSlot));
        _selectedIndex = Math.Max(0, _cards.FindIndex(c => PresetStore.SlotToDisplayNumber(c.Slot) == focusDisplay));
        RefreshSelection();

        PositionOnMonitor();
        Show();
        ApplyToolWindowStyle();
        UpdateLayout();
        PositionOnMonitor();
        ApplyToolWindowStyle();
    }

    public int SelectedSlot =>
        _cards.Count == 0 ? 1 : _cards[Math.Clamp(_selectedIndex, 0, _cards.Count - 1)].Slot;

    public void MoveSelection(int delta)
    {
        if (_renaming || _cards.Count == 0)
        {
            return;
        }

        _confirming = false;
        _selectedIndex = (_selectedIndex + delta + _cards.Count) % _cards.Count;
        RefreshSelection();
    }

    public void SelectSlot(int slot)
    {
        if (_renaming)
        {
            return;
        }

        slot = PresetStore.NormalizeSlot(slot);
        var idx = _cards.FindIndex(c => c.Slot == slot);
        if (idx < 0)
        {
            return;
        }

        _confirming = false;
        _selectedIndex = idx;
        RefreshSelection();
    }

    public bool TryBeginRename()
    {
        if (_confirming || _cards.Count == 0)
        {
            return false;
        }

        var card = _cards[_selectedIndex];
        _renaming = true;
        var box = new WpfTextBox
        {
            Text = card.Name,
            FontSize = 14,
            MaxLength = PresetStore.MaxNameLength,
            Width = 280,
            Margin = new Thickness(0, 0, 0, 0)
        };
        _renameBox = box;
        card.TitleBlock.Visibility = Visibility.Collapsed;
        if (card.TitleBlock.Parent is WpfPanel panel)
        {
            var index = panel.Children.IndexOf(card.TitleBlock);
            panel.Children.Insert(index + 1, box);
        }

        box.SelectAll();
        Focusable = true;
        AllowActivationForRename();
        Activate();
        box.Focus();
        Keyboard.Focus(box);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == WpfKey.Enter)
            {
                CommitRename();
                e.Handled = true;
            }
            else if (e.Key == WpfKey.Escape)
            {
                CancelRename();
                e.Handled = true;
            }
        };
        return true;
    }

    public bool TryConfirmOrLoad()
    {
        if (_renaming)
        {
            CommitRename();
            return true;
        }

        if (_cards.Count == 0)
        {
            return false;
        }

        var card = _cards[_selectedIndex];
        if (!card.HasContent)
        {
            return true;
        }

        if (!_confirming)
        {
            _confirming = true;
            RefreshSelection();
            return true;
        }

        LoadConfirmed?.Invoke(card.Slot);
        return true;
    }

    public bool TryEscape()
    {
        if (_renaming)
        {
            CancelRename();
            return true;
        }

        if (_confirming)
        {
            _confirming = false;
            RefreshSelection();
            return true;
        }

        return false;
    }

    private SlotCard BuildCard(PresetSlot slot, ILocalizationService? loc)
    {
        var display = PresetStore.SlotToDisplayNumber(slot.Slot);
        var name = string.IsNullOrWhiteSpace(slot.Name) ? PresetStore.DefaultName(slot.Slot) : slot.Name;
        var emptyLabel = loc?.Get("Preset.Browser.Empty") ?? "Empty";
        var apps = SummarizeApps(slot);

        var title = new TextBlock
        {
            Text = $"#{display} · {name}",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = MediaBrushes.White
        };

        var meta = new TextBlock
        {
            Text = slot.HasContent
                ? (slot.UpdatedAt.ToLocalTime().ToString("g") + (apps.Count > 0 ? " — " + string.Join(", ", apps.Take(6)) : ""))
                : emptyLabel,
            FontSize = 12,
            Foreground = new SolidColorBrush(MediaColor.FromArgb(0xBB, 0xFF, 0xFF, 0xFF)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var icons = new StackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var path in DistinctPaths(slot).Take(8))
        {
            var img = TryLoadIcon(path);
            if (img is null)
            {
                continue;
            }

            icons.Children.Add(new System.Windows.Controls.Image
            {
                Source = img,
                Width = 20,
                Height = 20,
                Margin = new Thickness(0, 0, 6, 0)
            });
        }

        var body = new StackPanel();
        body.Children.Add(title);
        body.Children.Add(meta);
        if (icons.Children.Count > 0)
        {
            body.Children.Add(icons);
        }

        var root = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Background = new SolidColorBrush(MediaColor.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            Child = body,
            Cursor = System.Windows.Input.Cursors.Hand
        };

        var card = new SlotCard
        {
            Slot = slot.Slot,
            Root = root,
            TitleBlock = title,
            Name = name,
            HasContent = slot.HasContent
        };

        root.MouseLeftButtonDown += (_, e) =>
        {
            _selectedIndex = _cards.IndexOf(card);
            if (e.ClickCount >= 2 && IsOverTitle(e, title))
            {
                TryBeginRename();
            }
            else
            {
                _confirming = false;
                RefreshSelection();
                TryConfirmOrLoad();
            }

            e.Handled = true;
        };

        _list.Children.Add(root);
        return card;
    }

    private void RefreshSelection()
    {
        var loc = Localization?.Invoke();
        for (var i = 0; i < _cards.Count; i++)
        {
            var card = _cards[i];
            var selected = i == _selectedIndex;
            card.Root.BorderThickness = new Thickness(selected ? 2 : 0);
            card.Root.BorderBrush = selected
                ? new SolidColorBrush(MediaColor.FromArgb(0xFF, 0x6C, 0xAE, 0xFF))
                : MediaBrushes.Transparent;
            card.Root.Background = new SolidColorBrush(
                selected
                    ? MediaColor.FromArgb(0x55, 0x6C, 0xAE, 0xFF)
                    : MediaColor.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

            if (selected && _confirming && card.HasContent)
            {
                _hint.Text = loc?.Format("Preset.Browser.Confirm", card.Name)
                             ?? $"Load “{card.Name}”? Enter confirm · Esc cancel (closes apps not in setup)";
            }
            else if (!_renaming)
            {
                _hint.Text = loc?.Get("Preset.Browser.Hint")
                             ?? "Enter load · F2 rename · Esc cancel · 0–9 select";
            }
        }

        if (_selectedIndex >= 0 && _selectedIndex < _cards.Count)
        {
            _cards[_selectedIndex].Root.BringIntoView();
        }
    }

    private void CommitRename()
    {
        if (!_renaming || _renameBox is null || _cards.Count == 0)
        {
            return;
        }

        var card = _cards[_selectedIndex];
        var name = PresetStore.SanitizeName(_renameBox.Text, card.Slot);
        card.Name = name;
        card.TitleBlock.Text = $"#{PresetStore.SlotToDisplayNumber(card.Slot)} · {name}";
        CancelRenameUiOnly();
        RenameCommitted?.Invoke(card.Slot, name);
        RefreshSelection();
    }

    private void CancelRename()
    {
        CancelRenameUiOnly();
        RefreshSelection();
    }

    private void CancelRenameUiOnly()
    {
        if (_renameBox?.Parent is WpfPanel panel)
        {
            panel.Children.Remove(_renameBox);
        }

        if (_cards.Count > 0)
        {
            _cards[_selectedIndex].TitleBlock.Visibility = Visibility.Visible;
        }

        _renameBox = null;
        _renaming = false;
        Focusable = false;
        ApplyToolWindowStyle();
    }

    private void PositionOnMonitor()
    {
        // Full-monitor transparent hit area; card is centered inside (Switcher pattern).
        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
        var scaleY = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;
        Left = _monitorBoundsPx.Left / scaleX;
        Top = _monitorBoundsPx.Top / scaleY;
        Width = Math.Max(_monitorBoundsPx.Width / scaleX, 1);
        Height = Math.Max(_monitorBoundsPx.Height / scaleY, 1);
        MinWidth = Width;
        MinHeight = Height;
    }

    private void ApplyToolWindowStyle()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var ex = NativeMethods.GetWindowLongPtrCompat(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        ex &= ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtrCompat(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(unchecked((int)ex)));
        Topmost = true;
        const uint flags = NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, flags);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, flags);
    }

    private void AllowActivationForRename()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var ex = NativeMethods.GetWindowLongPtrCompat(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TOOLWINDOW;
        ex &= ~NativeMethods.WS_EX_NOACTIVATE;
        ex &= ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtrCompat(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(unchecked((int)ex)));
    }

    private static bool IsOverTitle(MouseButtonEventArgs e, TextBlock title)
    {
        var pos = e.GetPosition(title);
        return pos.X >= 0 && pos.Y >= 0 && pos.X <= title.ActualWidth && pos.Y <= title.ActualHeight;
    }

    private static List<string> SummarizeApps(PresetSlot slot)
    {
        return DistinctPaths(slot)
            .Select(p => Path.GetFileNameWithoutExtension(p))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList()!;
    }

    private static IEnumerable<string> DistinctPaths(PresetSlot slot) =>
        slot.Layout.Monitors
            .SelectMany(m => m.Windows)
            .Select(w => w.ProcessPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static ImageSource? TryLoadIcon(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var icon = global::System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            return Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(20, 20));
        }
        catch
        {
            return null;
        }
    }
}
