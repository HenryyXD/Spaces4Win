namespace Spaces4Win.Services;

/// <summary>
/// Matches currently open windows to a saved layout without launching anything.
/// Priority: HWND+PID → path+title → path+class → unique path.
/// </summary>
public static class WindowLayoutMatcher
{
    public sealed record LayoutMatch(
        LayoutWindowEntry Entry,
        string PreferredMonitorId);

    public static Dictionary<IntPtr, LayoutMatch> Match(
        WindowLayoutDocument layout,
        IReadOnlyList<WindowIdentity> openWindows)
    {
        var result = new Dictionary<IntPtr, LayoutMatch>();
        if (layout.Monitors.Count == 0 || openWindows.Count == 0)
        {
            return result;
        }

        var entries = layout.Monitors
            .SelectMany(m => m.Windows.Select(w => (MonitorId: m.MonitorId, Entry: w)))
            .ToList();
        var unused = openWindows.ToList();

        Claim(entries, unused, result, (e, w) =>
            e.Hwnd != 0 &&
            w.Hwnd.ToInt64() == e.Hwnd &&
            (e.ProcessId == 0 || w.ProcessId == e.ProcessId));

        Claim(entries, unused, result, (e, w) =>
            !string.IsNullOrWhiteSpace(e.ProcessPath) &&
            !string.IsNullOrWhiteSpace(e.Title) &&
            PathsEqual(e.ProcessPath, w.ProcessPath) &&
            string.Equals(e.Title, w.Title, StringComparison.Ordinal));

        Claim(entries, unused, result, (e, w) =>
            !string.IsNullOrWhiteSpace(e.ProcessPath) &&
            !string.IsNullOrWhiteSpace(e.ClassName) &&
            PathsEqual(e.ProcessPath, w.ProcessPath) &&
            string.Equals(e.ClassName, w.ClassName, StringComparison.Ordinal));

        ClaimUniquePath(entries, unused, result);

        return result;
    }

    private static void Claim(
        List<(string MonitorId, LayoutWindowEntry Entry)> entries,
        List<WindowIdentity> unused,
        Dictionary<IntPtr, LayoutMatch> result,
        Func<LayoutWindowEntry, WindowIdentity, bool> predicate)
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var (monitorId, entry) = entries[i];
            var idx = unused.FindIndex(w => predicate(entry, w));
            if (idx < 0)
            {
                continue;
            }

            var window = unused[idx];
            unused.RemoveAt(idx);
            entries.RemoveAt(i);
            result[window.Hwnd] = new LayoutMatch(entry, monitorId);
        }
    }

    private static void ClaimUniquePath(
        List<(string MonitorId, LayoutWindowEntry Entry)> entries,
        List<WindowIdentity> unused,
        Dictionary<IntPtr, LayoutMatch> result)
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var (monitorId, entry) = entries[i];
            if (string.IsNullOrWhiteSpace(entry.ProcessPath))
            {
                continue;
            }

            var candidates = unused
                .Select((w, index) => (w, index))
                .Where(t => PathsEqual(entry.ProcessPath, t.w.ProcessPath))
                .ToList();

            if (candidates.Count != 1)
            {
                continue;
            }

            var (window, idx) = candidates[0];
            unused.RemoveAt(idx);
            entries.RemoveAt(i);
            result[window.Hwnd] = new LayoutMatch(entry, monitorId);
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            a.Trim(),
            b.Trim(),
            StringComparison.OrdinalIgnoreCase);
}
