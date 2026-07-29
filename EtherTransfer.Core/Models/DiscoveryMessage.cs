namespace EtherTransfer.Core.Models;

public class DiscoveryMessage
{
    public string Type { get; set; } = "HELLO";
    public string Id { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ComputerName { get; set; } = string.Empty;
    public int TcpPort { get; set; }
    public string OS { get; set; } = string.Empty;
}
