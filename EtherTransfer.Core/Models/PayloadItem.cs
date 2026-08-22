using System;
using System.Collections.Generic;
using System.Linq;

namespace EtherTransfer.Core.Models;

public enum PayloadItemType
{
    File,
    Folder
}

public class PayloadItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public PayloadItemType Type { get; set; }

    // Deep scanned items for actual transmission
    public List<FileSelectionItem> DeepScannedFiles { get; set; } = new();

    public long TotalSize => DeepScannedFiles.Sum(f => f.Size);
    public int FileCount => DeepScannedFiles.Count;

    // UI Helpers
    public bool IsFolder => Type == PayloadItemType.Folder;
    public bool IsFile => Type == PayloadItemType.File;
    public string DisplaySize => $"{TotalSize / 1024.0 / 1024.0:F1} MB";
    public string DisplayCount => Type == PayloadItemType.Folder ? $"({FileCount} files)" : "";
}
