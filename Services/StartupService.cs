using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Spaces4Win.Services;

/// <summary>
/// Registers Spaces4Win to start at user logon via Task Scheduler (not the Run key).
/// When elevated-at-logon is requested, the task uses RunLevel Highest so Windows
/// starts the process elevated without a UAC prompt on each login.
/// </summary>
public static class StartupService
{
    public const string TaskFolder = "Spaces4Win";
    public const string TaskName = "Autostart";

    private const string LegacyRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyValueName = "Spaces4Win";

    /// <summary>Sync logon registration with preferences. Safe to call on every start.</summary>
    public static void Apply(bool enabled, bool elevatedAtLogon)
    {
        ClearLegacyRunKey();

        if (!enabled)
        {
            DeleteTask();
            return;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            throw new InvalidOperationException("Could not resolve process path for startup registration.");
        }

        RegisterLogonTask(exe, elevatedAtLogon);
    }

    /// <summary>True when the Task Scheduler task or a legacy Run key is present.</summary>
    public static bool IsEnabled()
    {
        if (TaskExists())
        {
            return true;
        }

        using var key = Registry.CurrentUser.OpenSubKey(LegacyRunKeyPath, writable: false);
        var value = key?.GetValue(LegacyValueName) as string;
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>Kept for older call sites; prefer <see cref="Apply"/>.</summary>
    public static void SetEnabled(bool enabled) =>
        Apply(enabled, elevatedAtLogon: false);

    internal static string BuildCreateArguments(string exePath, bool elevatedAtLogon)
    {
        var rl = elevatedAtLogon ? "HIGHEST" : "LIMITED";
        // /IT = only when user is logged on interactively; /F = overwrite.
        return $"/Create /TN \"{TaskFolder}\\{TaskName}\" /TR \"{exePath}\" /SC ONLOGON /RL {rl} /F /IT";
    }

    internal static string BuildDeleteArguments() =>
        $"/Delete /TN \"{TaskFolder}\\{TaskName}\" /F";

    internal static string BuildQueryArguments() =>
        $"/Query /TN \"{TaskFolder}\\{TaskName}\"";

    private static void RegisterLogonTask(string exePath, bool elevatedAtLogon)
    {
        var args = BuildCreateArguments(exePath, elevatedAtLogon);

        // Unelevated schtasks often returns success for /RL HIGHEST but the task still
        // runs limited. Always register Highest via an elevated schtasks (or when we
        // are already elevated ourselves).
        if (elevatedAtLogon && !ElevationService.IsCurrentProcessElevated())
        {
            var elevated = RunSchtasks(args, elevate: true);
            if (elevated.ExitCode == 0)
            {
                return;
            }

            if (elevated.ExitCode == 1223)
            {
                throw new InvalidOperationException(
                    "Administrator approval is required once to register elevated startup.");
            }

            throw new InvalidOperationException(
                $"Could not register elevated startup task (exit {elevated.ExitCode}): {elevated.StdErr}".Trim());
        }

        var result = RunSchtasks(args, elevate: false);
        if (result.ExitCode == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Could not register startup task (exit {result.ExitCode}): {result.StdErr}".Trim());
    }

    private static void DeleteTask()
    {
        if (!TaskExists())
        {
            return;
        }

        var result = RunSchtasks(BuildDeleteArguments(), elevate: false);
        if (result.ExitCode == 0 || !TaskExists())
        {
            return;
        }

        _ = RunSchtasks(BuildDeleteArguments(), elevate: true);
    }

    private static bool TaskExists()
    {
        var result = RunSchtasks(BuildQueryArguments(), elevate: false);
        return result.ExitCode == 0;
    }

    private static void ClearLegacyRunKey()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(LegacyRunKeyPath, writable: true);
            key?.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        }
        catch
        {
            // best effort
        }
    }

    private static SchtasksResult RunSchtasks(string arguments, bool elevate)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                Arguments = arguments,
                UseShellExecute = elevate,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            if (elevate)
            {
                psi.Verb = "runas";
            }
            else
            {
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
            }

            using var process = Process.Start(psi);
            if (process is null)
            {
                return new SchtasksResult(1, "", "Failed to start schtasks.exe");
            }

            var stdout = "";
            var stderr = "";
            if (!elevate)
            {
                stdout = process.StandardOutput.ReadToEnd();
                stderr = process.StandardError.ReadToEnd();
            }

            if (!process.WaitForExit(60_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return new SchtasksResult(1, stdout, "schtasks timed out");
            }

            return new SchtasksResult(process.ExitCode, stdout, stderr);
        }
        catch (System.ComponentModel.Win32Exception ex) when (elevate && ex.NativeErrorCode == 1223)
        {
            return new SchtasksResult(1223, "", "UAC cancelled");
        }
        catch (Exception ex)
        {
            return new SchtasksResult(1, "", ex.Message);
        }
    }

    private readonly record struct SchtasksResult(int ExitCode, string StdOut, string StdErr);
}
