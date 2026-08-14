using System.Collections.Generic;

namespace EtherTransfer.Core.Models;

public class TransferResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int TotalElements { get; set; }
    public int CompletedElementsCount => CompletedElementNames.Count;
    public List<string> CompletedElementNames { get; set; } = new();
}
