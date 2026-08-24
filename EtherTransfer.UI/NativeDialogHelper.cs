using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace EtherTransfer.UI;

public static class NativeDialogHelper
{
    public static async Task<bool> ShowConfirmCancelDialogAsync(string message, string title)
    {
        Avalonia.Controls.Window? mainWindow = null;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            mainWindow = desktop.MainWindow;
        }

        return await ConfirmDialog.ShowAsync(mainWindow, message, title);
    }
}
