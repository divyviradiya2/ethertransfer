using System;

namespace EtherTransfer.Core.Models;

public class DiscoveredDevice
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public DateTime LastSeen { get; set; }
}
