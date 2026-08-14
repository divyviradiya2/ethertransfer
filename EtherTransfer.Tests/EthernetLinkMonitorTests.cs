using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using EtherTransfer.Network.NetworkInterfaces;
using NUnit.Framework;

namespace EtherTransfer.Tests;

public class InMemoryNetworkProvider : INetworkInterfaceProvider
{
    public List<NetworkInterfaceInfo> Interfaces { get; set; } = new();

    public IEnumerable<NetworkInterfaceInfo> GetEthernetInterfaces()
    {
        return Interfaces.ToList();
    }
}

[TestFixture]
public class EthernetLinkMonitorTests
{
    private InMemoryNetworkProvider _provider;
    private EthernetLinkMonitor _monitor;
    private List<EthernetLinkState> _stateChanges;

    private NetworkInterfaceInfo CreateDummyInterface(string name, OperationalStatus status, bool hasIpv4)
    {
        return new NetworkInterfaceInfo(
            Id: name,
            Name: name,
            Description: name + " desc",
            InterfaceType: NetworkInterfaceType.Ethernet,
            OperationalStatus: status,
            IsPhysical: true,
            IsEthernet: true,
            IsWifi: false,
            IsVirtual: false,
            MacAddress: new byte[] { 0, 1, 2, 3, 4, 5 },
            Ipv4Addresses: hasIpv4 ? new List<IPAddress> { IPAddress.Parse("192.168.1.10") } : new List<IPAddress>()
        );
    }

    [SetUp]
    public void Setup()
    {
        _provider = new InMemoryNetworkProvider();
        // Short poll interval for tests, short config timeout
        _monitor = new EthernetLinkMonitor(_provider, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(500));
        _stateChanges = new List<EthernetLinkState>();
        _monitor.StateChanged += (s, e) => _stateChanges.Add(e);
    }

    [TearDown]
    public void Teardown()
    {
        _monitor?.Dispose();
    }

    [Test]
    public async Task Scenario1_UnplugWhileConfiguring_TransitionsToNoCableInstantly()
    {
        // 1. Arrange: Up but no IP -> Configuring
        _provider.Interfaces.Add(CreateDummyInterface("eth0", OperationalStatus.Up, false));
        _monitor.Start();
        
        Assert.That(_monitor.CurrentState, Is.EqualTo(EthernetLinkState.Configuring));

        // 2. Act: Unplug (Down)
        _provider.Interfaces[0] = _provider.Interfaces[0] with { OperationalStatus = OperationalStatus.Down };
        
        // Simulate NetworkChange (since tests can't fire real OS events easily, we rely on the fast poll loop or manual reflection)
        // We'll wait a bit for the poll loop to pick it up, or we can invoke it via reflection if needed, but poll loop should catch it in < 100ms.
        await Task.Delay(150);

        // 3. Assert
        Assert.That(_monitor.CurrentState, Is.EqualTo(EthernetLinkState.NoCable));
        Assert.That(_stateChanges.Last(), Is.EqualTo(EthernetLinkState.NoCable));
    }

    [Test]
    public async Task Scenario3_ReplugFromNoCable_AutomaticallyEntersConfiguring()
    {
        // Start NoCable
        _provider.Interfaces.Add(CreateDummyInterface("eth0", OperationalStatus.Down, false));
        _monitor.Start();
        Assert.That(_monitor.CurrentState, Is.EqualTo(EthernetLinkState.NoCable));

        // Replug
        _provider.Interfaces[0] = _provider.Interfaces[0] with { OperationalStatus = OperationalStatus.Up };
        await Task.Delay(150); // wait for poll loop

        Assert.That(_monitor.CurrentState, Is.EqualTo(EthernetLinkState.Configuring));
        Assert.That(_stateChanges.Last(), Is.EqualTo(EthernetLinkState.Configuring));
    }

    [Test]
    public async Task Scenario5_ForceConfigAttemptToFail_TransitionsToConfigErrorExactlyOnce()
    {
        _provider.Interfaces.Add(CreateDummyInterface("eth0", OperationalStatus.Up, false));
        // Using a 500ms timeout from Setup()
        _monitor.Start();
        Assert.That(_monitor.CurrentState, Is.EqualTo(EthernetLinkState.Configuring));

        // Wait past the 500ms timeout
        await Task.Delay(800);

        Assert.That(_monitor.CurrentState, Is.EqualTo(EthernetLinkState.ConfigError));
        
        var errorCount = _stateChanges.Count(s => s == EthernetLinkState.ConfigError);
        Assert.That(errorCount, Is.EqualTo(1), "Should transition to ConfigError exactly once and stay there.");
        
        // Wait more, ensure it doesn't spam
        await Task.Delay(300);
        errorCount = _stateChanges.Count(s => s == EthernetLinkState.ConfigError);
        Assert.That(errorCount, Is.EqualTo(1), "Should not spam retries on its own.");
    }

    [Test]
    public async Task Scenario6_RapidUnplugReplugInsideDebounceWindow_EndsInMatchingState()
    {
        _provider.Interfaces.Add(CreateDummyInterface("eth0", OperationalStatus.Up, false));
        _monitor.Start();
        Assert.That(_monitor.CurrentState, Is.EqualTo(EthernetLinkState.Configuring));

        // Rapid unplug
        _provider.Interfaces[0] = _provider.Interfaces[0] with { OperationalStatus = OperationalStatus.Down };
        
        // Rapid replug before the 250ms debounce finishes
        await Task.Delay(50);
        _provider.Interfaces[0] = _provider.Interfaces[0] with { OperationalStatus = OperationalStatus.Up, Ipv4Addresses = new List<IPAddress> { IPAddress.Parse("192.168.1.10") } }; // It got an IP!
        
        await Task.Delay(200); // Let poll loop catch up

        Assert.That(_monitor.CurrentState, Is.EqualTo(EthernetLinkState.Ready));
    }
}
