using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;

namespace EtherTransfer.Network.NetworkInterfaces;

public record NetworkInterfaceInfo(
    string Id,
    string Name,
    string Description,
    NetworkInterfaceType InterfaceType,
    OperationalStatus OperationalStatus,
    bool IsPhysical,
    bool IsEthernet,
    bool IsWifi,
    bool IsVirtual,
    byte[] MacAddress,
    IReadOnlyList<IPAddress> Ipv4Addresses);
