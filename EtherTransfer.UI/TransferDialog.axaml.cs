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
    public event Action? TransferStarted;
    private bool _hasTriggeredTransferStarted = false;

    private string _peerDeviceName = "";
    public string PeerDeviceName
    {
        get => _peerDeviceName;
        set
        {
            _peerDeviceName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressTitle));
            OnPropertyChanged(nameof(WindowTitle));
            this.Title = WindowTitle;
        }
    }

    public string ProgressTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_peerDeviceName))
            {
                return _isSenderMode
                    ? $"Sending to {_peerDeviceName}"
                    : $"Receiving from {_peerDeviceName}";
            }
            return "Transferring";
        }
    }

    public string WindowTitle
    {
        get
        {
            if (IsProgressMode)
            {
                string actionPrefix = !string.IsNullOrWhiteSpace(_peerDeviceName)
                    ? (_isSenderMode ? $"Sending to {_peerDeviceName}" : $"Receiving from {_peerDeviceName}")
                    : "Transferring";

                if (_transferTotalBytes > 0)
                {
                    double percent = Math.Clamp((double)_transferSentBytes / _transferTotalBytes * 100, 0, 100);
                    return $"EtherTransfer - {actionPrefix} ({percent:F0}%)";
                }
                return $"EtherTransfer - {actionPrefix}...";
            }
            if (IsSuccessMode) return "EtherTransfer - Transfer Complete";
            if (IsFailureMode) return $"EtherTransfer - {FailureTitle}";
            if (IsSenderMode) return !string.IsNullOrWhiteSpace(_peerDeviceName) ? $"EtherTransfer - Waiting for {_peerDeviceName}" : "EtherTransfer - Waiting for Peer";
            return "EtherTransfer - Incoming Transfer Request";
        }
    }

    private IntPtr GetWindowHandle()
    {
        try
        {
            return this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private bool _isSenderMode;
    public bool IsSenderMode
    {
        get => _isSenderMode && !IsProgressMode && !IsSuccessMode && !IsFailureMode;
        set
        {
            _isSenderMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsReceiverMode));
            OnPropertyChanged(nameof(WindowTitle));
            this.Title = WindowTitle;
        }
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
            OnPropertyChanged(nameof(WindowTitle));
            this.Title = WindowTitle;
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
            OnPropertyChanged(nameof(WindowTitle));
            this.Title = WindowTitle;
        }
    }

    private bool _isPartialSuccessMode;
    public bool IsPartialSuccessMode
    {
        get => _isPartialSuccessMode;
        set { _isPartialSuccessMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsFullSuccessMode)); OnPropertyChanged(nameof(WindowTitle)); this.Title = WindowTitle; }
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
            OnPropertyChanged(nameof(WindowTitle));
            this.Title = WindowTitle;
        }
    }

    public bool IsSender => _isSenderMode;

    private string _failureTitle = "Transfer Cancelled";
    public string FailureTitle { get => _failureTitle; set { _failureTitle = value; OnPropertyChanged(); OnPropertyChanged(nameof(WindowTitle)); this.Title = WindowTitle; } }

    private string _failureMessage = "The transfer was cancelled.";
    public string FailureMessage { get => _failureMessage; set { _failureMessage = value; OnPropertyChanged(); } }

    private string _failureSubDetail = "No files were transferred.";
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
            OnPropertyChanged(nameof(WindowTitle));
            this.Title = WindowTitle;
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
            OnPropertyChanged(nameof(WindowTitle));
            this.Title = WindowTitle;
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
        WindowsTitleBarTheme.EnableDarkMode(this);
    }

    public static TransferDialog CreateSender(string targetName, CancellationTokenSource cts)
    {
        var dialog = new TransferDialog
        {
            IsSenderMode = true,
            PeerDeviceName = targetName,
            WaitingText = $"Waiting for {targetName} to accept...",
            _senderCts = cts
        };
        return dialog;
    }

    public static TransferDialog CreateReceiver(string senderName, string requestText, long totalBytes, TaskCompletionSource<(bool, string, CancellationToken)> tcs, CancellationTokenSource cancelCts)
    {
        var dialog = new TransferDialog
        {
            IsSenderMode = false,
            PeerDeviceName = senderName,
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

    private bool _isCancelledByUser = false;
    public bool IsCancelledByUser => _isCancelledByUser;

    private async void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        if (!IsProgressMode)
        {
            _isCancelled = true;
            _isCancelledByUser = true;
            ForceClose();
            return;
        }

        bool confirm = await ConfirmDialog.ShowAsync(this, "Are you sure you want to cancel the transfer?", "Cancel Transfer");
        if (confirm)
        {
            _isCancelled = true;
            _isCancelledByUser = true;
            CancelTransfer();
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

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        IntPtr hwnd = GetWindowHandle();
        if (IsSenderMode)
        {
            WindowsTaskbarProgress.SetProgressState(hwnd, TaskbarProgressState.Indeterminate);
        }
        else if (IsProgressMode)
        {
            WindowsTaskbarProgress.SetProgressState(hwnd, TaskbarProgressState.Normal);
        }
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

            System.Diagnostics.Debug.WriteLine($"Drive check failed: {ex.Message}");
        }

        IsProgressMode = true;
        this.Title = WindowTitle;

        if (!_hasTriggeredTransferStarted)
        {
            _hasTriggeredTransferStarted = true;
            TransferStarted?.Invoke();
        }

        IntPtr hwnd = GetWindowHandle();
        WindowsTaskbarProgress.SetProgressState(hwnd, TaskbarProgressState.Indeterminate);

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

            if (!_hasTriggeredTransferStarted)
            {
                _hasTriggeredTransferStarted = true;
                TransferStarted?.Invoke();
            }

            TransferFileName = e.CurrentFile;
            TransferItemCountText = $"({e.CurrentElementIndex}/{e.TotalElements})";
            TransferTotalBytes = e.TotalBytes;
            TransferSentBytes = e.BytesSent;

            TransferProgressText = $"{EtherTransfer.Core.FormatHelper.FormatSize(e.BytesSent)} / {EtherTransfer.Core.FormatHelper.FormatSize(e.TotalBytes)}";
            TransferSpeedText = $"{e.SpeedMbPerSec:F1} MB/s";

            this.Title = WindowTitle;

            IntPtr hwnd = GetWindowHandle();
            WindowsTaskbarProgress.SetProgressState(hwnd, TaskbarProgressState.Normal);
            if (e.TotalBytes > 0)
            {
                WindowsTaskbarProgress.SetProgressValue(hwnd, (ulong)Math.Min(e.BytesSent, e.TotalBytes), (ulong)e.TotalBytes);
            }
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

        IntPtr hwnd = GetWindowHandle();
        WindowsTaskbarProgress.SetProgressState(hwnd, TaskbarProgressState.NoProgress);
    }

    private bool _isForceClosing = false;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_isForceClosing || IsSuccessMode || IsFailureMode)
        {
            base.OnClosing(e);
            return;
        }

        if (IsProgressMode)
        {
            e.Cancel = true;

            bool confirm = await ConfirmDialog.ShowAsync(
                this,
                "Are you sure you want to cancel the transfer?",
                "Cancel Transfer");

            if (confirm)
            {
                _isCancelled = true;
                _isCancelledByUser = true;
                CancelTransfer();
            }
            return;
        }

        _isCancelled = true;
        _isCancelledByUser = true;
        _isForceClosing = true;
        CancelTransfer();
        _receiverTcs?.TrySetResult((false, "", default));
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _isCancelled = true;
        _receiverCancelCts?.Cancel();

        IntPtr hwnd = GetWindowHandle();
        WindowsTaskbarProgress.SetProgressState(hwnd, TaskbarProgressState.NoProgress);

        _receiverTcs?.TrySetResult((false, "", default));
        base.OnClosed(e);
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

