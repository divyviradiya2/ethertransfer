using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using EtherTransfer.Core.Models;
using EtherTransfer.Services.DeviceManager;
using EtherTransfer.Core.Models;
using EtherTransfer.Services;

namespace EtherTransfer.UI;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    public ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; } = new();
    public ObservableCollection<string> DebugMessages { get; } = new();
    public ObservableCollection<PayloadItem> SelectedPayloads { get; } = new();
    
    private readonly DeviceService _deviceService;
    private readonly TransferService _transferService;

    // View Model Properties
    private DiscoveredDevice? _selectedDevice;
    public DiscoveredDevice? SelectedDevice
    {
        get => _selectedDevice;
        set { _selectedDevice = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(CanSend)); }
    }
    public bool HasSelection => SelectedDevice != null;

    public bool HasSelectedFiles => SelectedPayloads.Count > 0;
    
    public string SelectionSummaryText => HasSelectedFiles 
        ? $"Total: {SelectedPayloads.Count} items ({SelectedPayloads.Sum(p => p.TotalSize) / 1024 / 1024} MB)" 
        : "";
        
    public bool CanSend => HasSelection && HasSelectedFiles && !IsTransferring;

    // Transfer Progress State
    private bool _isTransferring;
    public bool IsTransferring 
    { 
        get => _isTransferring; 
        set { _isTransferring = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanSend)); } 
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

    // Incoming Request State
    private bool _hasIncomingRequest;
    public bool HasIncomingRequest { get => _hasIncomingRequest; set { _hasIncomingRequest = value; OnPropertyChanged(); } }
    
    private string _incomingRequestText = "";
    public string IncomingRequestText { get => _incomingRequestText; set { _incomingRequestText = value; OnPropertyChanged(); } }
    
    private TaskCompletionSource<(bool accept, string savePath)>? _incomingRequestTcs;
    
    private string _customDeviceName = "";
    public string CustomDeviceName { get => _customDeviceName; set { _customDeviceName = value; OnPropertyChanged(); } }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        var settings = EtherTransfer.Core.SettingsManager.Load();
        if (string.IsNullOrWhiteSpace(settings.CustomDeviceName))
        {
            settings.CustomDeviceName = Environment.MachineName;
            EtherTransfer.Core.SettingsManager.Save(settings);
        }
        
        CustomDeviceName = settings.CustomDeviceName;

        // Start Discovery
        _deviceService = new DeviceService();
        _deviceService.DevicesChanged += OnDevicesChanged;
        _deviceService.DebugLog += OnDebugLog;
        _deviceService.Start(CustomDeviceName, 55000);

        // Start Transfer Service
        _transferService = new TransferService(CustomDeviceName, 55000);
        _transferService.DebugLog += OnDebugLog;
        _transferService.ProgressUpdated += OnProgressUpdated;
        _transferService.OnIncomingTransfer = HandleIncomingTransferAsync;
        _transferService.Start();
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var activeDevices = _deviceService.GetActiveDevices();
            DiscoveredDevices.Clear();
            foreach (var device in activeDevices)
            {
                DiscoveredDevices.Add(device);
            }
        });
    }
    
    private void OnDebugLog(object? sender, string message)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            DebugMessages.Add(message);
            
            // Keep log size manageable by removing oldest
            if (DebugMessages.Count > 100)
            {
                DebugMessages.RemoveAt(0);
            }
            
            // Auto-scroll to bottom AFTER modifying the collection
            var count = DebugMessages.Count;
            if (count > 0 && DebugLogList != null)
            {
                DebugLogList.ScrollIntoView(DebugMessages[count - 1]);
            }
        });
    }

    private void OnProgressUpdated(object? sender, EtherTransfer.Transfer.TransferProgressEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsTransferring = true;
            TransferFileName = e.CurrentFile;
            TransferTotalBytes = e.TotalBytes;
            TransferSentBytes = e.BytesSent;
            
            double mbSent = e.BytesSent / 1024.0 / 1024.0;
            double mbTotal = e.TotalBytes / 1024.0 / 1024.0;
            TransferProgressText = $"{mbSent:F1} MB / {mbTotal:F1} MB";
            TransferSpeedText = $"{e.SpeedMbPerSec:F1} MB/s";

            if (e.BytesSent >= e.TotalBytes)
            {
                // Simple auto-hide after completion
                Task.Delay(2000).ContinueWith(_ => Dispatcher.UIThread.InvokeAsync(() => IsTransferring = false));
            }
        });
    }

    private void UpdateSelectionUI()
    {
        OnPropertyChanged(nameof(HasSelectedFiles));
        OnPropertyChanged(nameof(SelectionSummaryText));
        OnPropertyChanged(nameof(CanSend));
    }

    private void ClearSelectionButton_Click(object? sender, RoutedEventArgs e)
    {
        SelectedPayloads.Clear();
        UpdateSelectionUI();
        OnDebugLog(this, "Cleared selection.");
    }

    private void RemovePayload_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string id)
        {
            var item = SelectedPayloads.FirstOrDefault(p => p.Id == id);
            if (item != null)
            {
                SelectedPayloads.Remove(item);
                UpdateSelectionUI();
                OnDebugLog(this, $"Removed {item.Name} from selection.");
            }
        }
    }

    private async void ExecuteSendButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedDevice == null || SelectedPayloads.Count == 0) return;
        
        var targetIp = SelectedDevice.Address;
        var targetPort = 55000;
        
        // Dynamically compile the TransferSession from all queued payloads
        var session = new TransferSession();
        foreach (var payload in SelectedPayloads)
        {
            if (payload.Type == PayloadItemType.Folder) 
            {
                session.ContainsFolders = true;
                session.PayloadFolderCount++;
            }
            else 
            {
                session.PayloadFileCount++;
            }
            session.Files.AddRange(payload.DeepScannedFiles);
        }

        if (session.Files.Count == 0)
        {
            OnDebugLog(this, "No valid files to send.");
            return;
        }
        
        OnDebugLog(this, $"Starting transmission of {session.TotalFiles} files to {SelectedDevice.Name}...");
        
        try
        {
            await _transferService.TransmitSessionAsync(targetIp, targetPort, session);
            
            // Auto-clear selection after successful transfer
            SelectedPayloads.Clear();
            UpdateSelectionUI();
            OnDebugLog(this, "Transfer finished successfully. Selection cleared.");
        }
        catch (Exception ex)
        {
            OnDebugLog(this, $"Transfer failed: {ex.Message}");
        }
    }

    private async void SendFilesButton_Click(object? sender, RoutedEventArgs e)
    {
        // Open File Picker
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select files to add",
            AllowMultiple = true
        });

        if (files.Count > 0)
        {
            var paths = new List<string>();
            foreach (var f in files)
            {
                if (f.TryGetLocalPath() is string localPath)
                    paths.Add(localPath);
            }
            
            if (paths.Count == 0)
            {
                OnDebugLog(this, "Failed to resolve local paths for selected files.");
                return;
            }

            OnDebugLog(this, $"Scanning {paths.Count} items...");
            foreach (var path in paths)
            {
                var payload = await _transferService.ScanItemAsync(path);
                SelectedPayloads.Add(payload);
            }
            UpdateSelectionUI();
        }
    }

    private async void SendFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select folder(s) to add",
                AllowMultiple = true
            });

            if (folders.Count > 0)
            {
                var paths = new List<string>();
                foreach (var f in folders)
                {
                    var localPath = f.TryGetLocalPath() ?? f.Path.LocalPath;
                    if (!string.IsNullOrEmpty(localPath))
                    {
                        paths.Add(localPath);
                    }
                }

                if (paths.Count == 0)
                {
                    OnDebugLog(this, "Failed to resolve local path for selected folders. Are these network or virtual drives?");
                    return;
                }

                OnDebugLog(this, $"Scanning {paths.Count} root folder(s)...");
                foreach (var path in paths)
                {
                    var payload = await _transferService.ScanItemAsync(path);
                    SelectedPayloads.Add(payload);
                }
                UpdateSelectionUI();
            }
        }
        catch (Exception ex)
        {
            OnDebugLog(this, $"Folder Picker Crash: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private Task<(bool accept, string savePath)> HandleIncomingTransferAsync(TransferRequestMessage request)
    {
        var tcs = new TaskCompletionSource<(bool, string)>();
        
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            string sizeStr = $"{request.TotalSize / 1024 / 1024} MB";
            
            if (request.PayloadFolderCount > 0 && request.PayloadFileCount > 0)
            {
                IncomingRequestText = $"{request.SenderName} wants to send {request.PayloadFolderCount} folder(s) and {request.PayloadFileCount} file(s) ({sizeStr}).";
            }
            else if (request.PayloadFolderCount > 0)
            {
                IncomingRequestText = $"{request.SenderName} wants to send {request.PayloadFolderCount} folder(s) containing {request.TotalFiles} file(s) ({sizeStr}).";
            }
            else
            {
                IncomingRequestText = $"{request.SenderName} wants to send {request.PayloadFileCount} file(s) ({sizeStr}).";
            }

            _incomingRequestTcs = tcs;
            HasIncomingRequest = true;
        });

        return tcs.Task;
    }

    private async void AcceptTransfer_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            // Pick save folder
            var folder = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose save location"
            });

            HasIncomingRequest = false;

            if (folder.Count > 0 && _incomingRequestTcs != null)
            {
                _incomingRequestTcs.TrySetResult((true, folder[0].Path.LocalPath));
            }
            else if (_incomingRequestTcs != null)
            {
                _incomingRequestTcs.TrySetResult((false, ""));
            }
        }
        catch (Exception ex)
        {
            HasIncomingRequest = false;
            _incomingRequestTcs?.TrySetResult((false, ""));
            // Log to our UI debugger
            OnDebugLog(this, $"Error opening folder picker: {ex.Message}");
        }
    }

    private void DeclineTransfer_Click(object? sender, RoutedEventArgs e)
    {
        HasIncomingRequest = false;
        _incomingRequestTcs?.TrySetResult((false, ""));
    }

    private void SaveName_Click(object? sender, RoutedEventArgs e)
    {
        var settings = EtherTransfer.Core.SettingsManager.Load();
        
        if (string.IsNullOrWhiteSpace(CustomDeviceName))
        {
            CustomDeviceName = Environment.MachineName;
        }

        settings.CustomDeviceName = CustomDeviceName;
        EtherTransfer.Core.SettingsManager.Save(settings);
        
        _deviceService.UpdateComputerName(CustomDeviceName);
        OnDebugLog(this, $"Saved and broadcasted new name: {CustomDeviceName}");
    }

    protected override void OnClosed(EventArgs e)
    {
        _deviceService.Dispose();
        _transferService.Dispose();
        base.OnClosed(e);
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}