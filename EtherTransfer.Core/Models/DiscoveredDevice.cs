using System;

namespace EtherTransfer.Core.Models;

public class DiscoveredDevice
{
    public string SessionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; } = 55000;
    public string OS { get; set; } = string.Empty;
    public DateTime LastSeen { get; set; }
}
