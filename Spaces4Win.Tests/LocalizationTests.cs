using Spaces4Win.Localization;

namespace Spaces4Win.Tests;

public class LocalizationTests
{
    [Fact]
    public void English_Resource_Exists()
    {
        var loc = new LocalizationService();
        loc.SetLanguagePreference("en");
        Assert.Equal("Overview", loc.Get("Settings.Navigation.Overview"));
        Assert.Equal("Save", loc.Get("Common.Save"));
    }

    [Fact]
    public void Portuguese_Resource_Exists()
    {
        var loc = new LocalizationService();
        loc.SetLanguagePreference("pt-BR");
        Assert.Equal("Visão geral", loc.Get("Settings.Navigation.Overview"));
        Assert.Equal("Salvar", loc.Get("Common.Save"));
    }

    [Fact]
    public void MissingKey_FallsBackToBracketedKey()
    {
        var loc = new LocalizationService();
        loc.SetLanguagePreference("en");
        Assert.Equal("[Does.Not.Exist]", loc.Get("Does.Not.Exist"));
    }

    [Fact]
    public void SystemPreference_PortugueseCulture_UsesPtBr()
    {
        var previous = Thread.CurrentThread.CurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("pt-BR");
            var loc = new LocalizationService();
            var culture = loc.ResolveUiCulture("System");
            Assert.Equal("pt-BR", culture.Name);
        }
        finally
        {
            Thread.CurrentThread.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Preference_Persists_AndNotifies()
    {
        var loc = new LocalizationService();
        var notified = false;
        loc.CultureChanged += (_, _) => notified = true;
        loc.SetLanguagePreference("pt-BR");
        Assert.Equal("pt-BR", loc.LanguagePreference);
        Assert.True(notified);
        Assert.Equal("Visão geral", loc["Settings.Navigation.Overview"]);
    }

    [Fact]
    public void RuntimeSwitch_UpdatesIndexer()
    {
        var loc = new LocalizationService();
        loc.SetLanguagePreference("en");
        Assert.Equal("Hotkeys", loc["Settings.Navigation.Hotkeys"]);
        loc.SetLanguagePreference("pt-BR");
        Assert.Equal("Atalhos", loc["Settings.Navigation.Hotkeys"]);
    }

    [Fact]
    public void Pluralization_UsesCorrectResource()
    {
        var loc = new LocalizationService();
        loc.SetLanguagePreference("en");
        Assert.Equal("1 window", loc.Plural("Workspace.WindowCount.One", "Workspace.WindowCount.Many", 1));
        Assert.Equal("3 windows", loc.Plural("Workspace.WindowCount.One", "Workspace.WindowCount.Many", 3));
        loc.SetLanguagePreference("pt-BR");
        Assert.Equal("1 janela", loc.Plural("Workspace.WindowCount.One", "Workspace.WindowCount.Many", 1));
        Assert.Equal("3 janelas", loc.Plural("Workspace.WindowCount.One", "Workspace.WindowCount.Many", 3));
    }

    [Fact]
    public void LanguageIds_AreStableIdentifiers()
    {
        var loc = new LocalizationService();
        Assert.Contains(loc.AvailableLanguages, o => o.Id == "System");
        Assert.Contains(loc.AvailableLanguages, o => o.Id == "en");
        Assert.Contains(loc.AvailableLanguages, o => o.Id == "pt-BR");
    }
}
