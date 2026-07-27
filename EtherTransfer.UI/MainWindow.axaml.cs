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
using EtherTransfer.Services;

namespace EtherTransfer.UI;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    public ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; } = new();
    public ObservableCollection<string> DebugMessages { get; } = new();
    
    private readonly DeviceService _deviceService;
    private readonly TransferService _transferService;

    // View Model Properties
    private DiscoveredDevice? _selectedDevice;
    public DiscoveredDevice? SelectedDevice
    {
        get => _selectedDevice;
        set { _selectedDevice = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelection)); }
    }
    public bool HasSelection => SelectedDevice != null;

    // Transfer Progress State
    private bool _isTransferring;
    public bool IsTransferring { get => _isTransferring; set { _isTransferring = value; OnPropertyChanged(); } }
    
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

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        string computerName = Environment.MachineName;

        // Start Discovery
        _deviceService = new DeviceService();
        _deviceService.DevicesChanged += OnDevicesChanged;
        _deviceService.DebugLog += OnDebugLog;
        _deviceService.Start(computerName, 55000);

        // Start Transfer Service
        _transferService = new TransferService(computerName, 55000);
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
            while (DebugMessages.Count > 100)
            {
                DebugMessages.RemoveAt(0);
            }
            if (DebugLogList != null && DebugMessages.Count > 0)
            {
                DebugLogList.ScrollIntoView(DebugMessages[DebugMessages.Count - 1]);
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

    private async void SendFilesButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedDevice == null) return;

        // Open File Picker
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select files to send",
            AllowMultiple = true
        });

        if (files.Count > 0)
        {
            var paths = files.Select(f => f.Path.LocalPath).ToList();
            var targetIp = SelectedDevice.Address;
            var targetPort = 55000;
            
            // Fire and forget send
            _ = _transferService.SendFilesAsync(targetIp, targetPort, paths);
        }
    }

    private Task<(bool accept, string savePath)> HandleIncomingTransferAsync(TransferRequestMessage request)
    {
        var tcs = new TaskCompletionSource<(bool, string)>();
        
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            IncomingRequestText = $"{request.SenderName} wants to send {request.TotalFiles} file(s) ({request.TotalSize / 1024 / 1024} MB).";
            _incomingRequestTcs = tcs;
            HasIncomingRequest = true;
        });

        return tcs.Task;
    }

    private async void AcceptTransfer_Click(object? sender, RoutedEventArgs e)
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
            _incomingRequestTcs.SetResult((true, folder[0].Path.LocalPath));
        }
        else if (_incomingRequestTcs != null)
        {
            _incomingRequestTcs.SetResult((false, ""));
        }
    }

    private void DeclineTransfer_Click(object? sender, RoutedEventArgs e)
    {
        HasIncomingRequest = false;
        _incomingRequestTcs?.SetResult((false, ""));
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