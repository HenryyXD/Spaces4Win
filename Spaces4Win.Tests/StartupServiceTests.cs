using Spaces4Win.Services;

namespace Spaces4Win.Tests;

public class StartupServiceTests
{
    [Fact]
    public void BuildCreateArguments_Limited_UsesOnLogonAndLimited()
    {
        var args = StartupService.BuildCreateArguments(@"C:\Apps\Spaces4Win.exe", elevatedAtLogon: false);
        Assert.Contains("/SC ONLOGON", args);
        Assert.Contains("/RL LIMITED", args);
        Assert.Contains(@"Spaces4Win\Autostart", args);
        Assert.Contains(@"C:\Apps\Spaces4Win.exe", args);
        Assert.DoesNotContain("HIGHEST", args);
    }

    [Fact]
    public void BuildCreateArguments_Elevated_UsesHighest()
    {
        var args = StartupService.BuildCreateArguments(@"C:\Program Files\Spaces4Win\Spaces4Win.exe", elevatedAtLogon: true);
        Assert.Contains("/RL HIGHEST", args);
        Assert.Contains("/F", args);
        Assert.Contains("/IT", args);
    }

    [Fact]
    public void BuildDeleteAndQuery_UseSameTaskName()
    {
        Assert.Contains(@"Spaces4Win\Autostart", StartupService.BuildDeleteArguments());
        Assert.Contains(@"Spaces4Win\Autostart", StartupService.BuildQueryArguments());
    }
}
