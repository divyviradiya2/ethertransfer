using Avalonia.Controls;
using System.Collections.ObjectModel;

namespace EtherTransfer.UI;

public partial class ScanDialog : Window
{
    public ObservableCollection<ScanProgressViewModel> ScanTasks { get; }
    
    public System.Threading.CancellationTokenSource ScanCts { get; }

    public ScanDialog()
    {
        InitializeComponent();
        ScanTasks = new ObservableCollection<ScanProgressViewModel>();
        ScanCts = new System.Threading.CancellationTokenSource();
        DataContext = this;
    }

    public ScanDialog(ObservableCollection<ScanProgressViewModel> tasks, System.Threading.CancellationTokenSource cts) : this()
    {
        ScanTasks = tasks;
        ScanCts = cts;
        DataContext = this;
    }
    
    private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ScanCts.Cancel();
        Close();
    }
}
