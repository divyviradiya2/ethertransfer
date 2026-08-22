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

public class CompletedItemViewModel
{
    public string Name { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public bool IsFile => !IsFolder;
    public bool IsSuccess { get; set; } = true;
    public bool IsFailed => !IsSuccess;
}

public partial class TransferDialog : Window, INotifyPropertyChanged
{
    private bool _isSenderMode;
    public bool IsSenderMode
    {
        get => _isSenderMode && !IsProgressMode && !IsSuccessMode && !IsFailureMode;
        set { _isSenderMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsReceiverMode)); }
    }

    public bool IsReceiverMode => !_isSenderMode && !IsProgressMode && !IsSuccessMode && !IsFailureMode;

    private bool _isProgressMode;
    public bool IsProgressMode
    {
        get => _isProgressMode && !IsSuccessMode && !IsFailureMode;
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
        get => _isSuccessMode && !IsFailureMode;
        set
        {
            _isSuccessMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSenderMode));
            OnPropertyChanged(nameof(IsReceiverMode));
            OnPropertyChanged(nameof(IsProgressMode));
            OnPropertyChanged(nameof(IsFullSuccessMode));
            OnPropertyChanged(nameof(IsFailureMode));
        }
    }

    private bool _isPartialSuccessMode;
    public bool IsPartialSuccessMode
    {
        get => _isPartialSuccessMode;
        set { _isPartialSuccessMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsFullSuccessMode)); }
    }

    public bool IsFullSuccessMode => IsSuccessMode && !IsPartialSuccessMode;

    private bool _isFailureMode;
    public bool IsFailureMode
    {
        get => _isFailureMode;
        set
        {
            _isFailureMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSenderMode));
            OnPropertyChanged(nameof(IsReceiverMode));
            OnPropertyChanged(nameof(IsProgressMode));
            OnPropertyChanged(nameof(IsSuccessMode));
            OnPropertyChanged(nameof(IsFullSuccessMode));
        }
    }

    private string _failureTitle = "Transfer Cancelled";
    public string FailureTitle { get => _failureTitle; set { _failureTitle = value; OnPropertyChanged(); } }

    private string _failureMessage = "The transfer was cancelled.";
    public string FailureMessage { get => _failureMessage; set { _failureMessage = value; OnPropertyChanged(); } }

    private string _failureSubDetail = "No files were saved to your device. Any temporary data was safely cleaned up.";
    public string FailureSubDetail { get => _failureSubDetail; set { _failureSubDetail = value; OnPropertyChanged(); } }

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

    public System.Collections.ObjectModel.ObservableCollection<CompletedItemViewModel> CompletedItems { get; } = new();

    public void SetCompletedElements(System.Collections.Generic.List<string> elements)
    {
        SetTransferElements(elements, null);
    }

    public void SetTransferElements(System.Collections.Generic.List<string>? completedElements, System.Collections.Generic.List<string>? failedElements = null)
    {
        CompletedItems.Clear();

        if (completedElements != null)
        {
            foreach (var name in completedElements)
            {
                bool isFolder = !System.IO.Path.HasExtension(name);
                CompletedItems.Add(new CompletedItemViewModel
                {
                    Name = name,
                    IsFolder = isFolder,
                    IsSuccess = true
                });
            }
        }

        if (failedElements != null)
        {
            foreach (var name in failedElements)
            {
                bool isFolder = !System.IO.Path.HasExtension(name);
                CompletedItems.Add(new CompletedItemViewModel
                {
                    Name = name,
                    IsFolder = isFolder,
                    IsSuccess = false
                });
            }
        }

        CompletedElementsList = string.Join("\n", System.Linq.Enumerable.Select(CompletedItems, e => $"{(e.IsSuccess ? "[OK]" : "[FAILED]")}  {e.Name}"));
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

    private bool _isCancelled = false;
    public bool IsCancelled => _isCancelled;

    private async void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        if (!IsProgressMode)
        {
            _isCancelled = true;
            ForceClose();
            return;
        }

        bool confirm = await NativeDialogHelper.ShowConfirmCancelDialogAsync("Are you sure you want to cancel the transfer?", "Cancel Transfer");
        if (confirm)
        {
            _isCancelled = true;
            CancelTransfer();
            // Do not ForceClose immediately. OnTransferFinished will show Partial Success
            // if any items finished, or close cleanly if 0 items finished.
        }
    }

    public bool CanOpenFolder => !string.IsNullOrWhiteSpace(SavePath) && Directory.Exists(SavePath);

    private void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string path = SavePath;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("xdg-open", $"\"{path}\"") { UseShellExecute = true });
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = true });
            }
        }
        catch { }
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

    private DateTime _lastUiUpdate = DateTime.MinValue;

    public void UpdateProgress(EtherTransfer.Transfer.TransferProgressEventArgs e)
    {
        if (_isCancelled || _isForceClosing || IsSuccessMode) return;

        var now = DateTime.UtcNow;
        bool isComplete = e.BytesSent >= e.TotalBytes;
        if (!isComplete && (now - _lastUiUpdate).TotalMilliseconds < 50)
            return;

        _lastUiUpdate = now;

        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_isCancelled || _isForceClosing || IsSuccessMode) return;

            if (!IsProgressMode) IsProgressMode = true;

            TransferFileName = e.CurrentFile;
            TransferItemCountText = $"({e.CurrentElementIndex}/{e.TotalElements})";
            TransferTotalBytes = e.TotalBytes;
            TransferSentBytes = e.BytesSent;

            TransferProgressText = $"{EtherTransfer.Core.FormatHelper.FormatSize(e.BytesSent)} / {EtherTransfer.Core.FormatHelper.FormatSize(e.TotalBytes)}";
            TransferSpeedText = $"{e.SpeedMbPerSec:F1} MB/s";
        });
    }

    private void Decline_Click(object? sender, RoutedEventArgs e)
    {
        _isCancelled = true;
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

    public void ForceClose()
    {
        _isCancelled = true;
        _isForceClosing = true;
        CancelTransfer();
        _receiverTcs?.TrySetResult((false, "", default));
        Close();
    }

    public void CancelTransfer()
    {
        _isCancelled = true;
        _senderCts?.Cancel();
        _receiverCancelCts?.Cancel();
    }

    private bool _isForceClosing = false;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_isForceClosing || IsSuccessMode || IsFailureMode)
        {
            base.OnClosing(e);
            return;
        }

        // Only prompt for confirmation if files are actively streaming
        if (IsProgressMode)
        {
            e.Cancel = true; // Prevent immediate close while confirming
            
            bool confirm = await NativeDialogHelper.ShowConfirmCancelDialogAsync(
                "Are you sure you want to cancel the transfer?", 
                "Cancel Transfer");
                
            if (confirm)
            {
                _isCancelled = true;
                CancelTransfer();
                // Let OnTransferFinished handle whether to display Partial Success or close
            }
            return;
        }

        // In Sender mode (waiting for accept) or Receiver mode (incoming prompt), close immediately
        _isCancelled = true;
        _isForceClosing = true;
        CancelTransfer();
        _receiverTcs?.TrySetResult((false, "", default));
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _isCancelled = true;
        _receiverCancelCts?.Cancel(); // Cancel any ongoing transfer
        // Ensure TCS is completed if window is closed via any route
        _receiverTcs?.TrySetResult((false, "", default));
        base.OnClosed(e);
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
