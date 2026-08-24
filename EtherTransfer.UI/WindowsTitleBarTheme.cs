using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Styling;

namespace EtherTransfer.UI;

public static class WindowsTitleBarTheme
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private const int GWLP_WNDPROC = -4;
    private const uint WM_NCACTIVATE = 0x0086;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static readonly ConcurrentDictionary<IntPtr, (WndProcDelegate Delegate, IntPtr OldWndProc)> _subclassedWindows = new();

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public static void EnableDarkMode(Window window)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        window.Opened += (s, e) =>
        {
            ApplyTheme(window);
            SubclassWindow(window);
        };

        window.ActualThemeVariantChanged += (s, e) => ApplyTheme(window);

        window.Closed += (s, e) => UnhookWindow(window);

        ApplyTheme(window);
        SubclassWindow(window);
    }

    public static void ApplyTheme(Window window)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero)
                return;

            bool isDark = window.ActualThemeVariant == ThemeVariant.Dark || IsWindowsSystemInDarkMode();
            int useDarkMode = isDark ? 1 : 0;

            if (isDark)
            {
                try
                {
                    SetWindowTheme(hwnd, "DarkMode_Explorer", null);
                }
                catch { }

                int blackColor = 0x00000000;
                int textColor = 0x00F5F4F4;
                int borderColor = 0x00241E1E;

                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref blackColor, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
            }

            int hr = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
            if (hr != 0)
            {

                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDarkMode, sizeof(int));
            }

            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }
        catch
        {

        }
    }

    private static void SubclassWindow(Window window)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero || _subclassedWindows.ContainsKey(hwnd))
                return;

            WndProcDelegate newWndProc = (hWnd, msg, wParam, lParam) =>
            {
                if (_subclassedWindows.TryGetValue(hWnd, out var info))
                {
                    if (msg == WM_NCACTIVATE)
                    {

                        if (wParam == IntPtr.Zero)
                        {
                            return CallWindowProc(info.OldWndProc, hWnd, msg, (IntPtr)1, lParam);
                        }
                    }
                    return CallWindowProc(info.OldWndProc, hWnd, msg, wParam, lParam);
                }
                return IntPtr.Zero;
            };

            IntPtr newWndProcPtr = Marshal.GetFunctionPointerForDelegate(newWndProc);
            IntPtr oldWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC, newWndProcPtr);

            if (oldWndProc != IntPtr.Zero)
            {
                _subclassedWindows[hwnd] = (newWndProc, oldWndProc);
            }
        }
        catch
        {
        }
    }

    private static void UnhookWindow(Window window)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd != IntPtr.Zero && _subclassedWindows.TryRemove(hwnd, out var info))
            {
                SetWindowLongPtr(hwnd, GWLP_WNDPROC, info.OldWndProc);
            }
        }
        catch
        {
        }
    }

    private static bool IsWindowsSystemInDarkMode()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key != null)
            {
                var val = key.GetValue("AppsUseLightTheme");
                if (val is int intVal)
                {
                    return intVal == 0;
                }
            }
        }
        catch
        {
        }
        return false;
    }
}

