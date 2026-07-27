namespace EtherTransfer.Core.Models;

public class DiscoveryMessage
{
    public string Type { get; set; } = "HELLO";
    public string ComputerName { get; set; } = string.Empty;
    public int TcpPort { get; set; }
}
