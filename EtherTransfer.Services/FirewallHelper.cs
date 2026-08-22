using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using EtherTransfer.Core.Models;

namespace EtherTransfer.Services;

public static class FirewallHelper
{
    private const string RuleName = "EtherTransfer";

    public static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
            return false;

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

    private static bool IsRuleConfiguredForPath(string exePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall show rule name=\"{RuleName}\" verbose",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {

                return output.Contains(exePath, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {

        }

        return false;
    }

    public static void EnsureFirewallRule(Action<string, LogLevel>? logger = null)
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (!IsAdministrator())
        {

            return;
        }

        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                logger?.Invoke("Unable to resolve process path for firewall rule registration.", LogLevel.Warning);
                return;
            }

            if (IsRuleConfiguredForPath(exePath))
            {
                logger?.Invoke($"Windows Firewall rule is already active for: {exePath}", LogLevel.Info);
                return;
            }

            logger?.Invoke($"Registering Windows Firewall rule for portable executable: {exePath}", LogLevel.Info);

            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{exePath}\" enable=yes profile=private,public",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                if (process.WaitForExit(4000))
                {
                    if (process.ExitCode == 0)
                    {
                        logger?.Invoke("Windows Firewall rule successfully registered.", LogLevel.Info);
                    }
                    else
                    {
                        string err = process.StandardError.ReadToEnd();
                        logger?.Invoke($"Firewall registration exited with code {process.ExitCode}: {err}", LogLevel.Warning);
                    }
                }
                else
                {
                    try { process.Kill(); } catch { }
                    logger?.Invoke("Firewall registration command timed out.", LogLevel.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.Invoke($"Firewall rule registration encountered an exception: {ex.Message}", LogLevel.Warning);
        }
    }
}
