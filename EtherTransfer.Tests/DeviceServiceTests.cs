using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using EtherTransfer.Core.Models;
using EtherTransfer.Services.DeviceManager;
using EtherTransfer.Network.UdpDiscovery;
using NUnit.Framework;

namespace EtherTransfer.Tests
{
    [TestFixture]
    public class DeviceServiceTests
    {
        private DeviceService _deviceService;

        [SetUp]
        public void Setup()
        {
            _deviceService = new DeviceService();
        }

        [TearDown]
        public void Teardown()
        {
            _deviceService.Stop();
            _deviceService.Dispose();
        }

        [Test]
        public void DeviceService_InitializesCorrectly()
        {
            Assert.That(_deviceService, Is.Not.Null);
            Assert.That(_deviceService.GetActiveDevices().Count(), Is.EqualTo(0));
        }

    }
}
