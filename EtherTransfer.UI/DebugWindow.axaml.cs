using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;

namespace EtherTransfer.UI;

public partial class DebugWindow : Window
{
    public DebugWindow()
    {
        InitializeComponent();
    }

    private async void CopyLog_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindow mainWindow)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                var fullLog = string.Join(Environment.NewLine, mainWindow.DebugMessages.Select(m => m.Text));
                await topLevel.Clipboard.SetTextAsync(fullLog);
            }
        }
    }
}
