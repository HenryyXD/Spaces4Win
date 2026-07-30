using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Spaces4Win.Native;

namespace Spaces4Win.Services;

public static class ElevationService
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevation = 20;

    public static bool IsCurrentProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsWindowElevated(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return false;
        }

        return IsProcessElevated(pid);
    }

    public static bool IsProcessElevated(uint processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero)
        {
            // Access denied typically means a higher-integrity process.
            return Marshal.GetLastWin32Error() == 5 && !IsCurrentProcessElevated();
        }

        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token))
            {
                return !IsCurrentProcessElevated();
            }

            try
            {
                var size = Marshal.SizeOf<TOKEN_ELEVATION>();
                var ptr = Marshal.AllocHGlobal(size);
                try
                {
                    if (!GetTokenInformation(token, TokenElevation, ptr, size, out _))
                    {
                        return false;
                    }

                    var elevation = Marshal.PtrToStructure<TOKEN_ELEVATION>(ptr);
                    return elevation.TokenIsElevated != 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>
    /// Starts a new elevated instance via UAC. Returns true if the elevated process was started.
    /// Passes <c>--elevating</c> so the replacement waits briefly for this process to release
    /// the single-instance mutex.
    /// </summary>
    public static bool TryRelaunchElevated()
    {
        if (IsCurrentProcessElevated())
        {
            return true;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--elevating",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
            };

            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User cancelled UAC.
            return false;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        IntPtr tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_ELEVATION
    {
        public int TokenIsElevated;
    }
}
