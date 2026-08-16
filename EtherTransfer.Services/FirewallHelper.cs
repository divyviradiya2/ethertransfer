using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using EtherTransfer.Core.Models;

namespace EtherTransfer.Services;

/// <summary>
/// Provides Windows Defender Firewall configuration helpers for EtherTransfer.
/// </summary>
public static class FirewallHelper
{
    private const string RuleName = "EtherTransfer";

    /// <summary>
    /// Checks whether the current process is running with Administrator privileges on Windows.
    /// </summary>
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

    /// <summary>
    /// Checks if a firewall rule already exists for the specified executable path.
    /// </summary>
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
                // Check if the current exePath is already present in the registered rules
                return output.Contains(exePath, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // On any inspection error, fall back to false so add rule can proceed
        }

        return false;
    }

    /// <summary>
    /// Ensures that an inbound Windows Defender Firewall rule exists for the current executable path across private and public profiles.
    /// Only runs when running on Windows with Administrator privileges (such as the Portable edition).
    /// If an active rule for this path already exists, redundant additions are skipped.
    /// </summary>
    /// <param name="logger">Optional structured logger callback.</param>
    public static void EnsureFirewallRule(Action<string, LogLevel>? logger = null)
    {
        if (!OperatingSystem.IsWindows())
            return;

        if (!IsAdministrator())
        {
            // Regular installed builds run as standard user (asInvoker) where firewall was set during installation
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

            // Check if rule already exists for this exact path
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
