using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Spaces4Win.Services;

/// <summary>
/// Cross-process quit signal so agents/scripts can exit without taskkill
/// (force-kill skips <see cref="ShutdownCoordinator"/> and leaves windows hidden).
/// </summary>
public static class AppControlChannel
{
    public const string GlobalQuitEventName = @"Global\Spaces4Win.QuitRequest";
    public const string LocalQuitEventName = @"Local\Spaces4Win.QuitRequest";

    public static IDisposable StartListening(Action onQuitRequested)
    {
        ArgumentNullException.ThrowIfNull(onQuitRequested);

        var handle = CreateServerEvent();
        var registration = ThreadPool.RegisterWaitForSingleObject(
            handle,
            (_, _) =>
            {
                try
                {
                    onQuitRequested();
                }
                catch
                {
                    // best effort
                }
            },
            state: null,
            millisecondsTimeOutInterval: -1,
            executeOnlyOnce: false);

        return new Listener(handle, registration);
    }

    /// <summary>
    /// Signals a running instance to quit. Returns false if no listener was found.
    /// </summary>
    public static bool TryRequestQuit()
    {
        return TrySet(GlobalQuitEventName) || TrySet(LocalQuitEventName);
    }

    /// <summary>
    /// Signals quit and waits until other Spaces4Win processes exit (excludes this PID).
    /// Exit codes: 0 = gone / already stopped, 1 = no listener while process still alive, 2 = timeout.
    /// </summary>
    public static int QuitAndWait(TimeSpan timeout)
    {
        var others = GetOtherSpaces4WinProcesses();
        if (others.Length == 0)
        {
            return 0;
        }

        var signaled = TryRequestQuit();
        if (!signaled)
        {
            return 1;
        }

        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            foreach (var p in others)
            {
                try
                {
                    p.Refresh();
                }
                catch
                {
                    // ignore
                }
            }

            if (others.All(p =>
                {
                    try
                    {
                        return p.HasExited;
                    }
                    catch
                    {
                        return true;
                    }
                }))
            {
                return 0;
            }

            Thread.Sleep(100);
        }

        return 2;
    }

    private static EventWaitHandle CreateServerEvent()
    {
        try
        {
            return CreateWithWorldAccess(GlobalQuitEventName);
        }
        catch
        {
            return CreateWithWorldAccess(LocalQuitEventName);
        }
    }

    private static EventWaitHandle CreateWithWorldAccess(string name)
    {
        var security = new EventWaitHandleSecurity();
        security.AddAccessRule(new EventWaitHandleAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            EventWaitHandleRights.FullControl,
            AccessControlType.Allow));

        var createdNew = false;
        var handle = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: name,
            createdNew: out createdNew);
        try
        {
            handle.SetAccessControl(security);
        }
        catch
        {
            // Created handle is still usable for same-integrity clients.
        }

        return handle;
    }

    private static bool TrySet(string name)
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(name, out var existing))
            {
                return false;
            }

            using (existing)
            {
                existing.Set();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Process[] GetOtherSpaces4WinProcesses()
    {
        var self = Environment.ProcessId;
        return Process.GetProcessesByName("Spaces4Win")
            .Where(p =>
            {
                try
                {
                    return p.Id != self;
                }
                catch
                {
                    return false;
                }
            })
            .ToArray();
    }

    private sealed class Listener : IDisposable
    {
        private readonly EventWaitHandle _handle;
        private readonly RegisteredWaitHandle _registration;
        private bool _disposed;

        public Listener(EventWaitHandle handle, RegisteredWaitHandle registration)
        {
            _handle = handle;
            _registration = registration;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _registration.Unregister(null);
            }
            catch
            {
                // ignore
            }

            _handle.Dispose();
        }
    }
}
