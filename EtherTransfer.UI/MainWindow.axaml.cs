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

public class LogMessage
{
    public string Text { get; set; } = string.Empty;
    public string Color { get; set; } = "#A6ADC8";
}

public partial class MainWindow : Window, INotifyPropertyChanged
{
    public ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; } = new();
    public ObservableCollection<LogMessage> DebugMessages { get; } = new();
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
        _deviceService.NetworkChanged += OnNetworkChanged;
        _deviceService.DebugLog += OnDebugLog;
        _deviceService.Start(CustomDeviceName, 55000);

        // Start Transfer Service
        _transferService = new TransferService(CustomDeviceName, 55000);
        _transferService.DebugLog += OnDebugLog;
        _transferService.ProgressUpdated += OnProgressUpdated;
        _transferService.TransferFinished += OnTransferFinished;
        _transferService.OnIncomingTransfer = HandleIncomingTransferAsync;
        _transferService.Start();
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        OnDebugLog(this, "Network interfaces changed (Ethernet cable plugged/unplugged). Scanning for active devices...");

        // Enterprise Robustness: Instantly kill active transfers if the physical link drops
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_activeDialog != null && _activeDialog.IsVisible)
            {
                var ethInterfaces = EtherTransfer.Network.NetworkInterfaces.NetworkHelper.GetEthernetInterfaces().ToList();
                if (ethInterfaces.Count == 0)
                {
                    OnDebugLog(this, "Physical link lost! Aborting active transfer instantly.");
                    _activeDialog.Close();

                    var errorDialog = new ErrorDialog("Connection lost (Ethernet cable disconnected).");
                    _ = errorDialog.ShowDialog(this);
                }
            }
        });
    }

    private void OnTransferFinished(object? sender, (bool success, string? error) e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_activeDialog != null && _activeDialog.IsVisible && !e.success)
            {
                OnDebugLog(this, $"Transfer stopped: {e.error}");
                _activeDialog.Close();

                var errorDialog = new ErrorDialog(e.error ?? "Transfer failed due to a network error.");
                _ = errorDialog.ShowDialog(this);
            }
        });
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
            var cleanedMessage = message.Trim();

            string color = "#A6ADC8"; // Default text (Catppuccin Subtext0)

            if (cleanedMessage.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                cleanedMessage.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                cleanedMessage.Contains("abort", StringComparison.OrdinalIgnoreCase) ||
                cleanedMessage.Contains("lost", StringComparison.OrdinalIgnoreCase) ||
                cleanedMessage.Contains("exception", StringComparison.OrdinalIgnoreCase))
            {
                color = "#F38BA8"; // Red
            }
            else if (cleanedMessage.Contains("warn", StringComparison.OrdinalIgnoreCase) ||
                     cleanedMessage.Contains("skip", StringComparison.OrdinalIgnoreCase) ||
                     cleanedMessage.Contains("no ethernet", StringComparison.OrdinalIgnoreCase))
            {
                color = "#F9E2AF"; // Yellow
            }
            else if (cleanedMessage.Contains("success", StringComparison.OrdinalIgnoreCase) ||
                     cleanedMessage.Contains("complete", StringComparison.OrdinalIgnoreCase) ||
                     cleanedMessage.Contains("accept", StringComparison.OrdinalIgnoreCase) ||
                     cleanedMessage.Contains("restored", StringComparison.OrdinalIgnoreCase) ||
                     cleanedMessage.Contains("configured", StringComparison.OrdinalIgnoreCase) ||
                     cleanedMessage.Contains("new device", StringComparison.OrdinalIgnoreCase))
            {
                color = "#A6E3A1"; // Green
            }
            else if (cleanedMessage.Contains("settled", StringComparison.OrdinalIgnoreCase) ||
                     cleanedMessage.Contains("scanning", StringComparison.OrdinalIgnoreCase) ||
                     cleanedMessage.Contains("listening", StringComparison.OrdinalIgnoreCase) ||
                     cleanedMessage.Contains("offline", StringComparison.OrdinalIgnoreCase))
            {
                color = "#89B4FA"; // Blue
            }
            else if (cleanedMessage.Contains("network interface", StringComparison.OrdinalIgnoreCase))
            {
                color = "#CBA6F7"; // Purple
            }

            DebugMessages.Add(new LogMessage { Text = cleanedMessage, Color = color });

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
            var msg = ex.Message;
            if (ex is System.IO.IOException || ex is System.Net.Sockets.SocketException)
                msg = "Connection lost (Ethernet cable disconnected or receiver aborted).";

            OnDebugLog(this, $"Transfer failed: {msg}");
            dialog.Close();

            var errorDialog = new ErrorDialog(msg);
            _ = errorDialog.ShowDialog(this);
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

            OnDebugLog(this, $"Scanning {paths.Count} items in parallel...");

            var scanTasksList = new ObservableCollection<ScanProgressViewModel>();
            var scanDialog = new ScanDialog(scanTasksList);
            _ = scanDialog.ShowDialog(this);

            var scanTasks = paths.Select(p =>
            {
                var vm = new ScanProgressViewModel { FolderName = System.IO.Path.GetFileName(p) ?? p };
                Dispatcher.UIThread.InvokeAsync(() => scanTasksList.Add(vm));

                var progress = new Progress<int>(count =>
                {
                    Dispatcher.UIThread.InvokeAsync(() => vm.FilesFound = count);
                });

                return _transferService.ScanItemAsync(p, progress).ContinueWith(t =>
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        vm.IsComplete = true;
                    });
                    return t.Result;
                });
            });

            var payloads = await Task.WhenAll(scanTasks);
            scanDialog.Close();

            foreach (var payload in payloads)
            {
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

                OnDebugLog(this, $"Scanning {paths.Count} root folder(s) in parallel...");

                var scanTasksList = new ObservableCollection<ScanProgressViewModel>();
                var scanDialog = new ScanDialog(scanTasksList);
                _ = scanDialog.ShowDialog(this);

                var scanTasks = paths.Select(p =>
                {
                    var vm = new ScanProgressViewModel { FolderName = System.IO.Path.GetFileName(p) ?? p };
                    Dispatcher.UIThread.InvokeAsync(() => scanTasksList.Add(vm));

                    var progress = new Progress<int>(count =>
                    {
                        Dispatcher.UIThread.InvokeAsync(() => vm.FilesFound = count);
                    });

                    return _transferService.ScanItemAsync(p, progress).ContinueWith(t =>
                    {
                        Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            vm.IsComplete = true;
                        });
                        return t.Result;
                    });
                });

                var payloads = await Task.WhenAll(scanTasks);
                scanDialog.Close();

                foreach (var payload in payloads)
                {
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

    private Task<(bool accept, string savePath, CancellationToken cancelToken)> HandleIncomingTransferAsync(TransferRequestMessage request, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<(bool, string, CancellationToken)>();

        ct.Register(() =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                _activeDialog?.Close();
                tcs.TrySetResult((false, "", default));
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

            var dialogCts = new CancellationTokenSource();
            var dialog = TransferDialog.CreateReceiver(text, tcs, dialogCts);
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