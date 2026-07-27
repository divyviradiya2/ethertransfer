using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using EtherTransfer.Core.Models;
using EtherTransfer.Services.DeviceManager;

namespace EtherTransfer.UI;

public partial class MainWindow : Window
{
    public ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; } = new();
    private readonly DeviceService _deviceService;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _deviceService = new DeviceService();
        _deviceService.DevicesChanged += OnDevicesChanged;

        // Use the machine name as the default computer name, and 55000 as TCP port
        _deviceService.Start(Environment.MachineName, 55000);
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

    private async void SendFilesButton_Click(object? sender, RoutedEventArgs e)
    {
        var msgBox = new Window()
        {
            Title = "File Picker Mock",
            Width = 300,
            Height = 150,
            Content = new TextBlock 
            { 
                Text = "Mock: Files selected.", 
                HorizontalAlignment = HorizontalAlignment.Center, 
                VerticalAlignment = VerticalAlignment.Center 
            }
        };
        await msgBox.ShowDialog(this);
    }

    private async void SendFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        var msgBox = new Window()
        {
            Title = "Folder Picker Mock",
            Width = 300,
            Height = 150,
            Content = new TextBlock 
            { 
                Text = "Mock: Folder selected.", 
                HorizontalAlignment = HorizontalAlignment.Center, 
                VerticalAlignment = VerticalAlignment.Center 
            }
        };
        await msgBox.ShowDialog(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        _deviceService.Dispose();
        base.OnClosed(e);
    }
}