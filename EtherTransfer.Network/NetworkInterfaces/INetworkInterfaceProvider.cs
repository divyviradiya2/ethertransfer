using System.Collections.Generic;

namespace EtherTransfer.Network.NetworkInterfaces;

public interface INetworkInterfaceProvider
{
    IEnumerable<NetworkInterfaceInfo> GetEthernetInterfaces();
}
