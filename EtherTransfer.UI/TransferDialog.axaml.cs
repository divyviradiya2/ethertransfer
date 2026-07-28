using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace EtherTransfer.UI;

public partial class TransferDialog : Window, INotifyPropertyChanged
{
    private bool _isSenderMode;
    public bool IsSenderMode 
    { 
        get => _isSenderMode; 
        set { _isSenderMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsReceiverMode)); } 
    }
    
    public bool IsReceiverMode => !IsSenderMode;

    private string _waitingText = "";
    public string WaitingText { get => _waitingText; set { _waitingText = value; OnPropertyChanged(); } }

    private string _incomingRequestText = "";
    public string IncomingRequestText { get => _incomingRequestText; set { _incomingRequestText = value; OnPropertyChanged(); } }

    private string _savePath = "";
    public string SavePath { get => _savePath; set { _savePath = value; OnPropertyChanged(); } }

    private CancellationTokenSource? _senderCts;
    private TaskCompletionSource<(bool accepted, string path)>? _receiverTcs;

    public TransferDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    // Factory method for Sender Mode
    public static TransferDialog CreateSender(string targetName, CancellationTokenSource cts)
    {
        var dialog = new TransferDialog
        {
            IsSenderMode = true,
            WaitingText = $"Waiting for {targetName} to accept...",
            _senderCts = cts
        };
        return dialog;
    }

    // Factory method for Receiver Mode
    public static TransferDialog CreateReceiver(string requestText, TaskCompletionSource<(bool, string)> tcs)
    {
        var dialog = new TransferDialog
        {
            IsSenderMode = false,
            IncomingRequestText = requestText,
            _receiverTcs = tcs,
            SavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        };
        return dialog;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        _senderCts?.Cancel();
        Close();
    }

    private void Accept_Click(object? sender, RoutedEventArgs e)
    {
        _receiverTcs?.TrySetResult((true, SavePath));
        Close();
    }

    private void Decline_Click(object? sender, RoutedEventArgs e)
    {
        _receiverTcs?.TrySetResult((false, ""));
        Close();
    }

    private async void ChangeFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose save location"
        });

        if (folders.Count > 0)
        {
            var path = folders[0].TryGetLocalPath() ?? folders[0].Path.LocalPath;
            if (!string.IsNullOrEmpty(path))
            {
                SavePath = path;
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        // Ensure TCS is completed if window is closed via the X button
        _receiverTcs?.TrySetResult((false, ""));
        base.OnClosed(e);
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
