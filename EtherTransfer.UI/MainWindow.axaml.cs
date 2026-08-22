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
using Avalonia.Input.Platform;
using EtherTransfer.Core.Models;
using EtherTransfer.Services.DeviceManager;
using EtherTransfer.Services;
using EtherTransfer.Network.NetworkInterfaces;
using EtherTransfer.Network.UdpDiscovery;

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
        ? $"Total: {SelectedPayloads.Count} items ({EtherTransfer.Core.FormatHelper.FormatSize(SelectedPayloads.Sum(p => p.TotalSize))})"
        : "";

    public string TotalSelectionSizeText => HasSelectedFiles
        ? $"Total Size: {EtherTransfer.Core.FormatHelper.FormatSize(SelectedPayloads.Sum(p => p.TotalSize))}"
        : "";

    public bool CanSend => HasSelection && HasSelectedFiles;

    // Current Dialog Reference
    private TransferDialog? _activeDialog;

    // Incoming Request State

    private string _customDeviceName = "";
    public string CustomDeviceName { get => _customDeviceName; set { _customDeviceName = value; OnPropertyChanged(); } }

    private readonly EthernetLinkMonitor _linkMonitor;
    public EthernetLinkState LinkState => _linkMonitor.CurrentState;
    public string? LinkErrorMessage => _linkMonitor.LastErrorMessage;
    public bool HasActiveVpn => _deviceService != null && _deviceService.HasActiveVpn;

    public bool IsNoCable => LinkState == EthernetLinkState.NoCable;
    public bool IsConfiguring => LinkState == EthernetLinkState.Configuring;
    public bool IsConfigError => LinkState == EthernetLinkState.ConfigError;
    public bool IsReady => LinkState == EthernetLinkState.Ready;

    private bool _isStartupFatalError;
    public bool IsStartupFatalError { get => _isStartupFatalError; set { _isStartupFatalError = value; OnPropertyChanged(); } }
    
    private string _startupFatalErrorMessage = "";
    public string StartupFatalErrorMessage { get => _startupFatalErrorMessage; set { _startupFatalErrorMessage = value; OnPropertyChanged(); } }

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

        // If running with Administrator privileges (e.g. Windows Portable edition), ensure inbound firewall rule
        _ = Task.Run(() =>
        {
            FirewallHelper.EnsureFirewallRule((msg, level) =>
            {
                OnDebugLog(this, new StructuredLogMessage("firewall.init", msg, level));
            });
        });

        _linkMonitor = new EthernetLinkMonitor(new EtherTransfer.Network.NetworkInterfaces.DefaultNetworkInterfaceProvider());
        _linkMonitor.StateChanged += OnLinkStateChanged;
        _linkMonitor.Start();

        // Force UI to pick up the initial state
        OnPropertyChanged(nameof(LinkState));
        OnPropertyChanged(nameof(IsNoCable));
        OnPropertyChanged(nameof(IsConfiguring));
        OnPropertyChanged(nameof(IsConfigError));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(LinkErrorMessage));

        // Start Transfer Service
        TransferService tempService;
        try
        {
            tempService = new TransferService(CustomDeviceName, 55000);
            tempService.Start();
        }
        catch
        {
            tempService = new TransferService(CustomDeviceName, 0); // Fallback to dynamic port
            tempService.Start();
        }
        _transferService = tempService;
        _transferService.DebugLog += OnDebugLog;
        _transferService.ProgressUpdated += OnProgressUpdated;
        _transferService.TransferFinished += OnTransferFinished;
        _transferService.OnIncomingTransfer = HandleIncomingTransferAsync;

        int actualTcpPort = _transferService.TcpPort;

        // Start Discovery
        _deviceService = new DeviceService();
        _deviceService.DevicesChanged += OnDevicesChanged;
        _deviceService.TransferCancelReceived += OnTransferCancelReceived;
        _deviceService.NetworkChanged += (_, _) => _ = Dispatcher.UIThread.InvokeAsync(() => OnPropertyChanged(nameof(HasActiveVpn)));
        _deviceService.DebugLog += OnDebugLog;
        
        // Start Discovery asynchronously to catch bind errors
        _ = StartDeviceServiceAsync(actualTcpPort);
    }

    private async Task StartDeviceServiceAsync(int actualTcpPort)
    {
        try
        {
            await _deviceService.StartAsync(CustomDeviceName, actualTcpPort);
            _ = Dispatcher.UIThread.InvokeAsync(() => OnPropertyChanged(nameof(HasActiveVpn)));
        }
        catch (Exception ex)
        {
            _ = Dispatcher.UIThread.InvokeAsync(() =>
            {
                var msg = $"Fatal error: Unable to bind to UDP discovery port 50000. Is another application using it?\n\n{ex.Message}";
                OnDebugLog(this, new StructuredLogMessage("startup.fatal", msg, LogLevel.Error));
                
                IsStartupFatalError = true;
                StartupFatalErrorMessage = msg;
            });
        }
    }

    private void OnLinkStateChanged(object? sender, EthernetLinkState newState)
    {
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            OnPropertyChanged(nameof(LinkState));
            OnPropertyChanged(nameof(IsNoCable));
            OnPropertyChanged(nameof(IsConfiguring));
            OnPropertyChanged(nameof(IsConfigError));
            OnPropertyChanged(nameof(IsReady));
            OnPropertyChanged(nameof(LinkErrorMessage));
            OnPropertyChanged(nameof(HasActiveVpn));

            // If we transition away from Ready while a transfer is active, abort it instantly.
            if (newState != EthernetLinkState.Ready && _activeDialog != null && _activeDialog.IsVisible)
            {
                OnDebugLog(this, new StructuredLogMessage("network.lost", $"Link state changed to {newState}. Aborting active transfer.", LogLevel.Error));
                _activeDialog.CancelTransfer();

                var errorDialog = new ErrorDialog($"Connection lost ({newState}).");
                _ = errorDialog.ShowDialog(this);
            }
        });
    }

    public void RetryConfig_Click(object? sender, RoutedEventArgs e)
    {
        _linkMonitor.ManualRetry();
    }



    private void OnTransferFinished(object? sender, EtherTransfer.Core.Models.TransferResult result)
    {
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_activeDialog != null && _activeDialog.IsVisible)
            {
                if (result.Success)
                {
                    _activeDialog.IsSuccessMode = true;
                    _activeDialog.IsPartialSuccessMode = false;
                    _activeDialog.IsFailureMode = false;
                    _activeDialog.SetTransferElements(result.CompletedElementNames, null);
                }
                else if (result.TotalElements > 1 && result.CompletedElementsCount > 0)
                {
                    _activeDialog.IsSuccessMode = true;
                    _activeDialog.IsPartialSuccessMode = true;
                    _activeDialog.IsFailureMode = false;
                    _activeDialog.TransferFinalSizeText = $"Completed {result.CompletedElementsCount} of {result.TotalElements} items";
                    _activeDialog.SetTransferElements(result.CompletedElementNames, result.FailedElementNames);
                }
                else
                {
                    // 0 items completed (e.g. single item cancelled or aborted before any item finished)
                    OnDebugLog(this, new StructuredLogMessage("transfer.cancelled", $"Transfer cancelled/failed: {result.ErrorMessage}", LogLevel.Info));

                    bool isConnectionLoss = !string.IsNullOrEmpty(result.ErrorMessage) && 
                        (result.ErrorMessage.Contains("Connection", StringComparison.OrdinalIgnoreCase) || 
                         result.ErrorMessage.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                         result.ErrorMessage.Contains("network", StringComparison.OrdinalIgnoreCase));

                    _activeDialog.IsSuccessMode = false;
                    _activeDialog.IsFailureMode = true;
                    _activeDialog.FailureTitle = isConnectionLoss ? "Transfer Failed" : "Transfer Cancelled";
                    _activeDialog.FailureMessage = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "The transfer was cancelled." : result.ErrorMessage;
                    _activeDialog.FailureSubDetail = "No files were saved to your device. Any temporary data was safely cleaned up.";
                }
            }
        });
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            var activeDevices = _deviceService.GetActiveDevices();
            DiscoveredDevices.Clear();
            foreach (var device in activeDevices)
            {
                DiscoveredDevices.Add(device);
            }

            // Auto-select if there is exactly 1 device
            if (DiscoveredDevices.Count == 1 && SelectedDevice == null)
            {
                SelectedDevice = DiscoveredDevices[0];
            }
            // Preserve selection by stable identity (SessionId) across IP changes
            else if (SelectedDevice != null)
            {
                var liveDevice = DiscoveredDevices.FirstOrDefault(d => d.SessionId == SelectedDevice.SessionId);
                if (liveDevice != null && liveDevice != SelectedDevice)
                {
                    SelectedDevice = liveDevice;
                }
            }
            
            OnPropertyChanged(nameof(HasActiveVpn));
        });
    }

    private void OnDebugLog(object? sender, StructuredLogMessage logMsg)
    {
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            var cleanedMessage = logMsg.Message.Trim();

            string color = "#A6ADC8"; // Default text (Catppuccin Subtext0)

            if (logMsg.Level == LogLevel.Error)
            {
                color = "#F38BA8"; // Red
            }
            else if (logMsg.Level == LogLevel.Warning)
            {
                color = "#F9E2AF"; // Yellow
            }
            else if (logMsg.EventId.StartsWith("device.new") || logMsg.EventId.StartsWith("ethernet.ready"))
            {
                color = "#A6E3A1"; // Green
            }
            else if (logMsg.Level == LogLevel.Info)
            {
                if (cleanedMessage.Contains("settled", StringComparison.OrdinalIgnoreCase) ||
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
            }

            DebugMessages.Add(new LogMessage { Text = cleanedMessage, Color = color });

            // Keep log size manageable by removing oldest
            if (DebugMessages.Count > 100)
            {
                DebugMessages.RemoveAt(0);
            }
        });
    }

    private DebugWindow? _debugWindow;

    private void ShowDebugLog_Click(object? sender, RoutedEventArgs e)
    {
        if (_debugWindow == null)
        {
            _debugWindow = new DebugWindow();
            _debugWindow.DataContext = this;
            _debugWindow.Closed += (s, args) => _debugWindow = null;
            _debugWindow.Show(this);
        }
        else
        {
            _debugWindow.Activate();
        }
    }

    private void OnProgressUpdated(object? sender, EtherTransfer.Transfer.TransferProgressEventArgs e)
    {
        _activeDialog?.UpdateProgress(e);
    }

    private void UpdateSelectionUI()
    {
        OnPropertyChanged(nameof(HasSelectedFiles));
        OnPropertyChanged(nameof(SelectionSummaryText));
        OnPropertyChanged(nameof(TotalSelectionSizeText));
        OnPropertyChanged(nameof(CanSend));
    }

    private void ClearSelectionButton_Click(object? sender, RoutedEventArgs e)
    {
        SelectedPayloads.Clear();
        UpdateSelectionUI();
        OnDebugLog(this, new StructuredLogMessage("ui.selection.cleared", "Cleared selection.", LogLevel.Info));
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
                OnDebugLog(this, new StructuredLogMessage("ui.selection.removed", $"Removed {item.Name} from selection.", LogLevel.Info));
            }
        }
    }

    private async void ExecuteSendButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedDevice == null || SelectedPayloads.Count == 0) return;

        // Resolve target IP at send time against the live table using stable identity
        var liveDevice = _deviceService.GetActiveDevices().FirstOrDefault(d => d.SessionId == SelectedDevice.SessionId);
        if (liveDevice == null)
        {
            var msg = "Selected device is no longer reachable on the network.";
            OnDebugLog(this, new StructuredLogMessage("transfer.failed", msg, LogLevel.Error));
            var errorDialog = new ErrorDialog(msg);
            _ = errorDialog.ShowDialog(this);
            return;
        }

        var targetIp = liveDevice.Address;
        if (targetIp != SelectedDevice.Address)
        {
            OnDebugLog(this, new StructuredLogMessage("transfer.ip_resolved", $"Resolved fresh IP {targetIp} for {liveDevice.Name} before sending.", LogLevel.Info));
        }

        var targetPort = liveDevice.Port > 0 ? liveDevice.Port : 55000;

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
            session.AddFiles(payload.DeepScannedFiles);
        }

        if (session.Files.Count == 0) return;

        var cts = new CancellationTokenSource();
        cts.Token.Register(() =>
        {
            if (System.Net.IPAddress.TryParse(targetIp, out var targetAddr))
            {
                _ = _deviceService.SendTransferCancelAsync(targetAddr);
            }
        });

        var dialog = TransferDialog.CreateSender(SelectedDevice.Name, cts);
        _activeDialog = dialog;

        dialog.Closed += (_, _) =>
        {
            if (_activeDialog == dialog)
            {
                _activeDialog = null;
            }
        };

        // Don't await the dialog, just show it. It will close itself on cancel, or we will close it when transfer finishes.
        _ = dialog.ShowDialog(this);

        try
        {
            await _transferService.TransmitSessionAsync(targetIp, targetPort, session, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnTransferCancelReceived(object? sender, PeerDiscoveredEventArgs e)
    {
        _ = Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_activeDialog != null)
            {
                if (_activeDialog.IsReceiverMode)
                {
                    OnDebugLog(this, new StructuredLogMessage("transfer.cancelled_by_sender", $"Sender '{e.Message.ComputerName}' cancelled the transfer request.", LogLevel.Info));
                    _activeDialog.IsFailureMode = true;
                    _activeDialog.FailureTitle = "Request Cancelled";
                    _activeDialog.FailureMessage = $"Sender '{e.Message.ComputerName}' cancelled the transfer request.";
                    _activeDialog.FailureSubDetail = "No transfer was initiated.";
                }
                else if (_activeDialog.IsProgressMode)
                {
                    OnDebugLog(this, new StructuredLogMessage("transfer.cancelled_by_sender", $"Sender '{e.Message.ComputerName}' cancelled active transfer.", LogLevel.Info));
                    _activeDialog.CancelTransfer();
                }
            }
        });
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
                OnDebugLog(this, new StructuredLogMessage("ui.error", "Failed to resolve local paths for selected files.", LogLevel.Error));
                return;
            }

            OnDebugLog(this, new StructuredLogMessage("ui.scanning", $"Scanning {paths.Count} items in parallel...", LogLevel.Info));

            var cts = new CancellationTokenSource();
            var scanTasksList = new ObservableCollection<ScanProgressViewModel>();
            var scanDialog = new ScanDialog(scanTasksList, cts);
            _ = scanDialog.ShowDialog(this);

                var scanTasks = paths.Select(p =>
                {
                    var vm = new ScanProgressViewModel { FolderName = System.IO.Path.GetFileName(p) ?? p };
                    _ = Dispatcher.UIThread.InvokeAsync(() => scanTasksList.Add(vm));

                    var progress = new Progress<int>(count =>
                    {
                        _ = Dispatcher.UIThread.InvokeAsync(() => vm.FilesFound = count);
                    });

                    return _transferService.ScanItemAsync(p, progress, cts.Token).ContinueWith(t =>
                    {
                        _ = Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            vm.IsComplete = true;
                        });
                        return t.IsCanceled || t.IsFaulted ? null : t.Result;
                    });
                });

            var payloads = await Task.WhenAll(scanTasks);
            bool wasCancelled = cts.IsCancellationRequested;
            scanDialog.Close();

            if (wasCancelled)
            {
                SelectedPayloads.Clear();
                UpdateSelectionUI();
                return;
            }

            foreach (var payload in payloads)
            {
                if (payload != null)
                {
                    SelectedPayloads.Add(payload);
                }
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
                    OnDebugLog(this, new StructuredLogMessage("ui.error", "Failed to resolve local path for selected folders. Are these network or virtual drives?", LogLevel.Warning));
                    return;
                }

                OnDebugLog(this, new StructuredLogMessage("ui.scanning", $"Scanning {paths.Count} root folder(s) in parallel...", LogLevel.Info));

                var cts = new CancellationTokenSource();
                var scanTasksList = new ObservableCollection<ScanProgressViewModel>();
                var scanDialog = new ScanDialog(scanTasksList, cts);
                _ = scanDialog.ShowDialog(this);

                var scanTasks = paths.Select(p =>
                {
                    var vm = new ScanProgressViewModel { FolderName = System.IO.Path.GetFileName(p) ?? p };
                    _ = Dispatcher.UIThread.InvokeAsync(() => scanTasksList.Add(vm));

                    var progress = new Progress<int>(count =>
                    {
                        _ = Dispatcher.UIThread.InvokeAsync(() => vm.FilesFound = count);
                    });

                    return _transferService.ScanItemAsync(p, progress, cts.Token).ContinueWith(t =>
                    {
                        _ = Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            vm.IsComplete = true;
                        });
                        return t.IsCanceled || t.IsFaulted ? null : t.Result;
                    });
                });

                var payloads = await Task.WhenAll(scanTasks);
                bool wasCancelled = cts.IsCancellationRequested;
                scanDialog.Close();

                if (wasCancelled)
                {
                    SelectedPayloads.Clear();
                    UpdateSelectionUI();
                    return;
                }

                foreach (var payload in payloads)
                {
                    if (payload != null)
                    {
                        SelectedPayloads.Add(payload);
                    }
                }
                UpdateSelectionUI();
            }
        }
        catch (Exception ex)
        {
            OnDebugLog(this, new StructuredLogMessage("ui.picker_crash", $"Folder Picker Crash: {ex.Message}\n{ex.StackTrace}", LogLevel.Error));
        }
    }

    private Task<(bool accept, string savePath, CancellationToken cancelToken)> HandleIncomingTransferAsync(TransferRequestMessage request, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<(bool, string, CancellationToken)>();

        ct.Register(() =>
        {
            _ = Dispatcher.UIThread.InvokeAsync(() =>
            {
                var dialogToClose = _activeDialog;
                _activeDialog = null;
                dialogToClose?.ForceClose();
                tcs.TrySetResult((false, "", default));
            });
        });

        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            string sizeStr = EtherTransfer.Core.FormatHelper.FormatSize(request.TotalSize);
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

            var cancelCts = new CancellationTokenSource();
            var dialog = TransferDialog.CreateReceiver(text, request.TotalSize, tcs, cancelCts);
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
        OnDebugLog(this, new StructuredLogMessage("ui.name_saved", $"Saved and broadcasted new name: {CustomDeviceName}", LogLevel.Info));
    }

    private bool _isShuttingDown = false;

    public void Shutdown()
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;

        try
        {
            _deviceService.Dispose();
            _transferService.Dispose();
            _linkMonitor.Dispose();
        }
        catch { }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        Shutdown();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Shutdown();
        base.OnClosed(e);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        
        // Force the window to the absolute foreground in case the UAC prompt 
        // or netsh console stole focus during the startup sequence.
        this.Topmost = true;
        this.Topmost = false;
        this.Activate();
        this.Focus();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}