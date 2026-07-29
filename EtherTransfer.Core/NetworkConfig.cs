using System;

namespace EtherTransfer.Core;

public class NetworkConfig
{
    public int DiscoveryPort { get; set; } = 50000;
    public int BroadcastIntervalMs { get; set; } = 2000;
    
    /// <summary>
    /// How long a peer can be silent before it's considered offline.
    /// Default 45 seconds to survive heavy UDP packet loss during transfers.
    /// </summary>
    public TimeSpan PeerStaleThreshold { get; set; } = TimeSpan.FromSeconds(45);
    
    /// <summary>
    /// Cooldown between link-local configuration attempts on Linux.
    /// </summary>
    public TimeSpan ConfigRetryCooldown { get; set; } = TimeSpan.FromSeconds(15);
    
    /// <summary>
    /// Time to wait for OS to assign IP before falling back.
    /// </summary>
    public TimeSpan IPWaitLoopDelay { get; set; } = TimeSpan.FromMilliseconds(500);
    public int IPWaitLoopMaxAttempts { get; set; } = 12; // 12 * 500ms = 6s
    
    /// <summary>
    /// Timeout for external process calls (nmcli, ip, etc.)
    /// </summary>
    public int ProcessTimeoutMs { get; set; } = 10000;
    
    public static NetworkConfig Default { get; } = new NetworkConfig();
}
