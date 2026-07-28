using Avalonia.Controls;
using System.Collections.ObjectModel;

namespace EtherTransfer.UI;

public partial class ScanDialog : Window
{
    public ObservableCollection<ScanProgressViewModel> ScanTasks { get; } = new();

    public ScanDialog()
    {
        InitializeComponent();
        DataContext = this;
    }

    public ScanDialog(ObservableCollection<ScanProgressViewModel> tasks) : this()
    {
        ScanTasks = tasks;
    }
}
