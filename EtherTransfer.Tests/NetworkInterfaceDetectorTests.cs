using System.Collections.Generic;
using System.Net.NetworkInformation;
using EtherTransfer.Network.NetworkInterfaces;
using Moq;
using NUnit.Framework;

namespace EtherTransfer.Tests;

[TestFixture]
public class NetworkInterfaceDetectorTests
{
    private Mock<IPlatformEnvironment> _mockEnv;

    [SetUp]
    public void Setup()
    {
        _mockEnv = new Mock<IPlatformEnvironment>();
    }

    // A helper method to create a fake NetworkInterface (requires reflection or a wrapper if we were going all the way,
    // but since we can't easily mock sealed classes in .NET without more tools, we'll test the Analyze method directly
    // by passing in a dummy NetworkInterface if possible. Or we can just use Moq's ability to mock it if it's mockable.
    // NetworkInterface is abstract, so we can mock it!)
    private Mock<NetworkInterface> CreateMockInterface(string name, NetworkInterfaceType type, string description = "")
    {
        var mockNi = new Mock<NetworkInterface>();
        mockNi.Setup(n => n.Name).Returns(name);
        mockNi.Setup(n => n.Id).Returns(name); // Using name as ID for simplicity
        mockNi.Setup(n => n.NetworkInterfaceType).Returns(type);
        mockNi.Setup(n => n.Description).Returns(description);
        return mockNi;
    }

    [Test]
    public void Linux_PhysicalEthernet_ReturnsPhysical()
    {
        var mockNi = CreateMockInterface("eth0", NetworkInterfaceType.Ethernet);
        _mockEnv.Setup(e => e.IsLinux).Returns(true);
        _mockEnv.Setup(e => e.DirectoryExists("/sys/class/net/eth0")).Returns(true);
        _mockEnv.Setup(e => e.GetSymlinkTarget("/sys/class/net/eth0")).Returns("../../devices/pci0000:00/0000:00:1c.0/0000:02:00.0/net/eth0");

        var (isPhysical, isVirtual, isWifi) = LinuxNetworkInterfaceDetector.Analyze(mockNi.Object, _mockEnv.Object);

        Assert.That(isPhysical, Is.True);
        Assert.That(isVirtual, Is.False);
        Assert.That(isWifi, Is.False);
    }

    [Test]
    public void Linux_VirtualTailscale_ReturnsVirtual()
    {
        var mockNi = CreateMockInterface("tailscale0", NetworkInterfaceType.Ethernet);
        _mockEnv.Setup(e => e.IsLinux).Returns(true);
        _mockEnv.Setup(e => e.DirectoryExists("/sys/class/net/tailscale0")).Returns(true);
        _mockEnv.Setup(e => e.GetSymlinkTarget("/sys/class/net/tailscale0")).Returns("../../devices/virtual/net/tailscale0");

        var (isPhysical, isVirtual, isWifi) = LinuxNetworkInterfaceDetector.Analyze(mockNi.Object, _mockEnv.Object);

        Assert.That(isPhysical, Is.False);
        Assert.That(isVirtual, Is.True);
        Assert.That(isWifi, Is.False);
    }

    [Test]
    public void Linux_Wifi_ReturnsPhysicalAndWifi()
    {
        var mockNi = CreateMockInterface("wlan0", NetworkInterfaceType.Ethernet);
        _mockEnv.Setup(e => e.IsLinux).Returns(true);
        _mockEnv.Setup(e => e.DirectoryExists("/sys/class/net/wlan0")).Returns(true);
        _mockEnv.Setup(e => e.GetSymlinkTarget("/sys/class/net/wlan0")).Returns("../../devices/pci0000:00/0000:00:1c.0/0000:02:00.0/net/wlan0");
        _mockEnv.Setup(e => e.DirectoryExists("/sys/class/net/wlan0/wireless")).Returns(true);

        var (isPhysical, isVirtual, isWifi) = LinuxNetworkInterfaceDetector.Analyze(mockNi.Object, _mockEnv.Object);

        Assert.That(isPhysical, Is.True);
        Assert.That(isVirtual, Is.False);
        Assert.That(isWifi, Is.True);
    }

    [Test]
    public void Windows_PhysicalEthernet_ReturnsPhysical()
    {
        var mockNi = CreateMockInterface("Ethernet", NetworkInterfaceType.Ethernet);
        _mockEnv.Setup(e => e.IsWindows).Returns(true);
        _mockEnv.Setup(e => e.GetRegistryValue($@"SYSTEM\CurrentControlSet\Control\Network\{{4D36E972-E325-11CE-BFC1-08002BE10318}}\Ethernet\Connection", "PnpInstanceID"))
                .Returns(@"PCI\VEN_10EC&DEV_8168&SUBSYS_86771043&REV_15\4&2f9a9c68&0&00E4");

        var (isPhysical, isVirtual, isWifi) = WindowsNetworkInterfaceDetector.Analyze(mockNi.Object, _mockEnv.Object);

        Assert.That(isPhysical, Is.True);
        Assert.That(isVirtual, Is.False);
    }

    [Test]
    public void Windows_VirtualVpn_ReturnsVirtual()
    {
        var mockNi = CreateMockInterface("WireGuard", NetworkInterfaceType.Ethernet, "WireGuard Tunnel");
        _mockEnv.Setup(e => e.IsWindows).Returns(true);
        _mockEnv.Setup(e => e.GetRegistryValue($@"SYSTEM\CurrentControlSet\Control\Network\{{4D36E972-E325-11CE-BFC1-08002BE10318}}\WireGuard\Connection", "PnpInstanceID"))
                .Returns(@"ROOT\NET\0000");

        var (isPhysical, isVirtual, isWifi) = WindowsNetworkInterfaceDetector.Analyze(mockNi.Object, _mockEnv.Object);

        Assert.That(isPhysical, Is.False);
        Assert.That(isVirtual, Is.True);
    }

    [Test]
    public void Windows_FallbackHeuristics_ReturnsVirtualForTailscale()
    {
        var mockNi = CreateMockInterface("Tailscale", NetworkInterfaceType.Ethernet, "Tailscale Tunnel");
        _mockEnv.Setup(e => e.IsWindows).Returns(true);
        // Simulate registry read failure
        _mockEnv.Setup(e => e.GetRegistryValue(It.IsAny<string>(), It.IsAny<string>())).Returns((string?)null!);

        var (isPhysical, isVirtual, isWifi) = WindowsNetworkInterfaceDetector.Analyze(mockNi.Object, _mockEnv.Object);

        Assert.That(isPhysical, Is.False);
        Assert.That(isVirtual, Is.True);
    }

    [Test]
    public void Windows_BluetoothAdapter_ReturnsVirtual()
    {
        var mockNi = CreateMockInterface("Bluetooth Network Connection", NetworkInterfaceType.Ethernet, "Bluetooth Device (Personal Area Network)");
        _mockEnv.Setup(e => e.IsWindows).Returns(true);
        _mockEnv.Setup(e => e.GetRegistryValue($@"SYSTEM\CurrentControlSet\Control\Network\{{4D36E972-E325-11CE-BFC1-08002BE10318}}\Bluetooth Network Connection\Connection", "PnpInstanceID"))
                .Returns(@"BTH\MS_BTHPAN\6&31A60DE8&0&2");

        var (isPhysical, isVirtual, isWifi) = WindowsNetworkInterfaceDetector.Analyze(mockNi.Object, _mockEnv.Object);

        Assert.That(isPhysical, Is.False);
        Assert.That(isVirtual, Is.True);
    }

    [Test]
    public void Windows_BluetoothAdapter_Fallback_ReturnsVirtual()
    {
        var mockNi = CreateMockInterface("Bluetooth Network Connection", NetworkInterfaceType.Ethernet, "Bluetooth Device (Personal Area Network)");
        _mockEnv.Setup(e => e.IsWindows).Returns(true);
        _mockEnv.Setup(e => e.GetRegistryValue(It.IsAny<string>(), It.IsAny<string>())).Returns((string?)null!);

        var (isPhysical, isVirtual, isWifi) = WindowsNetworkInterfaceDetector.Analyze(mockNi.Object, _mockEnv.Object);

        Assert.That(isPhysical, Is.False);
        Assert.That(isVirtual, Is.True);
    }

    [Test]
    public void Linux_BluetoothAdapter_ReturnsVirtual()
    {
        var mockNi = CreateMockInterface("bnep0", NetworkInterfaceType.Ethernet, "Bluetooth PAN");
        _mockEnv.Setup(e => e.IsLinux).Returns(true);
        _mockEnv.Setup(e => e.DirectoryExists("/sys/class/net/bnep0")).Returns(true);
        _mockEnv.Setup(e => e.GetSymlinkTarget("/sys/class/net/bnep0")).Returns("../../devices/virtual/net/bnep0");

        var (isPhysical, isVirtual, isWifi) = LinuxNetworkInterfaceDetector.Analyze(mockNi.Object, _mockEnv.Object);

        Assert.That(isPhysical, Is.False);
        Assert.That(isVirtual, Is.True);
    }
}
