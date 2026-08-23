using System;

namespace EtherTransfer.Core;

public class NetworkConfig
{
    public int DiscoveryPort { get; set; } = 50000;
    public int BroadcastIntervalMs { get; set; } = 2000;

    public TimeSpan PeerStaleThreshold { get; set; } = TimeSpan.FromSeconds(45);

    public TimeSpan ConfigRetryCooldown { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan IPWaitLoopDelay { get; set; } = TimeSpan.FromMilliseconds(500);
    public int IPWaitLoopMaxAttempts { get; set; } = 12;

    public int ProcessTimeoutMs { get; set; } = 10000;

    public static NetworkConfig Default { get; } = new NetworkConfig();
}

