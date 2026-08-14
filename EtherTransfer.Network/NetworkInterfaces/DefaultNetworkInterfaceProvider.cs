using System.Collections.Generic;
using System.Linq;

namespace EtherTransfer.Network.NetworkInterfaces;

public class DefaultNetworkInterfaceProvider : INetworkInterfaceProvider
{
    public IEnumerable<NetworkInterfaceInfo> GetEthernetInterfaces()
    {
        return CrossPlatformNetworkDetector.GetInterfaces()
            .Where(ni => ni.IsEthernet && ni.IsPhysical);
    }
}
