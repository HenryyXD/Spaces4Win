using System.Security.AccessControl;
using System.Security.Principal;

namespace Spaces4Win.Services;

/// <summary>
/// Ensures only one interactive Spaces4Win host runs per Windows logon session.
/// Control CLI (<c>--quit</c>, <c>--recover-hidden</c>) does not take this lock.
/// Release before elevated relaunch so the new process can acquire while the old one exits.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public const string MutexName = @"Local\Spaces4Win.SingleInstance";

    private readonly Mutex _mutex;
    private bool _owned;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
        _owned = true;
    }

    /// <summary>
    /// Tries to become the sole host. When <paramref name="wait"/> is positive,
    /// waits for a previous instance to release (elevation handoff via <c>--elevating</c>).
    /// </summary>
    public static SingleInstanceGuard? TryAcquire(TimeSpan wait = default)
        => TryAcquireNamed(MutexName, wait);

    /// <summary>Test helper — isolated named mutex so the running app does not interfere.</summary>
    public static SingleInstanceGuard? TryAcquireNamed(string name, TimeSpan wait = default)
    {
        Mutex mutex;
        try
        {
            mutex = CreateWithWorldAccess(name);
        }
        catch
        {
            return null;
        }

        var acquired = false;
        try
        {
            acquired = wait <= TimeSpan.Zero
                ? mutex.WaitOne(0)
                : mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            // Previous process crashed while holding the mutex — we now own it.
            acquired = true;
        }
        catch
        {
            mutex.Dispose();
            return null;
        }

        if (!acquired)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstanceGuard(mutex);
    }

    private static Mutex CreateWithWorldAccess(string name)
    {
        var security = new MutexSecurity();
        security.AddAccessRule(new MutexAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            MutexRights.FullControl,
            AccessControlType.Allow));

        var mutex = new Mutex(initiallyOwned: false, name: name, createdNew: out _);
        try
        {
            mutex.SetAccessControl(security);
        }
        catch
        {
            // Created handle is still usable for same-integrity clients.
        }

        return mutex;
    }

    /// <summary>
    /// Drops ownership so an elevated replacement can become the sole host
    /// while this process is still shutting down.
    /// </summary>
    public void Release()
    {
        if (!_owned || _disposed)
        {
            return;
        }

        try
        {
            _mutex.ReleaseMutex();
        }
        catch
        {
            // ignore
        }

        _owned = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Release();
        _mutex.Dispose();
    }
}
