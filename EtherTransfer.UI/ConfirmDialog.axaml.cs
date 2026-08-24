using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EtherTransfer.UI;

public partial class ConfirmDialog : Window
{
    private bool _result = false;

    public ConfirmDialog()
    {
        InitializeComponent();
        WindowsTitleBarTheme.EnableDarkMode(this);
    }

    public ConfirmDialog(string message, string title = "Cancel Transfer?", string yesText = "Yes, Cancel", string noText = "No, Keep") : this()
    {
        TitleText.Text = title;
        MessageText.Text = message;
        YesButton.Content = yesText;
        NoButton.Content = noText;
    }

    private void Yes_Click(object? sender, RoutedEventArgs e)
    {
        _result = true;
        Close(true);
    }

    private void No_Click(object? sender, RoutedEventArgs e)
    {
        _result = false;
        Close(false);
    }

    public static async Task<bool> ShowAsync(Window? owner, string message, string title = "Cancel Transfer?", string yesText = "Yes, Cancel", string noText = "No, Keep")
    {
        var dialog = new ConfirmDialog(message, title, yesText, noText);
        if (owner != null && owner.IsVisible)
        {
            return await dialog.ShowDialog<bool>(owner);
        }
        else
        {
            var tcs = new TaskCompletionSource<bool>();
            dialog.Closed += (_, _) => tcs.TrySetResult(dialog._result);
            dialog.Show();
            return await tcs.Task;
        }
    }
}
