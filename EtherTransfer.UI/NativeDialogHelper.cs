using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace EtherTransfer.UI;

public static class NativeDialogHelper
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_YESNO = 0x00000004;
    private const uint MB_ICONWARNING = 0x00000030;
    private const int IDYES = 6;

    /// <summary>
    /// Shows a native OS confirmation dialog (Yes/No). Returns true if Yes is clicked.
    /// </summary>
    public static async Task<bool> ShowConfirmCancelDialogAsync(string message, string title)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return await Task.Run(() =>
            {
                int result = MessageBoxW(IntPtr.Zero, message, title, MB_YESNO | MB_ICONWARNING);
                return result == IDYES;
            });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return await Task.Run(() =>
            {
                // Try zenity first (GNOME/Ubuntu)
                try
                {
                    using var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "zenity",
                        Arguments = $"--question --text=\"{message}\" --title=\"{title}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    process?.WaitForExit();
                    return process?.ExitCode == 0;
                }
                catch
                {
                    // Fallback to kdialog (KDE/Kubuntu)
                    try
                    {
                        using var process = Process.Start(new ProcessStartInfo
                        {
                            FileName = "kdialog",
                            Arguments = $"--yesno \"{message}\" --title \"{title}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        process?.WaitForExit();
                        return process?.ExitCode == 0; // kdialog returns 0 for yes, 1 for no
                    }
                    catch
                    {
                        // If all fails, just return true so they can cancel
                        return true;
                    }
                }
            });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return await Task.Run(() =>
            {
                try
                {
                    var script = $"display dialog \"{message}\" with title \"{title}\" buttons {{\"No\", \"Yes\"}} default button \"No\"";
                    using var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "osascript",
                        Arguments = $"-e '{script}'",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    });
                    process?.WaitForExit();
                    var output = process?.StandardOutput.ReadToEnd() ?? "";
                    return output.Contains("button returned:Yes");
                }
                catch
                {
                    return true;
                }
            });
        }

        return true; // Default fallback for unknown OS
    }
}
