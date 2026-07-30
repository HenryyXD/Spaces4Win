using Wpf.Ui.Abstractions;

namespace Spaces4Win.Settings;

public sealed class SettingsPageProvider : INavigationViewPageProvider
{
    private readonly SettingsSessionLocator _locator;
    private readonly Dictionary<Type, object> _cache = new();

    public SettingsPageProvider(SettingsSessionLocator locator)
    {
        _locator = locator;
    }

    public object? GetPage(Type pageType)
    {
        if (_cache.TryGetValue(pageType, out var existing))
        {
            return existing;
        }

        var page = Activator.CreateInstance(pageType)
                   ?? throw new InvalidOperationException($"Cannot create page {pageType.Name}");

        if (page is System.Windows.FrameworkElement fe)
        {
            fe.DataContext = CreateViewModel(pageType);
        }

        _cache[pageType] = page;
        return page;
    }

    private object CreateViewModel(Type pageType)
    {
        var session = _locator.Session;
        var loc = _locator.Localization;

        return pageType.Name switch
        {
            nameof(Pages.OverviewPage) => new ViewModels.OverviewPageViewModel(session, loc),
            nameof(Pages.WorkspacesPage) => new ViewModels.WorkspacesPageViewModel(session, loc),
            nameof(Pages.HotkeysPage) => new ViewModels.HotkeysPageViewModel(session, loc),
            nameof(Pages.IndicatorPage) => new ViewModels.IndicatorPageViewModel(session, loc),
            nameof(Pages.AppearancePage) => new ViewModels.AppearancePageViewModel(session, loc),
            nameof(Pages.BehaviorPage) => new ViewModels.BehaviorPageViewModel(session, loc),
            nameof(Pages.StartupPage) => new ViewModels.StartupPageViewModel(session, loc),
            nameof(Pages.AboutPage) => new ViewModels.AboutPageViewModel(session, loc),
            _ => session
        };
    }
}

public sealed class SettingsSessionLocator
{
    public required Settings.ViewModels.SettingsSession Session { get; init; }
    public required Localization.ILocalizationService Localization { get; init; }
    public required ViewModels.SettingsShellViewModel Shell { get; init; }
}
