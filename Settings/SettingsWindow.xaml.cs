using System.Windows;
using System.Windows.Data;
using Spaces4Win.Localization;
using Spaces4Win.Settings.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Spaces4Win.Settings;

public partial class SettingsWindow : FluentWindow
{
    private readonly SettingsShellViewModel _vm;
    private readonly ILocalizationService _loc;
    private readonly SnackbarService _snackbar = new();

    public SettingsWindow(SettingsShellViewModel viewModel, ILocalizationService loc, SettingsPageProvider pageProvider)
    {
        _vm = viewModel;
        _loc = loc;
        DataContext = _vm;

        SystemThemeWatcher.Watch(this, WindowBackdropType.Mica);
        InitializeComponent();

        RootNavigation.SetPageProviderService(pageProvider);
        _snackbar.SetSnackbarPresenter(SnackbarPresenter);

        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        _vm.Saved += OnSaved;
        _loc.CultureChanged += OnCultureChanged;
        Closed += (_, _) =>
        {
            _vm.Saved -= OnSaved;
            _loc.CultureChanged -= OnCultureChanged;
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Re-assert user theme after SystemThemeWatcher.Watch's initial ApplySystemTheme.
        ThemeHelper.ApplyTheme(_vm.Session.Draft.Theme, _vm.Session.Draft.ReduceMotion);
        RootNavigation.Navigate(typeof(Pages.OverviewPage));
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Collapse labels into icon-only pane on narrow widths.
        if (e.NewSize.Width < 860)
        {
            _vm.IsPaneOpen = false;
        }
    }

    private void OnSaved(object? sender, EventArgs e)
    {
        _snackbar.Show(
            _loc.Get("App.Name"),
            _loc.Get("Common.Saved"),
            ControlAppearance.Success,
            new SymbolIcon(SymbolRegular.Checkmark24),
            TimeSpan.FromSeconds(2));
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        Title = _loc.Get("App.Name");
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is Visibility.Visible;
}
