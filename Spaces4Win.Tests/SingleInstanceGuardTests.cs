using Spaces4Win.Services;

namespace Spaces4Win.Tests;

public sealed class SingleInstanceGuardTests
{
    private static string UniqueName() =>
        @"Local\Spaces4Win.SingleInstance.Test." + Guid.NewGuid().ToString("N");

    [Fact]
    public void Second_acquire_on_other_thread_fails_while_first_holds()
    {
        var name = UniqueName();
        using var first = SingleInstanceGuard.TryAcquireNamed(name);
        Assert.NotNull(first);

        SingleInstanceGuard? second = null;
        var thread = new Thread(() => second = SingleInstanceGuard.TryAcquireNamed(name));
        thread.Start();
        thread.Join();

        Assert.Null(second);
    }

    [Fact]
    public void Acquire_succeeds_after_release()
    {
        var name = UniqueName();
        var first = SingleInstanceGuard.TryAcquireNamed(name);
        Assert.NotNull(first);
        first!.Release();
        first.Dispose();

        using var second = SingleInstanceGuard.TryAcquireNamed(name);
        Assert.NotNull(second);
    }
}
