using System.Collections.Generic;
using System.Linq;

namespace EtherTransfer.Core.Models;

public class FileSelectionItem
{
    public string AbsolutePath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public long Size { get; set; }
}

public class TransferSession
{
    public List<FileSelectionItem> Files { get; set; } = new();
    public bool ContainsFolders { get; set; }
    public int PayloadFolderCount { get; set; }
    public int PayloadFileCount { get; set; }
    
    public long TotalSize => Files.Sum(f => f.Size);
    public int TotalFiles => Files.Count;
}
