using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Spaces4Win.Core;
using Spaces4Win.Localization;
using Spaces4Win.Native;
using Spaces4Win.Overview;
using Spaces4Win.Services;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using WpfKey = System.Windows.Input.Key;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;
using WpfTextCompositionEventArgs = System.Windows.Input.TextCompositionEventArgs;
using WpfCursors = System.Windows.Input.Cursors;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace Spaces4Win;

/// <summary>
/// Lightweight per-monitor overview: one section per workspace, fixed-size WrapPanel cards, DWM once.
/// Manual drag between workspaces/monitors; drop on empty gaps to create the next free workspace id.
/// </summary>
public sealed class OverviewWindow : Window
{
    public const string DragDataFormat = "Spaces4Win.OverviewHwnd";

    private const double OuterMargin = 28;
    private const double CardWidth = 280;
    private const double CardHeight = 196;
    private const double CardMargin = 12;
    private const double HeaderBand = 34;
    private const double CardInnerPad = 10;
    private const double GapDropHeight = 22;
    private const double DimmedCardOpacity = 0.35;
    private const double FilteredOutOpacity = 0.28;
    private const byte FilteredThumbOpacity = 72;
    private const double SelectedCardScale = 1.08;
    private static readonly TimeSpan SelectionScaleDuration = TimeSpan.FromMilliseconds(150);
    private const double ThumbCornerRadius = 12;
    private const double ThumbInset = 5;
    /// <summary>DIP band at top/bottom of ScrollViewer that auto-scrolls during card drag.</summary>
    private const double DragAutoScrollEdgeDip = 56;
    private const double DragAutoScrollMaxPxPerTick = 28;

    private static readonly SolidColorBrush AccentBrush =
        new(MediaColor.FromRgb(0x4C, 0xC2, 0xFF));

    private static readonly SolidColorBrush DefaultCardBorderBrush =
        new(MediaColor.FromArgb(0x55, 0xFF, 0xFF, 0xFF));

    private readonly string _monitorId;
    private readonly System.Drawing.Rectangle _monitorBounds;
    private readonly WorkspaceManager _workspaceManager;
    private readonly OverviewService _overviewService;
    private readonly ILocalizationService _loc;
    private readonly Action? _requestRefresh;

    private OverviewThumbnailService? _thumbs;
    private readonly List<(Border Card, WindowThumbnailInfo Info)> _cards = new();
    private bool _closing;
    private bool _reloading;
    private ScrollViewer? _scroll;
    private DockPanel? _root;
    private System.Windows.Controls.TextBox? _searchBox;

    private WpfPoint _pressPoint;
    private Border? _pressCard;
    private bool _suppressClick;

    private string _searchQuery = "";
    private int _selectedMatchIndex = -1;
    private bool _suppressSearchBroadcast;

    private bool _manualDragActive;
    private IntPtr _dragHwnd;
    private Border? _dragSourceCard;
    private double _dragSourceOpacity = 1;
    private OverviewDragGhostWindow? _dragGhost;
    private DispatcherTimer? _dragAutoScrollTimer;
    private double _dragAutoScrollDelta;

    private Border? _highlightedDropTarget;
    private FrameworkElement? _dropPlaceholderHost;
    private int _dropPlaceholderIndex = -1;
    private int _thumbSyncDepth;

    private sealed class CardTag
    {
        public required WindowThumbnailInfo Info { get; init; }
        public required int WorkspaceId { get; init; }
        public required Border PreviewHost { get; init; }
        public required Border ThumbFrame { get; init; }
        public bool DwmLive { get; set; }
    }

    public sealed class DropTargetTag
    {
        public int? ExistingWorkspaceId { get; init; }
        public int? GapAboveId { get; init; }
        public int? GapBelowId { get; init; }
        public bool IsActiveWorkspace { get; init; }
        public int? WrapInsertIndex { get; init; }
    }

    public string MonitorId => _monitorId;
    public System.Drawing.Rectangle MonitorBounds => _monitorBounds;

    public string SearchQuery => _searchQuery;

    public int MatchingCardCount => GetMatchingCards().Count;

    /// <summary>
    /// Matching cards grouped by workspace id (overview section order), with the
    /// selection index used by <see cref="SetSelectedMatchIndex"/>.
    /// </summary>
    public IReadOnlyList<(int WorkspaceId, int SelectionIndex)> GetMatchingNavEntries()
    {
        var matching = GetMatchingCards();
        var list = new List<(int WorkspaceId, int SelectionIndex)>(matching.Count);
        for (var i = 0; i < matching.Count; i++)
        {
            var ws = matching[i].Card.Tag is CardTag tag ? tag.WorkspaceId : 0;
            list.Add((ws, i));
        }

        return list;
    }

    public int SelectedMatchIndex => _selectedMatchIndex;

    public bool SearchBoxFocused => _searchBox?.IsKeyboardFocusWithin == true;

    public IntPtr NativeHandle
    {
        get
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            return hwnd == IntPtr.Zero ? new WindowInteropHelper(this).EnsureHandle() : hwnd;
        }
    }

    public OverviewWindow(
        string monitorId,
        System.Drawing.Rectangle monitorBounds,
        WorkspaceManager workspaceManager,
        OverviewService overviewService,
        ILocalizationService loc,
        Action? requestRefresh = null)
    {
        _monitorId = monitorId;
        _monitorBounds = monitorBounds;
        _workspaceManager = workspaceManager;
        _overviewService = overviewService;
        _loc = loc;
        _requestRefresh = requestRefresh;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(MediaColor.FromArgb(0xB8, 0x10, 0x12, 0x18));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Focusable = true;

        KeyDown += OnKeyDown;
        PreviewTextInput += OnPreviewTextInput;
        Loaded += OnLoaded;
        Closed += OnClosedHandler;
    }

    public bool ContainsScreenPoint(WpfPoint screenPx)
    {
        try
        {
            var local = PointFromScreen(screenPx);
            return local.X >= 0 &&
                   local.Y >= 0 &&
                   local.X <= ActualWidth &&
                   local.Y <= ActualHeight;
        }
        catch
        {
            return false;
        }
    }

    public DropTargetTag? TryHitDropTarget(WpfPoint screenPx)
    {
        if (_scroll?.Content is not FrameworkElement body)
        {
            return null;
        }

        WpfPoint local;
        try
        {
            local = body.PointFromScreen(screenPx);
        }
        catch
        {
            return null;
        }

        var hit = VisualTreeHelper.HitTest(body, local)?.VisualHit;
        if (hit is null)
        {
            return null;
        }

        return ResolveDropTargetFromVisual(hit, local, body);
    }

    public void ApplyExternalDrop(IntPtr hwnd, DropTargetTag tag) => ApplyDrop(hwnd, tag);

    public void ClearDropHighlight()
    {
        if (_highlightedDropTarget is not null)
        {
            HighlightDropTarget(_highlightedDropTarget, on: false);
            _highlightedDropTarget = null;
        }

        RemoveDropPlaceholder();
    }

    public void SetSearchQuery(string? query, bool notifyService = true)
    {
        var next = query ?? "";
        if (string.Equals(_searchQuery, next, StringComparison.Ordinal) &&
            (_searchBox is null || string.Equals(_searchBox.Text, next, StringComparison.Ordinal)))
        {
            ApplySearchFilter();
            return;
        }

        _suppressSearchBroadcast = true;
        try
        {
            _searchQuery = next;
            if (_searchBox is not null)
            {
                _searchBox.Text = _searchQuery;
                _searchBox.Visibility = string.IsNullOrEmpty(_searchQuery)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            ApplySearchFilter();
        }
        finally
        {
            _suppressSearchBroadcast = false;
        }

        if (notifyService)
        {
            _overviewService.BroadcastSearchQuery(_searchQuery, this);
        }
    }

    public void ShowSearchBox(bool focus, bool selectAll = false)
    {
        if (_searchBox is null)
        {
            return;
        }

        _searchBox.Visibility = Visibility.Visible;
        if (focus)
        {
            ClaimKeyboardFocus();
            _searchBox.Focus();
            if (selectAll)
            {
                _searchBox.SelectAll();
            }
        }
    }

    public void ClaimKeyboardFocus()
    {
        var hwnd = NativeHandle;
        if (hwnd == IntPtr.Zero)
        {
            hwnd = new WindowInteropHelper(this).EnsureHandle();
        }

        NativeMethods.TryActivateWindow(hwnd);
        Activate();
        Focus();
    }

    public void SwitchOrCreateWorkspaceFromKeyboard(int workspaceId)
    {
        _workspaceManager.SwitchOrCreateWorkspace(_monitorId, workspaceId);
        Close();
    }

    public void ClearSearchLocal()
    {
        _suppressSearchBroadcast = true;
        try
        {
            _searchQuery = "";
            if (_searchBox is not null)
            {
                _searchBox.Text = "";
                _searchBox.Visibility = Visibility.Collapsed;
            }

            ApplySearchFilter();
        }
        finally
        {
            _suppressSearchBroadcast = false;
        }
    }

    public void SetSelectedMatchIndex(int index)
    {
        _selectedMatchIndex = index;
        ApplySearchFilter();
    }

    public int GetCardColumnCount() => EstimateCardColumns();

    private int EstimateCardColumns()
    {
        var width = _scroll?.ViewportWidth > 8
            ? _scroll.ViewportWidth
            : Math.Max(Width - OuterMargin * 2, CardWidth);
        var stride = CardWidth + CardMargin;
        return Math.Max(1, (int)Math.Floor(width / stride));
    }

    public bool TryActivateSelected()
    {
        var matching = GetMatchingCards();
        if (_selectedMatchIndex < 0 || _selectedMatchIndex >= matching.Count)
        {
            return false;
        }

        if (matching[_selectedMatchIndex].Card.Tag is CardTag tag)
        {
            SelectWindow(tag.Info.Hwnd, tag.WorkspaceId);
            return true;
        }

        return false;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = _monitorBounds.Left / dpi.DpiScaleX;
        Top = _monitorBounds.Top / dpi.DpiScaleY;
        Width = _monitorBounds.Width / dpi.DpiScaleX;
        Height = _monitorBounds.Height / dpi.DpiScaleY;

        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        _thumbs = new OverviewThumbnailService(hwnd);

        _root = new DockPanel { Margin = new Thickness(OuterMargin) };
        var header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        _root.Children.Add(header);

        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = BuildBody()
        };
        _scroll.ScrollChanged += (_, _) => UpdateAllThumbnails();
        _root.Children.Add(_scroll);
        Content = _root;

        SizeChanged += (_, _) => UpdateAllThumbnails();

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            ScheduleThumbnailRefresh();
            Focus();
        });
    }

    public void Reload()
    {
        if (_closing || _scroll is null || _reloading)
        {
            return;
        }

        _reloading = true;
        try
        {
            ClearDropHighlight();
            _thumbs?.Dispose();
            _cards.Clear();
            WindowIconHelper.ClearCache();
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                _thumbs = new OverviewThumbnailService(hwnd);
            }

            var offset = _scroll.VerticalOffset;
            _scroll.Content = BuildBody();
            ApplySearchFilter();
            Dispatcher.BeginInvoke(() =>
            {
                _scroll?.ScrollToVerticalOffset(offset);
                ScheduleThumbnailRefresh();
            }, DispatcherPriority.Loaded);
        }
        finally
        {
            _reloading = false;
        }
    }

    private FrameworkElement BuildHeader()
    {
        var header = new DockPanel { Height = 48, Margin = new Thickness(0, 0, 0, 16) };
        var esc = new TextBlock
        {
            Text = _loc.Get("Overview.EscHint"),
            FontSize = 13,
            Opacity = 0.7,
            Foreground = MediaBrushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(esc, Dock.Right);
        header.Children.Add(esc);

        _searchBox = new System.Windows.Controls.TextBox
        {
            Visibility = Visibility.Collapsed,
            MinWidth = 180,
            MaxWidth = 320,
            Margin = new Thickness(16, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
            Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(MediaColor.FromArgb(0x88, 0x20, 0x22, 0x2A)),
            Foreground = MediaBrushes.White,
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CaretBrush = MediaBrushes.White
        };
        _searchBox.TextChanged += (_, _) =>
        {
            _searchQuery = _searchBox.Text ?? "";
            ApplySearchFilter();
            if (!_suppressSearchBroadcast)
            {
                _overviewService.BroadcastSearchQuery(_searchQuery, this);
            }
        };
        _searchBox.KeyDown += OnSearchBoxKeyDown;
        DockPanel.SetDock(_searchBox, Dock.Right);
        header.Children.Add(_searchBox);

        header.Children.Add(new TextBlock
        {
            Text = _loc.Get("Overview.Title"),
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = MediaBrushes.White,
            VerticalAlignment = VerticalAlignment.Center
        });
        return header;
    }

    private FrameworkElement BuildBody()
    {
        var snapshot = OverviewSnapshotBuilder.Build(_workspaceManager, _monitorId);
        var host = new StackPanel();
        var ids = snapshot.Rows.Select(r => r.WorkspaceId).ToList();

        host.Children.Add(CreateGapDropZone(
            aboveId: null,
            belowId: ids.Count > 0 ? ids[0] : null));

        for (var i = 0; i < snapshot.Rows.Count; i++)
        {
            var row = snapshot.Rows[i];
            host.Children.Add(CreateWorkspaceSection(row));

            var below = i + 1 < ids.Count ? ids[i + 1] : (int?)null;
            host.Children.Add(CreateGapDropZone(aboveId: row.WorkspaceId, belowId: below));
        }

        return host;
    }

    private Border CreateGapDropZone(int? aboveId, int? belowId)
    {
        return new Border
        {
            Height = GapDropHeight,
            Margin = new Thickness(0, 0, 0, 4),
            CornerRadius = new CornerRadius(8),
            Background = MediaBrushes.Transparent,
            Tag = new DropTargetTag
            {
                ExistingWorkspaceId = null,
                GapAboveId = aboveId,
                GapBelowId = belowId
            },
            ToolTip = _loc.Get("Overview.DropCreateHint")
        };
    }

    private Border CreateWorkspaceSection(WorkspaceRowSnapshot row)
    {
        var section = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(row.IsActive ? 2 : 1),
            BorderBrush = row.IsActive
                ? AccentBrush
                : new SolidColorBrush(MediaColor.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            Background = new SolidColorBrush(MediaColor.FromArgb(0x55, 0x18, 0x1C, 0x24)),
            Padding = new Thickness(14, 12, 14, 14),
            Margin = new Thickness(0, 0, 0, 4),
            Tag = new DropTargetTag { ExistingWorkspaceId = row.WorkspaceId, IsActiveWorkspace = row.IsActive }
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = row.IsActive
                ? _loc.Format("Overview.WorkspaceActive", row.WorkspaceId)
                : _loc.Format("Overview.Workspace", row.WorkspaceId),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = MediaBrushes.White,
            Margin = new Thickness(0, 0, 0, 10)
        });

        if (row.Windows.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = _loc.Get("Overview.EmptyWorkspace"),
                Foreground = new SolidColorBrush(MediaColor.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                FontSize = 13,
                Margin = new Thickness(2, 4, 0, 4),
                Cursor = WpfCursors.Hand,
                Tag = row.WorkspaceId
            };
            empty.MouseLeftButtonUp += OnEmptyWorkspaceClick;
            stack.Children.Add(empty);
        }
        else
        {
            var wrap = new WrapPanel { Tag = row.WorkspaceId };
            foreach (var info in row.Windows)
            {
                wrap.Children.Add(CreateCard(info, row.WorkspaceId));
            }

            stack.Children.Add(wrap);
        }

        section.Child = stack;
        return section;
    }

    private Border CreateCard(WindowThumbnailInfo info, int workspaceId)
    {
        var icon = WindowIconHelper.GetIcon(info.Hwnd);
        var previewHost = new Border
        {
            Background = new SolidColorBrush(MediaColor.FromArgb(0x44, 0x1A, 0x1E, 0x28)),
            CornerRadius = new CornerRadius(Math.Max(0, ThumbCornerRadius - ThumbInset)),
            ClipToBounds = true,
            Child = BuildThumbFallback(icon)
        };

        // Rounded frame: DWM paints rectangular above WPF, so inset the thumb —
        // the frame background shows through as rounded corners. Fallback icon
        // keeps the same rounded look when DWM fails.
        var thumbFrame = new Border
        {
            CornerRadius = new CornerRadius(ThumbCornerRadius),
            Background = new SolidColorBrush(MediaColor.FromArgb(0x88, 0x12, 0x16, 0x20)),
            BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(ThumbInset),
            ClipToBounds = true,
            Margin = new Thickness(0, 2, 0, 0),
            Child = previewHost
        };
        ApplyRoundedClip(thumbFrame, ThumbCornerRadius);
        thumbFrame.SizeChanged += (_, _) => ApplyRoundedClip(thumbFrame, ThumbCornerRadius);
        previewHost.SizeChanged += (_, _) =>
            ApplyRoundedClip(previewHost, Math.Max(0, ThumbCornerRadius - ThumbInset));

        FrameworkElement previewContent = thumbFrame;

        if (info.IsMinimized)
        {
            var minimizedLabel = new TextBlock
            {
                Text = _loc.Get("Overview.Minimized"),
                Foreground = new SolidColorBrush(MediaColor.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
            var previewGrid = new Grid();
            previewGrid.Children.Add(thumbFrame);
            previewGrid.Children.Add(minimizedLabel);
            previewContent = previewGrid;
        }

        var card = new Border
        {
            Width = CardWidth,
            Height = CardHeight,
            Margin = new Thickness(0, 0, CardMargin, CardMargin),
            CornerRadius = new CornerRadius(12),
            BorderBrush = info.IsSticky
                ? new SolidColorBrush(MediaColor.FromRgb(0x4C, 0xC2, 0xFF))
                : DefaultCardBorderBrush,
            BorderThickness = new Thickness(info.IsSticky ? 2 : 1),
            Background = new SolidColorBrush(MediaColor.FromArgb(0xA8, 0x28, 0x2A, 0x30)),
            Cursor = WpfCursors.Hand,
            // Allow selection scale to draw outside the layout slot.
            ClipToBounds = false,
            Padding = new Thickness(CardInnerPad, 8, CardInnerPad, CardInnerPad),
            RenderTransformOrigin = new WpfPoint(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
            Tag = new CardTag
            {
                Info = info,
                WorkspaceId = workspaceId,
                PreviewHost = previewHost,
                ThumbFrame = thumbFrame
            }
        };

        var root = new DockPanel { LastChildFill = true };
        var header = BuildCardHeader(info);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(previewContent);
        card.Child = root;

        card.PreviewMouseLeftButtonDown += OnCardPreviewMouseDown;
        card.PreviewMouseMove += OnCardPreviewMouseMove;
        card.MouseLeftButtonUp += OnCardClick;
        _cards.Add((card, info));
        return card;
    }

    private static void ApplyRoundedClip(FrameworkElement element, double radius)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return;
        }

        element.Clip = new RectangleGeometry(
            new WpfRect(0, 0, element.ActualWidth, element.ActualHeight),
            radius,
            radius);
    }

    private static UIElement BuildThumbFallback(ImageSource? icon)
    {
        if (icon is null)
        {
            return new TextBlock
            {
                Text = "—",
                FontSize = 36,
                Foreground = new SolidColorBrush(MediaColor.FromArgb(0x66, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        return new System.Windows.Controls.Image
        {
            Source = icon,
            Width = 56,
            Height = 56,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.92
        };
    }

    private static void EnsureThumbFallback(Border previewHost, IntPtr hwnd)
    {
        previewHost.Child = BuildThumbFallback(WindowIconHelper.GetIcon(hwnd));
    }

    /// <summary>
    /// Cloaked / SW_HIDE windows often register a DWM thumb with a valid size but paint black
    /// (sharp rect). Prefer a WPF Image (PrintWindow or icon) so CornerRadius/Clip apply.
    /// </summary>
    private static bool PreferStaticThumb(IntPtr hwnd) =>
        WindowClassifier.IsCloaked(hwnd) || !NativeMethods.IsWindowVisible(hwnd);

    private void ApplyStaticThumb(Border host, CardTag tag, IntPtr hwnd, bool matches, byte thumbOpacity)
    {
        _thumbs?.Unregister(hwnd);
        tag.DwmLive = false;

        // Prefer the background-captured bitmap from the global cache (populated when the
        // window was hidden into an inactive workspace — no blocking/sleep on the UI thread).
        var cache = _workspaceManager.CaptureCache;
        ImageSource? capture = cache?.Get(hwnd);

        // Fallback for visible windows that failed DWM: capture directly (they are not cloaked).
        if (capture is null && !PreferStaticThumb(hwnd))
        {
            capture = OverviewWindowCapture.TryCapture(hwnd);
        }

        // If the cache has no entry yet (window hidden after overview opened), request one
        // and show the icon for now; the next ScheduleThumbnailRefresh beat will pick it up.
        if (capture is null && cache is not null && PreferStaticThumb(hwnd))
        {
            cache.RequestCapture(hwnd, completedHwnd =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (!_closing)
                    {
                        UpdateAllThumbnails();
                    }
                });
            });
        }

        ImageSource? source = capture ?? WindowIconHelper.GetIcon(hwnd);
        if (source is null)
        {
            EnsureThumbFallback(host, hwnd);
        }
        else if (capture is not null)
        {
            host.Child = new System.Windows.Controls.Image
            {
                Source = source,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true
            };
        }
        else
        {
            host.Child = BuildThumbFallback(source);
        }

        host.Opacity = matches ? 1.0 : thumbOpacity / 255.0;
        ApplyRoundedClip(host, Math.Max(0, ThumbCornerRadius - ThumbInset));
        ApplyRoundedClip(tag.ThumbFrame, ThumbCornerRadius);
    }

    private FrameworkElement BuildCardHeader(WindowThumbnailInfo info)
    {
        var header = new Grid { Height = HeaderBand - 4, Margin = new Thickness(0, 0, 0, 6) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = WindowIconHelper.GetIcon(info.Hwnd);
        if (icon is not null)
        {
            var image = new System.Windows.Controls.Image
            {
                Source = icon,
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true
            };
            Grid.SetColumn(image, 0);
            header.Children.Add(image);
        }

        var title = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(info.Title) ? _loc.Get("Overview.AppName") : info.Title,
            Foreground = MediaBrushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            ToolTip = info.Title
        };
        Grid.SetColumn(title, 1);
        header.Children.Add(title);

        var close = new System.Windows.Controls.Button
        {
            Content = "\uE711",
            FontFamily = new WpfFontFamily("Segoe MDL2 Assets"),
            FontSize = 10,
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Margin = new Thickness(6, 0, -4, 0),
            Cursor = WpfCursors.Hand,
            ToolTip = _loc.Get("Overview.CloseWindow"),
            Background = MediaBrushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(MediaColor.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false
        };
        close.Style = CreateCloseButtonStyle();
        close.Click += (_, e) =>
        {
            e.Handled = true;
            _pressCard = null;
            _suppressClick = false;
            CloseManagedWindow(info.Hwnd);
        };
        close.PreviewMouseLeftButtonDown += (_, e) => e.Handled = true;
        Grid.SetColumn(close, 2);
        header.Children.Add(close);

        return header;
    }

    private static Style CreateCloseButtonStyle()
    {
        var style = new Style(typeof(System.Windows.Controls.Button));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, MediaBrushes.Transparent));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty,
            new SolidColorBrush(MediaColor.FromArgb(0xCC, 0xFF, 0xFF, 0xFF))));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, WpfCursors.Hand));

        var template = new ControlTemplate(typeof(System.Windows.Controls.Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.Name = "Bd";
        borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(System.Windows.Controls.Control.BackgroundProperty));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        contentFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, WpfHorizontalAlignment.Center);
        contentFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(contentFactory);
        template.VisualTree = borderFactory;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty,
            new SolidColorBrush(MediaColor.FromArgb(0x33, 0xFF, 0xFF, 0xFF))));
        hover.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, MediaBrushes.White));
        template.Triggers.Add(hover);

        var pressed = new Trigger
        {
            Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty,
            Value = true
        };
        pressed.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty,
            new SolidColorBrush(MediaColor.FromRgb(0xC4, 0x2B, 0x1C))));
        pressed.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, MediaBrushes.White));
        template.Triggers.Add(pressed);

        style.Setters.Add(new Setter(System.Windows.Controls.Control.TemplateProperty, template));
        return style;
    }

    private void CloseManagedWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return;
        }

        WindowIconHelper.Forget(hwnd);
        NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        Dispatcher.BeginInvoke(() =>
        {
            if (_closing)
            {
                return;
            }

            _requestRefresh?.Invoke();
        }, DispatcherPriority.ApplicationIdle);
    }

    private void OnCardPreviewMouseDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (sender is not Border card || card.Tag is not CardTag tag)
        {
            return;
        }

        if (!OverviewSearchFilter.Matches(tag.Info, _searchQuery))
        {
            return;
        }

        _pressCard = card;
        _pressPoint = e.GetPosition(this);
        _suppressClick = false;
    }

    private void OnCardPreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_manualDragActive ||
            _pressCard is null ||
            e.LeftButton != MouseButtonState.Pressed ||
            sender is not Border card ||
            !ReferenceEquals(card, _pressCard) ||
            card.Tag is not CardTag tag)
        {
            return;
        }

        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        StartManualDrag(card, tag);
    }

    private void StartManualDrag(Border card, CardTag tag)
    {
        _manualDragActive = true;
        _suppressClick = true;
        _dragHwnd = tag.Info.Hwnd;
        _dragSourceCard = card;
        _dragSourceOpacity = card.Opacity;
        card.Opacity = DimmedCardOpacity;

        _dragGhost = new OverviewDragGhostWindow(tag.Info.Hwnd, CardWidth, CardHeight);
        CaptureMouse();
        MouseMove += OnManualDragMouseMove;
        MouseLeftButtonUp += OnManualDragMouseUp;
        PreviewKeyDown += OnManualDragPreviewKeyDown;
        // CaptureMouse swallows wheel on ScrollViewer — forward it while dragging.
        PreviewMouseWheel += OnManualDragPreviewMouseWheel;
        EnsureDragAutoScrollTimer();

        UpdateGhostPosition(Mouse.GetPosition(this));
    }

    private void OnManualDragMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_manualDragActive)
        {
            return;
        }

        UpdateGhostPosition(e.GetPosition(this));
        var screen = GetScreenPoint(e);
        UpdateDropHover(screen);
        UpdateDragAutoScroll(screen);
    }

    private void OnManualDragPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_manualDragActive)
        {
            return;
        }

        WpfPoint screen;
        try
        {
            screen = PointToScreen(e.GetPosition(this));
        }
        catch
        {
            return;
        }

        var target = _overviewService.OpenWindows.FirstOrDefault(w => w.ContainsScreenPoint(screen))
                     ?? this;
        target.ScrollByWheelDelta(e.Delta);
        e.Handled = true;
    }

    /// <summary>Scroll the overview body by a mouse-wheel delta while a card drag is active.</summary>
    internal void ScrollByWheelDelta(int wheelDelta)
    {
        if (_scroll is null || _closing)
        {
            return;
        }

        // WPF notches are typically ±120; keep a comfortable line-ish step.
        var offset = _scroll.VerticalOffset - wheelDelta / 3.0;
        _scroll.ScrollToVerticalOffset(Math.Clamp(offset, 0, _scroll.ScrollableHeight));
    }

    private void EnsureDragAutoScrollTimer()
    {
        if (_dragAutoScrollTimer is not null)
        {
            return;
        }

        _dragAutoScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _dragAutoScrollTimer.Tick += OnDragAutoScrollTick;
        _dragAutoScrollTimer.Start();
    }

    private void StopDragAutoScrollTimer()
    {
        if (_dragAutoScrollTimer is null)
        {
            return;
        }

        _dragAutoScrollTimer.Stop();
        _dragAutoScrollTimer.Tick -= OnDragAutoScrollTick;
        _dragAutoScrollTimer = null;
        _dragAutoScrollDelta = 0;
    }

    private void OnDragAutoScrollTick(object? sender, EventArgs e)
    {
        // Timer may run on a non-source monitor while the drag started elsewhere.
        if (_dragAutoScrollDelta == 0 || _scroll is null || _closing)
        {
            return;
        }

        var next = Math.Clamp(
            _scroll.VerticalOffset + _dragAutoScrollDelta,
            0,
            _scroll.ScrollableHeight);
        if (Math.Abs(next - _scroll.VerticalOffset) < 0.01)
        {
            return;
        }

        _scroll.ScrollToVerticalOffset(next);
    }

    private void UpdateDragAutoScroll(WpfPoint screenPx)
    {
        foreach (var window in _overviewService.OpenWindows)
        {
            if (window.ContainsScreenPoint(screenPx))
            {
                window.UpdateDragAutoScrollLocal(screenPx);
            }
            else
            {
                window.ClearDragAutoScrollDelta();
            }
        }
    }

    internal void ClearDragAutoScrollDelta() => _dragAutoScrollDelta = 0;

    internal void UpdateDragAutoScrollLocal(WpfPoint screenPx)
    {
        if (_scroll is null)
        {
            _dragAutoScrollDelta = 0;
            return;
        }

        WpfPoint local;
        try
        {
            local = _scroll.PointFromScreen(screenPx);
        }
        catch
        {
            _dragAutoScrollDelta = 0;
            return;
        }

        var h = _scroll.ActualHeight;
        if (h < DragAutoScrollEdgeDip * 2)
        {
            _dragAutoScrollDelta = 0;
            return;
        }

        if (local.Y < DragAutoScrollEdgeDip)
        {
            var t = 1.0 - Math.Clamp(local.Y / DragAutoScrollEdgeDip, 0, 1);
            _dragAutoScrollDelta = -DragAutoScrollMaxPxPerTick * t;
            EnsureDragAutoScrollTimer();
        }
        else if (local.Y > h - DragAutoScrollEdgeDip)
        {
            var t = 1.0 - Math.Clamp((h - local.Y) / DragAutoScrollEdgeDip, 0, 1);
            _dragAutoScrollDelta = DragAutoScrollMaxPxPerTick * t;
            EnsureDragAutoScrollTimer();
        }
        else
        {
            _dragAutoScrollDelta = 0;
        }
    }

    private void OnManualDragMouseUp(object sender, WpfMouseButtonEventArgs e)
    {
        if (!_manualDragActive)
        {
            return;
        }

        CompleteManualDrag(GetScreenPoint(e), cancelled: false);
    }

    private void OnManualDragPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (!_manualDragActive || e.Key != WpfKey.Escape)
        {
            return;
        }

        CompleteManualDrag(screenPx: null, cancelled: true);
        e.Handled = true;
    }

    private WpfPoint GetScreenPoint(WpfMouseEventArgs e) => PointToScreen(e.GetPosition(this));

    private void UpdateGhostPosition(WpfPoint localInWindow)
    {
        if (_dragGhost is null)
        {
            return;
        }

        var screen = PointToScreen(localInWindow);
        var dpi = VisualTreeHelper.GetDpi(this);
        _dragGhost.ShowAtScreenDip(
            screen.X / dpi.DpiScaleX - CardWidth / 2,
            screen.Y / dpi.DpiScaleY - CardHeight / 2);
    }

    private void UpdateDropHover(WpfPoint screenPx)
    {
        foreach (var window in _overviewService.OpenWindows)
        {
            window.ClearDropHighlight();
        }

        var resolved = _overviewService.TryResolveDropTarget(screenPx);
        if (resolved is null)
        {
            return;
        }

        resolved.Window.ShowDropHighlight(resolved.Tag, screenPx);
    }

    internal void ShowDropHighlight(DropTargetTag tag, WpfPoint screenPx)
    {
        if (_scroll?.Content is not FrameworkElement body)
        {
            return;
        }

        WpfPoint local;
        try
        {
            local = body.PointFromScreen(screenPx);
        }
        catch
        {
            return;
        }

        var hit = VisualTreeHelper.HitTest(body, local)?.VisualHit;
        if (hit is null)
        {
            return;
        }

        var dropBorder = FindAncestor<Border>(hit, b => b.Tag is DropTargetTag);
        if (dropBorder is not null)
        {
            _highlightedDropTarget = dropBorder;
            HighlightDropTarget(dropBorder, on: true);
        }

        if (tag.ExistingWorkspaceId is not null &&
            FindAncestor<WrapPanel>(hit, _ => true) is WrapPanel wrap)
        {
            var insertIndex = ComputeWrapInsertIndex(wrap, local, body);
            tag = new DropTargetTag
            {
                ExistingWorkspaceId = tag.ExistingWorkspaceId,
                IsActiveWorkspace = tag.IsActiveWorkspace,
                WrapInsertIndex = insertIndex
            };
            ShowWrapPlaceholder(wrap, insertIndex);
        }
    }

    private void ShowWrapPlaceholder(WrapPanel wrap, int insertIndex)
    {
        RemoveDropPlaceholder();

        var placeholder = new Border
        {
            Width = CardWidth,
            Height = CardHeight,
            Margin = new Thickness(0, 0, CardMargin, CardMargin),
            CornerRadius = new CornerRadius(12),
            Background = MediaBrushes.Transparent,
            Child = new WpfRectangle
            {
                RadiusX = 12,
                RadiusY = 12,
                Stroke = AccentBrush,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 6, 4 },
                Fill = MediaBrushes.Transparent
            }
        };

        insertIndex = Math.Clamp(insertIndex, 0, wrap.Children.Count);
        wrap.Children.Insert(insertIndex, placeholder);
        _dropPlaceholderHost = wrap;
        _dropPlaceholderIndex = insertIndex;
    }

    private void RemoveDropPlaceholder()
    {
        if (_dropPlaceholderHost is WrapPanel wrap &&
            _dropPlaceholderIndex >= 0 &&
            _dropPlaceholderIndex < wrap.Children.Count &&
            wrap.Children[_dropPlaceholderIndex] is Border { Child: WpfRectangle })
        {
            wrap.Children.RemoveAt(_dropPlaceholderIndex);
        }

        _dropPlaceholderHost = null;
        _dropPlaceholderIndex = -1;
    }

    private void CompleteManualDrag(WpfPoint? screenPx, bool cancelled)
    {
        MouseMove -= OnManualDragMouseMove;
        MouseLeftButtonUp -= OnManualDragMouseUp;
        PreviewKeyDown -= OnManualDragPreviewKeyDown;
        PreviewMouseWheel -= OnManualDragPreviewMouseWheel;
        StopDragAutoScrollTimer();
        // Clear auto-scroll on every overview (cross-monitor drag may have started a timer there).
        foreach (var window in _overviewService.OpenWindows)
        {
            if (!ReferenceEquals(window, this))
            {
                window.StopDragAutoScrollTimer();
            }
        }

        ReleaseMouseCapture();

        foreach (var window in _overviewService.OpenWindows)
        {
            window.ClearDropHighlight();
        }

        if (!cancelled && _dragHwnd != IntPtr.Zero && screenPx is WpfPoint sp)
        {
            var resolved = _overviewService.TryResolveDropTarget(sp);
            resolved?.Window.ApplyExternalDrop(_dragHwnd, resolved.Tag);
        }

        if (_dragSourceCard is not null)
        {
            _dragSourceCard.Opacity = _dragSourceOpacity;
        }

        _dragGhost?.Close();
        _dragGhost = null;
        _dragSourceCard = null;
        _dragHwnd = IntPtr.Zero;
        _manualDragActive = false;
        _pressCard = null;
    }

    private static DropTargetTag? ResolveDropTargetFromVisual(DependencyObject hit, WpfPoint localInBody, FrameworkElement body)
    {
        if (FindAncestor<Border>(hit, b => b.Tag is DropTargetTag) is Border tagged &&
            tagged.Tag is DropTargetTag directTag)
        {
            if (directTag.ExistingWorkspaceId is not null &&
                FindAncestor<WrapPanel>(hit, _ => true) is WrapPanel wrap)
            {
                var insertIndex = ComputeWrapInsertIndex(wrap, localInBody, body);
                return new DropTargetTag
                {
                    ExistingWorkspaceId = directTag.ExistingWorkspaceId,
                    IsActiveWorkspace = directTag.IsActiveWorkspace,
                    WrapInsertIndex = insertIndex
                };
            }

            return directTag;
        }

        if (FindAncestor<Border>(hit, b => b.Tag is CardTag) is not null &&
            FindAncestor<Border>(hit, b => b.Tag is DropTargetTag) is Border section &&
            section.Tag is DropTargetTag sectionTag)
        {
            if (FindAncestor<WrapPanel>(hit, _ => true) is WrapPanel wrap)
            {
                var insertIndex = ComputeWrapInsertIndex(wrap, localInBody, body);
                return new DropTargetTag
                {
                    ExistingWorkspaceId = sectionTag.ExistingWorkspaceId,
                    IsActiveWorkspace = sectionTag.IsActiveWorkspace,
                    WrapInsertIndex = insertIndex
                };
            }

            return sectionTag;
        }

        return null;
    }

    private static int ComputeWrapInsertIndex(WrapPanel wrap, WpfPoint localInBody, FrameworkElement body)
    {
        WpfPoint localInWrap;
        try
        {
            var screen = body.PointToScreen(localInBody);
            localInWrap = wrap.PointFromScreen(screen);
        }
        catch
        {
            return wrap.Children.Count;
        }

        var bestIndex = wrap.Children.Count;
        var bestDistance = double.MaxValue;

        for (var i = 0; i < wrap.Children.Count; i++)
        {
            if (wrap.Children[i] is not FrameworkElement child || child.ActualWidth <= 0)
            {
                continue;
            }

            WpfPoint childLocal;
            try
            {
                var childScreen = child.PointToScreen(new WpfPoint(0, 0));
                childLocal = wrap.PointFromScreen(childScreen);
            }
            catch
            {
                continue;
            }

            var center = new WpfPoint(
                childLocal.X + child.ActualWidth / 2,
                childLocal.Y + child.ActualHeight / 2);
            var dx = localInWrap.X - center.X;
            var dy = localInWrap.Y - center.Y;
            var dist = dx * dx + dy * dy;
            if (dist >= bestDistance)
            {
                continue;
            }

            bestDistance = dist;
            bestIndex = localInWrap.X < center.X ? i : i + 1;
        }

        return Math.Clamp(bestIndex, 0, wrap.Children.Count);
    }

    private static T? FindAncestor<T>(DependencyObject start, Func<T, bool> predicate)
        where T : DependencyObject
    {
        for (var node = start; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is T match && predicate(match))
            {
                return match;
            }
        }

        return null;
    }

    private void OnCardClick(object sender, WpfMouseButtonEventArgs e)
    {
        if (_suppressClick || _manualDragActive)
        {
            _suppressClick = false;
            e.Handled = true;
            return;
        }

        if (sender is not Border { Tag: CardTag tag })
        {
            return;
        }

        if (!OverviewSearchFilter.Matches(tag.Info, _searchQuery))
        {
            return;
        }

        SelectWindow(tag.Info.Hwnd, tag.WorkspaceId);
        e.Handled = true;
    }

    private void ApplyDrop(IntPtr hwnd, DropTargetTag tag)
    {
        var result = _workspaceManager.MoveWindowFromOverview(
            hwnd,
            _monitorId,
            tag.ExistingWorkspaceId,
            tag.GapAboveId,
            tag.GapBelowId);

        if (result == WorkspaceManager.OverviewMoveResult.AtLimit)
        {
            return;
        }

        if (result == WorkspaceManager.OverviewMoveResult.Ok)
        {
            _requestRefresh?.Invoke();
        }
    }

    private static void HighlightDropTarget(Border border, bool on)
    {
        if (border.Tag is DropTargetTag { ExistingWorkspaceId: null })
        {
            border.Background = on
                ? new SolidColorBrush(MediaColor.FromArgb(0x55, 0x4C, 0xC2, 0xFF))
                : MediaBrushes.Transparent;
            return;
        }

        var active = border.Tag is DropTargetTag { IsActiveWorkspace: true };
        if (on)
        {
            border.BorderBrush = AccentBrush;
            border.BorderThickness = new Thickness(2);
        }
        else
        {
            border.BorderBrush = active
                ? AccentBrush
                : new SolidColorBrush(MediaColor.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
            border.BorderThickness = new Thickness(active ? 2 : 1);
        }
    }

    private void ScheduleThumbnailRefresh()
    {
        void Attempt(int _)
        {
            if (_closing)
            {
                return;
            }

            UpdateAllThumbnails();
        }

        Dispatcher.BeginInvoke(() => Attempt(0), DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(() => Attempt(1), DispatcherPriority.Render);
        Dispatcher.BeginInvoke(() => Attempt(2), DispatcherPriority.ContextIdle);
        // Cloaked / late DWM sources often need an extra beat after first paint.
        Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(80).ConfigureAwait(true);
            Attempt(3);
        }, DispatcherPriority.ApplicationIdle);
    }

    private void UpdateAllThumbnails()
    {
        if (_thumbs is null || _closing)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var dpiY = VisualTreeHelper.GetDpi(this).DpiScaleY;

        foreach (var (card, info) in _cards)
        {
            if (!NativeMethods.IsWindow(info.Hwnd) || card.Tag is not CardTag tag)
            {
                continue;
            }

            var host = tag.PreviewHost;
            ApplyRoundedClip(tag.ThumbFrame, ThumbCornerRadius);
            ApplyRoundedClip(host, Math.Max(0, ThumbCornerRadius - ThumbInset));

            var matches = OverviewSearchFilter.Matches(info, _searchQuery);
            var thumbOpacity = matches ? (byte)255 : FilteredThumbOpacity;
            host.Opacity = 1.0;

            if (!host.IsVisible || host.ActualWidth < 8 || host.ActualHeight < 8)
            {
                ApplyStaticThumb(host, tag, info.Hwnd, matches, thumbOpacity);
                continue;
            }

            // Cloaked / hidden: DWM often paints a blank sharp rectangle — use PrintWindow/icon.
            if (PreferStaticThumb(info.Hwnd))
            {
                ApplyStaticThumb(host, tag, info.Hwnd, matches, thumbOpacity);
                continue;
            }

            var thumb = _thumbs.EnsureRegistered(info.Hwnd);
            if (thumb == IntPtr.Zero)
            {
                ApplyStaticThumb(host, tag, info.Hwnd, matches, thumbOpacity);
                continue;
            }

            if (NativeMethods.DwmQueryThumbnailSourceSize(thumb, out var src) != 0 ||
                src.cx < 8 ||
                src.cy < 8)
            {
                _thumbs.Unregister(info.Hwnd);
                ApplyStaticThumb(host, tag, info.Hwnd, matches, thumbOpacity);
                continue;
            }

            var aspect = Math.Clamp(src.cx / (double)src.cy, 0.45, 3.2);
            info.AspectRatio = aspect;

            GeneralTransform transform;
            WpfPoint topLeft;
            WpfPoint bottomRight;
            try
            {
                // Include RenderTransform (selection scale) so dest size/pos track the card.
                transform = host.TransformToAncestor(this);
                const double pad = 1;
                topLeft = transform.Transform(new WpfPoint(pad, pad));
                bottomRight = transform.Transform(new WpfPoint(
                    Math.Max(pad + 8, host.ActualWidth - pad),
                    Math.Max(pad + 8, host.ActualHeight - pad)));
            }
            catch
            {
                ApplyStaticThumb(host, tag, info.Hwnd, matches, thumbOpacity);
                continue;
            }

            var boxLeft = topLeft.X;
            var boxTop = topLeft.Y;
            var boxW = Math.Max(8, bottomRight.X - topLeft.X);
            var boxH = Math.Max(8, bottomRight.Y - topLeft.Y);

            double drawW;
            double drawH;
            if (aspect >= boxW / boxH)
            {
                drawW = boxW;
                drawH = boxW / aspect;
            }
            else
            {
                drawH = boxH;
                drawW = boxH * aspect;
            }

            var drawX = boxLeft + (boxW - drawW) / 2;
            var drawY = boxTop + (boxH - drawH) / 2;
            var left = (int)Math.Round(drawX * dpi);
            var top = (int)Math.Round(drawY * dpiY);
            var right = (int)Math.Round((drawX + drawW) * dpi);
            var bottom = (int)Math.Round((drawY + drawH) * dpiY);
            if (right - left < 8 || bottom - top < 8)
            {
                ApplyStaticThumb(host, tag, info.Hwnd, matches, thumbOpacity);
                continue;
            }

            // Live DWM fills the inset host; rounded frame stays outside dest rect.
            host.Child = null;
            tag.DwmLive = true;
            _thumbs.Update(thumb, left, top, right, bottom, opacity: thumbOpacity, visible: matches);
        }
    }

    private void ApplySearchFilter()
    {
        var matching = GetMatchingCards();
        if (_selectedMatchIndex >= matching.Count)
        {
            _selectedMatchIndex = matching.Count > 0 ? 0 : -1;
        }

        for (var i = 0; i < _cards.Count; i++)
        {
            var (card, info) = _cards[i];
            var matches = OverviewSearchFilter.Matches(info, _searchQuery);
            card.Opacity = matches ? 1.0 : FilteredOutOpacity;
            card.IsHitTestVisible = matches;
            UpdateCardSelectionRing(card, info, matching);
        }

        UpdateAllThumbnails();
    }

    private void UpdateCardSelectionRing(Border card, WindowThumbnailInfo info, List<(Border Card, WindowThumbnailInfo Info)> matching)
    {
        var matchIdx = matching.FindIndex(m => ReferenceEquals(m.Card, card));
        var selected = matchIdx >= 0 && matchIdx == _selectedMatchIndex;
        card.BorderBrush = selected || info.IsSticky ? AccentBrush : DefaultCardBorderBrush;
        card.BorderThickness = new Thickness(selected || info.IsSticky ? 2 : 1);
        ApplyCardSelectionScale(card, selected);
    }

    private void ApplyCardSelectionScale(Border card, bool selected)
    {
        if (card.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform(1, 1);
            card.RenderTransform = st;
            card.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
        }

        System.Windows.Controls.Panel.SetZIndex(card, selected ? 2 : 0);
        var target = selected ? SelectedCardScale : 1.0;
        var current = st.ScaleX;
        if (Math.Abs(current - target) < 0.001 &&
            st.HasAnimatedProperties == false)
        {
            return;
        }

        if (!AreAnimationsEnabled())
        {
            st.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            st.ScaleX = target;
            st.ScaleY = target;
            UpdateAllThumbnails();
            return;
        }

        BeginThumbSync();
        var anim = new DoubleAnimation(current, target, SelectionScaleDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        EventHandler? completed = null;
        completed = (_, _) =>
        {
            anim.Completed -= completed;
            EndThumbSync();
        };
        anim.Completed += completed;
        st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, anim.Clone());
    }

    private void BeginThumbSync()
    {
        if (_thumbSyncDepth++ == 0)
        {
            CompositionTarget.Rendering += OnThumbSyncRendering;
        }
    }

    private void EndThumbSync()
    {
        if (_thumbSyncDepth <= 0)
        {
            return;
        }

        if (--_thumbSyncDepth == 0)
        {
            CompositionTarget.Rendering -= OnThumbSyncRendering;
            if (!_closing)
            {
                UpdateAllThumbnails();
            }
        }
    }

    private void OnThumbSyncRendering(object? sender, EventArgs e)
    {
        if (_closing)
        {
            return;
        }

        UpdateAllThumbnails();
    }

    private static bool AreAnimationsEnabled()
    {
        try
        {
            return App.Services.AnimationSettings.AnimationsEnabled;
        }
        catch
        {
            return true;
        }
    }

    private List<(Border Card, WindowThumbnailInfo Info)> GetMatchingCards() =>
        _cards.Where(c => OverviewSearchFilter.Matches(c.Info, _searchQuery)).ToList();

    public void ScrollSelectedIntoView()
    {
        var matching = GetMatchingCards();
        if (_scroll is null || _selectedMatchIndex < 0 || _selectedMatchIndex >= matching.Count)
        {
            return;
        }

        matching[_selectedMatchIndex].Card.BringIntoView();
    }

    private void ClearSearch() => _overviewService.ClearSearchEverywhere();

    private void OnPreviewTextInput(object sender, WpfTextCompositionEventArgs e)
    {
        if (_manualDragActive ||
            _searchBox?.IsKeyboardFocusWithin == true ||
            string.IsNullOrEmpty(e.Text) ||
            char.IsControl(e.Text[0]))
        {
            return;
        }

        _overviewService.ActivateSearchEverywhere(this);
        if (_searchBox is not null)
        {
            _suppressSearchBroadcast = true;
            try
            {
                _searchBox.Text += e.Text;
                _searchBox.CaretIndex = _searchBox.Text.Length;
                _searchQuery = _searchBox.Text;
                ApplySearchFilter();
            }
            finally
            {
                _suppressSearchBroadcast = false;
            }

            _overviewService.BroadcastSearchQuery(_searchQuery, this);
        }

        e.Handled = true;
    }

    private void OnSearchBoxKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key is WpfKey.Left or WpfKey.Right && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            return;
        }

        if (e.Key == WpfKey.Escape)
        {
            ClearSearch();
            e.Handled = true;
            return;
        }

        if (e.Key is WpfKey.Up or WpfKey.Down or WpfKey.Left or WpfKey.Right)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                return;
            }

            _overviewService.NavigateSelection(e.Key);
            e.Handled = true;
            return;
        }

        if (e.Key == WpfKey.Enter)
        {
            _overviewService.ActivateSelected();
            e.Handled = true;
        }
    }

    private void OnEmptyWorkspaceClick(object sender, WpfMouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int workspaceId })
        {
            _workspaceManager.SwitchWorkspace(_monitorId, workspaceId);
            Close();
            e.Handled = true;
        }
    }

    private void SelectWindow(IntPtr hwnd, int workspaceId)
    {
        _workspaceManager.SwitchWorkspace(_monitorId, workspaceId);

        var target = hwnd;
        var dispatcher = Dispatcher;
        EventHandler? onClosed = null;
        onClosed = (_, _) =>
        {
            Closed -= onClosed;
            dispatcher.BeginInvoke(
                () => NativeMethods.TryActivateWindow(target),
                DispatcherPriority.ApplicationIdle);
        };
        Closed += onClosed;
        Close();
    }

    private void OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (_manualDragActive)
        {
            return;
        }

        if (e.Key == WpfKey.Escape)
        {
            if (!string.IsNullOrWhiteSpace(_searchQuery))
            {
                ClearSearch();
            }
            else
            {
                Close();
            }

            e.Handled = true;
            return;
        }

        if (e.Key == WpfKey.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _overviewService.ActivateSearchEverywhere(this);
            e.Handled = true;
            return;
        }

        if (e.Key is WpfKey.Left or WpfKey.Right && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _overviewService.FocusAdjacent(e.Key == WpfKey.Right ? 1 : -1);
            e.Handled = true;
            return;
        }

        if (_searchBox?.IsKeyboardFocusWithin == true &&
            e.Key is WpfKey.Left or WpfKey.Right &&
            Keyboard.Modifiers == ModifierKeys.Shift)
        {
            return;
        }

        if (e.Key is WpfKey.D1 or WpfKey.D2 or WpfKey.D3 or WpfKey.D4 or WpfKey.D5
            or WpfKey.D6 or WpfKey.D7 or WpfKey.D8 or WpfKey.D9)
        {
            var workspace = e.Key - WpfKey.D1 + 1;
            _workspaceManager.SwitchOrCreateWorkspace(_monitorId, workspace);
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key == WpfKey.Enter)
        {
            _overviewService.ActivateSelected();
            e.Handled = true;
            return;
        }

        if (e.Key is WpfKey.Up or WpfKey.Down or WpfKey.Left or WpfKey.Right)
        {
            _overviewService.NavigateSelection(e.Key);
            e.Handled = true;
        }
    }

    private void OnClosedHandler(object? sender, EventArgs e)
    {
        if (_manualDragActive)
        {
            CompleteManualDrag(screenPx: null, cancelled: true);
        }

        _closing = true;
        while (_thumbSyncDepth > 0)
        {
            EndThumbSync();
        }

        _thumbs?.Dispose();
        _thumbs = null;
        _cards.Clear();
        WindowIconHelper.ClearCache();
    }
}
