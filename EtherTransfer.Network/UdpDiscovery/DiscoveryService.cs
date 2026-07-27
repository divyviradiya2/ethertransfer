using System;
using System.Linq;
using System.Net;
using EtherTransfer.Core.Models;
using EtherTransfer.Network.NetworkInterfaces;
using Makaretu.Dns;

namespace EtherTransfer.Network.UdpDiscovery;

public class PeerDiscoveredEventArgs : EventArgs
{
    public DiscoveryMessage Message { get; }
    public IPAddress SourceAddress { get; }

    public PeerDiscoveredEventArgs(DiscoveryMessage message, IPAddress sourceAddress)
    {
        Message = message;
        SourceAddress = sourceAddress;
    }
}

public class DiscoveryService : IDisposable
{
    private MulticastService? _mdns;
    private ServiceDiscovery? _sd;
    private ServiceProfile? _profile;
    private CancellationTokenSource? _cts;
    
    public event EventHandler<PeerDiscoveredEventArgs>? PeerDiscovered;

    public void Start(string computerName, int tcpPort)
    {
        _cts = new CancellationTokenSource();
        _mdns = new MulticastService();
        _sd = new ServiceDiscovery(_mdns);

        // 1. Announce our presence via mDNS
        _profile = new ServiceProfile(computerName, "_ethtransfer._tcp", (ushort)tcpPort);
        _sd.Advertise(_profile);

        // 2. Listen for others
        _sd.ServiceInstanceDiscovered += OnServiceInstanceDiscovered;
        _mdns.AnswerReceived += OnAnswerReceived;

        _mdns.Start();
        
        // Periodically query to find existing/new peers (fixes late IP assignment on direct Ethernet)
        _ = Task.Run(async () =>
        {
            try
            {
                while (_cts != null && !_cts.IsCancellationRequested)
                {
                    _sd.QueryServiceInstances("_ethtransfer._tcp");
                    await Task.Delay(3000, _cts.Token);
                }
            }
            catch { }
        });
    }

    private void OnServiceInstanceDiscovered(object? sender, ServiceInstanceDiscoveryEventArgs e)
    {
        // When a service instance is discovered, we need its IP, so we query for its A record
        _mdns?.SendQuery(e.ServiceInstanceName, DnsClass.IN, DnsType.A);
    }

    private void OnAnswerReceived(object? sender, MessageEventArgs e)
    {
        var response = e.Message;
        
        // Find the SRV record to get the port and target name
        var srvRecord = response.Answers.OfType<SRVRecord>().FirstOrDefault(r => r.Name.ToString().Contains("_ethtransfer._tcp"));
        if (srvRecord == null)
        {
            srvRecord = response.AdditionalRecords.OfType<SRVRecord>().FirstOrDefault(r => r.Name.ToString().Contains("_ethtransfer._tcp"));
        }

        // Find the A record to get the IP address
        var aRecord = response.Answers.OfType<ARecord>().FirstOrDefault();
        if (aRecord == null)
        {
            aRecord = response.AdditionalRecords.OfType<ARecord>().FirstOrDefault();
        }

        if (srvRecord != null && aRecord != null)
        {
            var ip = aRecord.Address;
            
            // STRICT ETHERNET FILTERING
            // Only accept devices that are on the same subnet as one of our Ethernet adapters!
            if (!NetworkHelper.IsOnEthernetSubnet(ip))
            {
                return; // Ignore Wi-Fi or unroutable devices completely
            }

            // Extract the computer name from the SRV record name (e.g., "Divy-PC._ethtransfer._tcp.local")
            var nameParts = srvRecord.Name.ToString().Split('.');
            var computerName = nameParts[0];

            var message = new DiscoveryMessage
            {
                Type = "HELLO",
                ComputerName = computerName,
                TcpPort = srvRecord.Port
            };

            PeerDiscovered?.Invoke(this, new PeerDiscoveredEventArgs(message, ip));
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _sd?.Unadvertise(_profile);
        _mdns?.Stop();
    }

    public void Dispose()
    {
        Stop();
        _sd?.Dispose();
        _mdns?.Dispose();
    }
}
