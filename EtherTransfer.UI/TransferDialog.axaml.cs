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
        get => _isSenderMode && !IsProgressMode && !IsSuccessMode;
        set { _isSenderMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsReceiverMode)); }
    }

    public bool IsReceiverMode => !_isSenderMode && !IsProgressMode && !IsSuccessMode;

    private bool _isProgressMode;
    public bool IsProgressMode
    {
        get => _isProgressMode && !IsSuccessMode;
        set
        {
            _isProgressMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSenderMode));
            OnPropertyChanged(nameof(IsReceiverMode));
        }
    }

    private bool _isSuccessMode;
    public bool IsSuccessMode
    {
        get => _isSuccessMode;
        set
        {
            _isSuccessMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSenderMode));
            OnPropertyChanged(nameof(IsReceiverMode));
            OnPropertyChanged(nameof(IsProgressMode));
            OnPropertyChanged(nameof(IsFullSuccessMode));
        }
    }

    private bool _isPartialSuccessMode;
    public bool IsPartialSuccessMode
    {
        get => _isPartialSuccessMode;
        set { _isPartialSuccessMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsFullSuccessMode)); }
    }

    public bool IsFullSuccessMode => IsSuccessMode && !IsPartialSuccessMode;

    private string _transferFileName = "";
    public string TransferFileName { get => _transferFileName; set { _transferFileName = value; OnPropertyChanged(); } }

    private string _transferItemCountText = "";
    public string TransferItemCountText { get => _transferItemCountText; set { _transferItemCountText = value; OnPropertyChanged(); } }

    private long _transferTotalBytes;
    public long TransferTotalBytes 
    { 
        get => _transferTotalBytes; 
        set 
        { 
            _transferTotalBytes = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(TransferProgressText)); 
            OnPropertyChanged(nameof(TransferPercentageText)); 
            OnPropertyChanged(nameof(TransferFinalSizeText)); 
        } 
    }

    private string _transferFinalSizeText = "";
    public string TransferFinalSizeText 
    { 
        get => string.IsNullOrEmpty(_transferFinalSizeText) ? EtherTransfer.Core.FormatHelper.FormatSize(_transferTotalBytes) : _transferFinalSizeText; 
        set { _transferFinalSizeText = value; OnPropertyChanged(); } 
    }

    private string _completedElementsList = "";
    public string CompletedElementsList { get => _completedElementsList; set { _completedElementsList = value; OnPropertyChanged(); } }

    public void SetCompletedElements(System.Collections.Generic.List<string> elements)
    {
        CompletedElementsList = string.Join("\n", elements);
    }

    private long _transferSentBytes;
    public long TransferSentBytes 
    { 
        get => _transferSentBytes; 
        set 
        { 
            _transferSentBytes = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(TransferProgressText)); 
            OnPropertyChanged(nameof(TransferPercentageText)); 
        } 
    }

    public string TransferPercentageText
    {
        get
        {
            if (_transferTotalBytes == 0) return "0%";
            double percent = (double)_transferSentBytes / _transferTotalBytes * 100;
            if (percent > 100) percent = 100;
            return $"{percent:F0}%";
        }
    }

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
    private CancellationTokenSource? _receiverCancelCts;
    private TaskCompletionSource<(bool accepted, string path, CancellationToken cancelToken)>? _receiverTcs;

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
    public static TransferDialog CreateReceiver(string requestText, long totalBytes, TaskCompletionSource<(bool, string, CancellationToken)> tcs, CancellationTokenSource cancelCts)
    {
        var dialog = new TransferDialog
        {
            IsSenderMode = false,
            IncomingRequestText = requestText,
            TransferTotalBytes = totalBytes,
            _receiverTcs = tcs,
            _receiverCancelCts = cancelCts,
            SavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        };
        return dialog;
    }

    private async void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        if (!IsProgressMode)
        {
            _isForceClosing = true;
            CancelTransfer();
            Close();
            return;
        }

        bool confirm = await NativeDialogHelper.ShowConfirmCancelDialogAsync("Are you sure you want to cancel the transfer?", "Cancel Transfer");
        if (confirm)
        {
            CancelTransfer();
            // We do not close here. We let the background task throw the cancellation exception
            // and MainWindow will transition this dialog to IsPartialSuccessMode.
        }
    }

    private void Done_Click(object? sender, RoutedEventArgs e)
    {
        _isForceClosing = true;
        Close();
    }

    private async void Accept_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string dir = SavePath;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            var driveInfo = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir)) ?? dir);
            if (driveInfo.AvailableFreeSpace < TransferTotalBytes)
            {
                var neededStr = EtherTransfer.Core.FormatHelper.FormatSize(TransferTotalBytes);
                var freeStr = EtherTransfer.Core.FormatHelper.FormatSize(driveInfo.AvailableFreeSpace);
                
                var errorDialog = new ErrorDialog($"Not enough free space on the selected disk.\n\nRequired: {neededStr}\nAvailable: {freeStr}");
                await errorDialog.ShowDialog(this);
                return;
            }
        }
        catch (Exception ex)
        {
            // Fallback if drive check fails (e.g. permission issues or unknown path format)
            System.Diagnostics.Debug.WriteLine($"Drive check failed: {ex.Message}");
        }

        // Don't close! Just return the path so the transfer starts.
        // We will switch to progress mode automatically when the first ProgressUpdated event fires.
        _receiverTcs?.TrySetResult((true, SavePath, _receiverCancelCts!.Token));
    }

    private bool _successQueued = false;

    public void UpdateProgress(EtherTransfer.Transfer.TransferProgressEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (IsSuccessMode) return;

            if (!IsProgressMode) IsProgressMode = true;

            TransferFileName = e.CurrentFile;
            TransferItemCountText = $"({e.CurrentElementIndex}/{e.TotalElements})";
            TransferTotalBytes = e.TotalBytes;
            TransferSentBytes = e.BytesSent;

            TransferProgressText = $"{EtherTransfer.Core.FormatHelper.FormatSize(e.BytesSent)} / {EtherTransfer.Core.FormatHelper.FormatSize(e.TotalBytes)}";
            TransferSpeedText = $"{e.SpeedMbPerSec:F1} MB/s";

            if (e.BytesSent >= e.TotalBytes && !_successQueued)
            {
                _successQueued = true;
                // Wait a tiny bit so the user sees the progress bar hit 100%
                Task.Delay(500).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsSuccessMode = true;
                }));
            }
        });
    }

    private void Decline_Click(object? sender, RoutedEventArgs e)
    {
        _receiverTcs?.TrySetResult((false, "", default));
        _isForceClosing = true;
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

    public void CancelTransfer()
    {
        _senderCts?.Cancel();
        _receiverCancelCts?.Cancel();
    }

    private bool _isForceClosing = false;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_isForceClosing || IsSuccessMode)
        {
            base.OnClosing(e);
            return;
        }

        if (IsProgressMode || IsSenderMode || IsReceiverMode)
        {
            e.Cancel = true; // Prevent immediate close
            
            if (!IsProgressMode)
            {
                _isForceClosing = true;
                CancelTransfer();
                Close();
                return;
            }

            // Show native OS dialog
            bool confirm = await NativeDialogHelper.ShowConfirmCancelDialogAsync(
                "Are you sure you want to cancel the transfer?", 
                "Cancel Transfer");
                
            if (confirm)
            {
                CancelTransfer();
                // We do not close here. Let MainWindow catch the cancellation and set IsPartialSuccessMode
            }
        }
        else
        {
            base.OnClosing(e);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _receiverCancelCts?.Cancel(); // Cancel any ongoing transfer
        // Ensure TCS is completed if window is closed via the X button
        _receiverTcs?.TrySetResult((false, "", default));
        base.OnClosed(e);
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
