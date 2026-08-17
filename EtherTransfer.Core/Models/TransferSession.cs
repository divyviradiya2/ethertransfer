using System.Collections.Generic;
using System.Linq;

namespace EtherTransfer.Core.Models;

public class FileSelectionItem
{
    public string AbsolutePath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string RootName { get; set; } = string.Empty; // The top-level folder or file name the user selected
    public long Size { get; set; }
}

public class TransferSession
{
    private readonly object _lock = new();
    private readonly List<FileSelectionItem> _files = new();

    public IReadOnlyList<FileSelectionItem> Files
    {
        get
        {
            lock (_lock) return _files.ToList();
        }
    }

    public void AddFiles(IEnumerable<FileSelectionItem> files)
    {
        lock (_lock) _files.AddRange(files);
    }

    public bool ContainsFolders { get; set; }
    public int PayloadFolderCount { get; set; }
    public int PayloadFileCount { get; set; }

    public long TotalSize
    {
        get
        {
            lock (_lock) return _files.Sum(f => f.Size);
        }
    }

    public int TotalFiles
    {
        get
        {
            lock (_lock) return _files.Count;
        }
    }
}
