using System.Runtime.InteropServices;
using System.Text;
using Spaces4Win.Core;
using Spaces4Win.Native;

namespace Spaces4Win.Services;

internal static class ProcessPathHelper
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public static WindowIdentity CaptureIdentity(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return new WindowIdentity
        {
            Hwnd = hwnd,
            ProcessId = pid,
            ProcessPath = TryGetProcessPath(pid) ?? string.Empty,
            Title = WindowClassifier.GetWindowTitle(hwnd),
            ClassName = WindowClassifier.GetClassName(hwnd)
        };
    }

    public static string? TryGetProcessPath(uint processId)
    {
        if (processId == 0)
        {
            return null;
        }

        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var sb = new StringBuilder(1024);
            var size = (uint)sb.Capacity;
            if (!QueryFullProcessImageName(handle, 0, sb, ref size))
            {
                return null;
            }

            return sb.ToString();
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(
        IntPtr hProcess,
        uint dwFlags,
        StringBuilder lpExeName,
        ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
