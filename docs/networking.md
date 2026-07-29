# EtherTransfer Networking Layer

## Overview
EtherTransfer relies on UDP broadcasts for peer discovery and TCP for high-speed file transfers. The network layer has been heavily hardened to ensure reliable peer discovery even in challenging enterprise network environments (e.g., bridging, dual-homed machines, flaky link-local fallback on Linux).

## Stable Identity (`SessionId`)
A device's identity is now tied to a dynamically generated `SessionId` (Guid) generated upon app startup, not its current IP address.
- **Why?** Laptops commonly switch between networks, or connect to multiple networks simultaneously (VPNs, docks). Tying identity to an IP address creates stale UI entries ("ghost devices") and prevents reliable file transfers.
- **How it works:** 
  1. `DiscoveryService` generates a unique `SessionId`.
  2. `DiscoveryMessage` broadcasts this `SessionId`.
  3. `DeviceService` keys its internal `_devices` dictionary using `SessionId`.
  4. If a device changes its IP address, `DeviceService` updates the `Address` in place, avoiding duplicates.
  5. The UI resolves the *live IP address* from `DeviceService` at the exact moment the user clicks "Send".

## Concurrency & Performance (`EthernetConfigurator`)
On Linux, EtherTransfer tries to configure Link-Local IPv4 addresses automatically using NetworkManager (`nmcli`) if standard DHCP fails.
- **Locking & Thread Safety:** The configuration process is protected by `_lock` to safely handle simultaneous physical network unplugs and application startup events.
- **Parallel Checks:** `Task.WhenAll` allows `EthernetConfigurator` to wait for all interfaces concurrently, significantly reducing application startup time on multi-NIC setups.
- **Honest Tracking:** Configuration outcomes are properly tracked using `ConfigStatus` (Pending, Success, Failed) to avoid pointless retries and misleading log messages. The manual `ip addr add` `sudo` fallback was removed due to unpredictable behavior and permission issues.

## UDP Discovery Port Binding
The UDP listener `UdpClient` now binds to `0.0.0.0:50000` *before* `EthernetConfigurator` runs.
- **Why?** Previously, slow NetworkManager link-local fallback negotiations would block the listener thread, meaning the app would completely miss `HELLO` packets broadcasted by peers who booted up faster. 
- **Self-Discovery:** `DiscoveryService` filters out incoming packets where `message.SessionId == _sessionId` to prevent the UI from displaying the host machine.

## Link-State Driven Eviction
- Instead of waiting for a 45-second stale timeout when a cable is unplugged, `DeviceService` hooks into OS `NetworkChange.NetworkAddressChanged`.
- It recalculates active IP subnets and instantly evicts peers that are no longer physically reachable, preventing stalled transfer attempts.

## Structured Logging
The network layer utilizes `StructuredLogMessage` (`EtherTransfer.Core.Models`), allowing events to carry structured identifiers (`EventId`) and `LogLevel`.
- This separates diagnostic string parsing from UI rendering logic.
- `MainWindow` maps log levels and event IDs to distinct Catppuccin color codes for the user-facing debug panel.
