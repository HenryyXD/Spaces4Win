using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spaces4Win.Localization;
using Wpf.Ui.Controls;

namespace Spaces4Win.Settings.ViewModels;

public sealed partial class SettingsShellViewModel : ObservableObject
{
    private readonly ILocalizationService _loc;

    public SettingsShellViewModel(SettingsSession session, ILocalizationService loc)
    {
        Session = session;
        _loc = loc;
        RebuildNavigation();
        _loc.CultureChanged += (_, _) =>
        {
            RebuildNavigation();
            Session.RefreshMonitors();
            OnPropertyChanged(nameof(Loc));
        };
    }

    public SettingsSession Session { get; }
    public ILocalizationService Loc => _loc;

    public ObservableCollection<object> MenuItems { get; } = new();
    public ObservableCollection<object> FooterItems { get; } = new();

    [ObservableProperty]
    private bool _isPaneOpen = true;

    public void RebuildNavigation()
    {
        MenuItems.Clear();
        FooterItems.Clear();

        MenuItems.Add(Nav("Settings.Navigation.Overview", SymbolRegular.Home24, typeof(Pages.OverviewPage)));
        MenuItems.Add(Nav("Settings.Navigation.Workspaces", SymbolRegular.Grid24, typeof(Pages.WorkspacesPage)));
        MenuItems.Add(Nav("Settings.Navigation.Hotkeys", SymbolRegular.Keyboard24, typeof(Pages.HotkeysPage)));
        MenuItems.Add(Nav("Settings.Navigation.Indicators", SymbolRegular.Circle24, typeof(Pages.IndicatorPage)));
        MenuItems.Add(Nav("Settings.Navigation.Appearance", SymbolRegular.PaintBrush24, typeof(Pages.AppearancePage)));
        MenuItems.Add(Nav("Settings.Navigation.Behavior", SymbolRegular.Settings24, typeof(Pages.BehaviorPage)));
        MenuItems.Add(Nav("Settings.Navigation.Startup", SymbolRegular.ShieldTask24, typeof(Pages.StartupPage)));

        FooterItems.Add(Nav("Settings.Navigation.About", SymbolRegular.Info24, typeof(Pages.AboutPage)));
    }

    private NavigationViewItem Nav(string titleKey, SymbolRegular icon, Type pageType)
    {
        return new NavigationViewItem
        {
            Content = _loc.Get(titleKey),
            Icon = new SymbolIcon { Symbol = icon },
            TargetPageType = pageType,
            Tag = titleKey
        };
    }

    [RelayCommand]
    private void Save()
    {
        if (Session.Save())
        {
            Saved?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void Discard() => Session.Discard();

    public event EventHandler? Saved;
}
