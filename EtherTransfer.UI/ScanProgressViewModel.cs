using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EtherTransfer.UI;

public class ScanProgressViewModel : INotifyPropertyChanged
{
    private string _folderName = string.Empty;
    public string FolderName
    {
        get => _folderName;
        set { _folderName = value; OnPropertyChanged(); }
    }

    private int _filesFound;
    public int FilesFound
    {
        get => _filesFound;
        set { _filesFound = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
    }

    private bool _isComplete;
    public bool IsComplete
    {
        get => _isComplete;
        set { _isComplete = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
    }

    public string StatusText => IsComplete ? $"Complete ({FilesFound} files)" : $"Scanning... ({FilesFound} files found)";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
