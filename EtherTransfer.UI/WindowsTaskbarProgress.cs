using System;
using System.Runtime.InteropServices;

namespace EtherTransfer.UI;

public enum TaskbarProgressState
{
    NoProgress = 0,
    Indeterminate = 0x1,
    Normal = 0x2,
    Error = 0x4,
    Paused = 0x8
}

[ComImport]
[Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITaskbarList3
{

    [PreserveSig] void HrInit();
    [PreserveSig] void AddTab(IntPtr hwnd);
    [PreserveSig] void DeleteTab(IntPtr hwnd);
    [PreserveSig] void ActivateTab(IntPtr hwnd);
    [PreserveSig] void SetActiveAlt(IntPtr hwnd);

    [PreserveSig] void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

    [PreserveSig] void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
    [PreserveSig] void SetProgressState(IntPtr hwnd, TaskbarProgressState tbpFlags);
    [PreserveSig] void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
    [PreserveSig] void UnregisterTab(IntPtr hwndTab);
    [PreserveSig] void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
    [PreserveSig] void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, uint dwReserved);
    [PreserveSig] void ThumbBarAddButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);
    [PreserveSig] void ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);
    [PreserveSig] void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
    [PreserveSig] void SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string pszDescription);
    [PreserveSig] void SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string pszTip);
    [PreserveSig] void SetThumbnailClip(IntPtr hwnd, IntPtr prcClip);
}

[ComImport]
[Guid("56fdf344-fd6d-11d0-958a-006097c9a090")]
[ClassInterface(ClassInterfaceType.None)]
internal class TaskbarList { }

public static class WindowsTaskbarProgress
{
    private static ITaskbarList3? _taskbarList;
    private static bool _initialized = false;
    private static bool _supported = false;
    private static readonly object _lock = new();

    private static void EnsureInitialized()
    {
        if (_initialized) return;

        lock (_lock)
        {
            if (_initialized) return;
            _initialized = true;

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _supported = false;
                return;
            }

            try
            {
                var instance = (ITaskbarList3)new TaskbarList();
                instance.HrInit();
                _taskbarList = instance;
                _supported = true;
            }
            catch
            {
                _supported = false;
                _taskbarList = null;
            }
        }
    }

    public static void SetProgressState(IntPtr hwnd, TaskbarProgressState state)
    {
        if (hwnd == IntPtr.Zero) return;
        EnsureInitialized();
        if (!_supported || _taskbarList == null) return;

        try
        {
            lock (_lock)
            {
                _taskbarList?.SetProgressState(hwnd, state);
            }
        }
        catch
        {

        }
    }

    public static void SetProgressValue(IntPtr hwnd, ulong completed, ulong total)
    {
        if (hwnd == IntPtr.Zero) return;
        EnsureInitialized();
        if (!_supported || _taskbarList == null) return;

        try
        {
            lock (_lock)
            {
                _taskbarList?.SetProgressValue(hwnd, completed, total);
            }
        }
        catch
        {

        }
    }
}

