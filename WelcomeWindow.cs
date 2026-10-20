using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Spaces4Win.Config;
using Spaces4Win.Localization;

namespace Spaces4Win;

/// <summary>Four-line first-run tips. Shown once, then <see cref="AppConfig.HasSeenWelcome"/> is saved.</summary>
public sealed class WelcomeWindow : Window
{
    public const int CurrentRevision = 1;

    private readonly AppServices _services;

    public WelcomeWindow(AppServices services, ILocalizationService loc)
    {
        _services = services;
        Title = loc.Get("Welcome.Title");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = true;
        Topmost = true;

        var config = services.Config;
        var switchKey = config.SwitchWorkspaceHotkeys.FirstOrDefault()?.Display ?? "CapsLock+1";
        var overviewKey = config.OverviewHotkey.Display;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = loc.Get("Welcome.Title"),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        root.Children.Add(Line(loc.Format("Welcome.Switch", switchKey)));
        root.Children.Add(Line(loc.Format("Welcome.Overview", overviewKey)));
        root.Children.Add(Line(loc.Get("Welcome.Indicator")));
        root.Children.Add(Line(loc.Get("Welcome.Admin")));

        var gotIt = new Button
        {
            Content = loc.Get("Welcome.GotIt"),
            Height = 32,
            Margin = new Thickness(0, 8, 0, 8),
            IsDefault = true,
            Style = PlainButton()
        };
        gotIt.Click += (_, _) => Close();

        var alt = new Button
        {
            Content = loc.Get("Welcome.UseCtrlAlt"),
            Height = 32,
            Style = PlainButton()
        };
        alt.Click += (_, _) =>
        {
            config.UseCtrlAltInsteadOfCapsLock();
            services.ConfigService.Save(config);
            services.HotkeyService?.Apply(config);
            Close();
        };

        root.Children.Add(gotIt);
        root.Children.Add(alt);
        Content = root;
        Closed += (_, _) => Persist();
    }

    private void Persist()
    {
        var config = _services.Config;
        config.HasSeenWelcome = true;
        config.WelcomeRevision = CurrentRevision;
        _services.ConfigService.Save(config);
    }

    /// <summary>App-level WPF-UI styles hide a plain Button on an unthemed window.</summary>
    private static Style PlainButton()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.White);
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC8, 0xC8, 0xC8)));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.PaddingProperty, new Thickness(10, 4, 10, 4));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;

        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, System.Windows.Media.Brushes.Black));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, System.Windows.Media.Brushes.White));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.TemplateProperty, template));
        style.Setters.Add(new Setter(System.Windows.Controls.Control.CursorProperty, System.Windows.Input.Cursors.Hand));
        return style;
    }

    private static TextBlock Line(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8)
    };
}
