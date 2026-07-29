using System.Collections.Generic;
using System.Net.NetworkInformation;

namespace EtherTransfer.Network.NetworkInterfaces;

public record EthernetInterfaceState(string Id, string Name, string Description, OperationalStatus OperationalStatus, bool HasIpv4Address);

public interface INetworkInterfaceProvider
{
    IEnumerable<EthernetInterfaceState> GetEthernetInterfaces();
}
