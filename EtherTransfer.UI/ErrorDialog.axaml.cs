using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EtherTransfer.UI;

public partial class ErrorDialog : Window
{
    public ErrorDialog()
    {
        InitializeComponent();
    }

    public ErrorDialog(string message) : this()
    {
        var textBlock = this.FindControl<TextBlock>("MessageText");
        if (textBlock != null)
            textBlock.Text = message;
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
