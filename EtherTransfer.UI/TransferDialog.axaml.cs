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
        get => _isSenderMode && !IsProgressMode; 
        set { _isSenderMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsReceiverMode)); } 
    }
    
    public bool IsReceiverMode => !_isSenderMode && !IsProgressMode;

    private bool _isProgressMode;
    public bool IsProgressMode
    {
        get => _isProgressMode;
        set 
        { 
            _isProgressMode = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(IsSenderMode)); 
            OnPropertyChanged(nameof(IsReceiverMode)); 
        }
    }

    private string _transferFileName = "";
    public string TransferFileName { get => _transferFileName; set { _transferFileName = value; OnPropertyChanged(); } }

    private long _transferTotalBytes;
    public long TransferTotalBytes { get => _transferTotalBytes; set { _transferTotalBytes = value; OnPropertyChanged(); } }

    private long _transferSentBytes;
    public long TransferSentBytes { get => _transferSentBytes; set { _transferSentBytes = value; OnPropertyChanged(); } }

    private string _transferProgressText = "";
    public string TransferProgressText { get => _transferProgressText; set { _transferProgressText = value; OnPropertyChanged(); } }

    private string _transferSpeedText = "";
    public string TransferSpeedText { get => _transferSpeedText; set { _transferSpeedText = value; OnPropertyChanged(); } }

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
        // Don't close! Just return the path so the transfer starts.
        // We will switch to progress mode automatically when the first ProgressUpdated event fires.
        _receiverTcs?.TrySetResult((true, SavePath));
    }

    public void UpdateProgress(EtherTransfer.Transfer.TransferProgressEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!IsProgressMode) IsProgressMode = true;

            TransferFileName = e.CurrentFile;
            TransferTotalBytes = e.TotalBytes;
            TransferSentBytes = e.BytesSent;
            
            double mbSent = e.BytesSent / 1024.0 / 1024.0;
            double mbTotal = e.TotalBytes / 1024.0 / 1024.0;
            TransferProgressText = $"{mbSent:F1} MB / {mbTotal:F1} MB";
            TransferSpeedText = $"{e.SpeedMbPerSec:F1} MB/s";

            if (e.BytesSent >= e.TotalBytes)
            {
                // Auto-close after completion
                Task.Delay(1500).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Close()));
            }
        });
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
