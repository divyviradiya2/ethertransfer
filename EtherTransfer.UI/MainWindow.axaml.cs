using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using EtherTransfer.Core.Models;
using EtherTransfer.Services.DeviceManager;
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
        
    public bool CanSend => HasSelection && HasSelectedFiles;

    // Current Dialog Reference
    private TransferDialog? _activeDialog;

    // Incoming Request State
    
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
        _activeDialog?.UpdateProgress(e);
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

        if (session.Files.Count == 0) return;
        
        var cts = new CancellationTokenSource();
        var dialog = TransferDialog.CreateSender(SelectedDevice.Name, cts);
        _activeDialog = dialog;
        
        // Don't await the dialog, just show it. It will close itself on cancel, or we will close it when transfer finishes.
        _ = dialog.ShowDialog(this);
        
        try
        {
            await _transferService.TransmitSessionAsync(targetIp, targetPort, session, cts.Token);
            
            // Auto-clear selection after successful transfer
            SelectedPayloads.Clear();
            UpdateSelectionUI();
            OnDebugLog(this, "Transfer finished successfully. Selection cleared.");
        }
        catch (OperationCanceledException)
        {
            OnDebugLog(this, "Transfer cancelled by user.");
            dialog.Close();
        }
        catch (Exception ex)
        {
            OnDebugLog(this, $"Transfer failed: {ex.Message}");
            dialog.Close();
        }
        finally
        {
            // Dialog manages its own closure on success via the Done button
            _activeDialog = null;
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

    private Task<(bool accept, string savePath)> HandleIncomingTransferAsync(TransferRequestMessage request, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<(bool, string)>();
        
        ct.Register(() => 
        {
            Dispatcher.UIThread.InvokeAsync(() => 
            {
                _activeDialog?.Close();
                tcs.TrySetResult((false, ""));
            });
        });
        
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            string sizeStr = $"{request.TotalSize / 1024 / 1024} MB";
            string text;
            
            if (request.PayloadFolderCount > 0 && request.PayloadFileCount > 0)
            {
                text = $"{request.SenderName} wants to send {request.PayloadFolderCount} folder(s) and {request.PayloadFileCount} file(s) ({sizeStr}).";
            }
            else if (request.PayloadFolderCount > 0)
            {
                text = $"{request.SenderName} wants to send {request.PayloadFolderCount} folder(s) containing {request.TotalFiles} file(s) ({sizeStr}).";
            }
            else
            {
                text = $"{request.SenderName} wants to send {request.PayloadFileCount} file(s) ({sizeStr}).";
            }

            var dialog = TransferDialog.CreateReceiver(text, tcs);
            _activeDialog = dialog;
            await dialog.ShowDialog(this);
            _activeDialog = null;
        });

        return tcs.Task;
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