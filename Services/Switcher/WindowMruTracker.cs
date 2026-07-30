namespace Spaces4Win.Services.Switcher;

/// <summary>Short per-monitor MRU of managed window HWNDs for Caps+Q ordering.</summary>
public sealed class WindowMruTracker
{
    private readonly object _sync = new();
    private readonly Dictionary<string, LinkedList<nint>> _byMonitor = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _capacity;

    public WindowMruTracker(int capacity = 16)
    {
        _capacity = Math.Clamp(capacity, 2, 64);
    }

    public void Touch(string monitorId, IntPtr hwnd)
    {
        if (string.IsNullOrWhiteSpace(monitorId) || hwnd == IntPtr.Zero)
        {
            return;
        }

        lock (_sync)
        {
            if (!_byMonitor.TryGetValue(monitorId, out var list))
            {
                list = new LinkedList<nint>();
                _byMonitor[monitorId] = list;
            }

            var node = list.Find(hwnd);
            if (node is not null)
            {
                list.Remove(node);
            }

            list.AddFirst(hwnd);
            while (list.Count > _capacity)
            {
                list.RemoveLast();
            }
        }
    }

    public void Forget(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        lock (_sync)
        {
            foreach (var list in _byMonitor.Values)
            {
                var node = list.Find(hwnd);
                if (node is not null)
                {
                    list.Remove(node);
                }
            }
        }
    }

    public IReadOnlyList<nint> GetNewestFirst(string monitorId)
    {
        lock (_sync)
        {
            if (!_byMonitor.TryGetValue(monitorId, out var list) || list.Count == 0)
            {
                return Array.Empty<nint>();
            }

            return list.ToList();
        }
    }
}
