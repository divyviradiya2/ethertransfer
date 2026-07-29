using System.Linq;
using System.Threading.Tasks;
using EtherTransfer.Network.NetworkInterfaces;
using NUnit.Framework;

namespace EtherTransfer.Tests
{
    [TestFixture]
    public class EthernetConfiguratorTests
    {
        [Test]
        public async Task EnsureEthernetReadyAsync_RunsWithoutCrashing()
        {
            var log = await EthernetConfigurator.EnsureEthernetReadyAsync(isRebind: false);
            Assert.That(log, Is.Not.Null);
            // On Windows, it just returns an empty log.
            // On Linux, it might do things, but we are just testing it doesn't crash here.
        }

        [Test]
        public void RestoreOriginalConfig_RunsWithoutCrashing()
        {
            var log = EthernetConfigurator.RestoreOriginalConfig();
            Assert.That(log, Is.Not.Null);
        }
    }
}
