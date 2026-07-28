using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Runtime.Versioning;

namespace EtherTransfer.Services;

public static class FirewallManager
{
    private const string RuleName = "Ether Transfer";

    public static async Task EnsureFirewallRulesAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            await EnsureWindowsFirewallAsync();
        }
        // macOS and Linux are excluded as per plan
    }

    [SupportedOSPlatform("windows")]
    private static Task EnsureWindowsFirewallAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                string exePath = Environment.ProcessPath ?? string.Empty;
                if (string.IsNullOrEmpty(exePath)) return;

                if (IsWindowsFirewallRulePresent(exePath))
                {
                    return; // Rule already exists
                }

                // Rule does not exist, prompt for UAC and add it
                AddWindowsFirewallRule(exePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to configure firewall: {ex}");
            }
        });
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsFirewallRulePresent(string exePath)
    {
        try
        {
            Type? type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type == null) return false;

            dynamic? fwPolicy2 = Activator.CreateInstance(type);
            if (fwPolicy2 == null) return false;

            dynamic rules = fwPolicy2.Rules;
            foreach (dynamic rule in rules)
            {
                if (rule.Name == RuleName)
                {
                    string ruleApp = rule.ApplicationName;
                    if (string.Equals(ruleApp, exePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        catch
        {
            return false; // Fallback to false, meaning we will try to add it
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindowsFirewallRule(string exePath)
    {
        // We use netsh to add the rule via UAC
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{exePath}\" enable=yes profile=any",
            UseShellExecute = true,
            Verb = "runas", // Triggers UAC
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using var process = Process.Start(psi);
            process?.WaitForExit();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User cancelled the UAC prompt
        }
    }
}
