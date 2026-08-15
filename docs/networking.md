# EtherTransfer Networking Architecture

## Overview
EtherTransfer relies on UDP broadcasts for peer discovery and high-performance TCP streaming for file transfers. The network layer has been heavily hardened and re-architected to guarantee absolute reliability over direct, unmanaged physical Ethernet links.

---

## 1. UDP Peer Discovery (`DiscoveryService.cs`)

EtherTransfer uses UDP on Port **50000** for decentralized peer discovery.

### Session-Based Identity
A device's identity is tied to a dynamically generated `SessionId` (Guid) created upon app startup, rather than a static IP address.
- **Why?** Laptops commonly switch IP addresses due to DHCP or link-local renegotiations when plugging/unplugging cables. Tying identity to an IP address creates stale "ghost devices."
- **Mechanism**: The `HELLO` broadcast payload contains the `SessionId`. The UI and `DeviceService` key their peers by this ID. If an IP address changes, the application updates the record in-place instead of creating a duplicate.

### Subnet Security Filtering
The `DiscoveryService` implements an aggressive enterprise-level filter on incoming `HELLO` packets.
- When a packet is received, the service cross-references the source IP against `NetworkHelper.IsIpInActiveSubnets(sourceIpStr)`.
- If the packet originated from a subnet that is *not* bound to a physical Ethernet adapter (e.g., it leaked over a Wi-Fi connection), the packet is silently dropped.

---

## 2. Physical Link Monitoring (`EthernetLinkMonitor.cs`)

Because direct PC-to-PC connections do not use a router, the OS often struggles to allocate IP addresses (falling back to APIPA/Link-Local). The `EthernetLinkMonitor` replaces legacy static configuration scripts with a robust, real-time state machine.

### The State Machine
- **`NoCable`**: No physical Ethernet link is detected.
- **`Configuring`**: A physical link is detected (OperationalStatus is UP), but the OS has not yet assigned an IPv4 address.
- **`Ready`**: The interface is UP and has a valid IPv4 address. Peer discovery and transfers are permitted.
- **`ConfigError`**: The OS failed to assign an IP address within the timeout period.

### Linux Auto-Configuration (`nmcli`)
On Windows and macOS, link-local (169.254.x.x) fallback is native and reliable. On Linux, it often hangs indefinitely.
- When `EthernetLinkMonitor` enters the `Configuring` state on Linux, it invokes a background `nmcli` command (`nmcli device modify {iface} ipv4.method link-local`) to force the interface into link-local mode.
- **Teardown**: When the cable is unplugged (transition to `NoCable`) or the application shuts down, `EthernetLinkMonitor` runs `nmcli device reapply {iface}` to cleanly restore the user's original network profile, leaving zero footprint.

---

## 3. TCP Transfer Protocol Hardening

Because EtherTransfer operates directly on physical wire without a switch, the OS TCP stack doesn't always cleanly abort a connection immediately when a cable is pulled. To prevent application hangs, the TCP streaming protocol employs aggressive failure detection:

- **Watchdog Timeouts**: Every network `ReadAsync` and `WriteAsync` call is wrapped in a `CancellationTokenSource.CancelAfter(timeoutMs)` watchdog. 
  - Metadata reads/writes (e.g., file headers, skip markers) are given a strict **2-second timeout**.
  - File chunk payload reads/writes (1MB chunks) are given a **5-second timeout**. If a 1MB chunk cannot traverse a direct Ethernet link in 5 seconds, the connection is considered physically severed.
- **TCP Keep-Alives**: Since watchdogs only run during active data transmission, the system explicitly enables native OS TCP Keep-Alives (`SocketOptionName.KeepAlive`). This provides a seamless safety net for idle states (such as when waiting for a user UI prompt), ensuring physical link drops are caught even when no data is actively flowing.

---

## 4. Structured Diagnostics

The network layer utilizes `StructuredLogMessage`, allowing all network events to carry structured identifiers (`EventId`) and `LogLevel`.
- This separates diagnostic string parsing from UI rendering logic.
- The UI maps log levels and event IDs to distinct color codes in the debug console, allowing developers to immediately spot DHCP failures or timeout watchdogs.
