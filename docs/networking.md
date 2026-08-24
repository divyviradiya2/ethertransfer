# EtherTransfer Networking Architecture

> **Interactive Documentation**: For interactive schemas, benchmarks, and deep links to source files, see **[Architecture & Networking Docs](https://divyviradiya2.github.io/ethertransfer/docs.html#architecture-networking)**.

## Overview
EtherTransfer relies on UDP broadcasts for peer discovery and high-performance TCP streaming for file transfers. The network layer has been heavily hardened and re-architected to guarantee absolute reliability over direct, unmanaged physical Ethernet links.

<div align="center">
  <img src="network_diagram.svg" alt="EtherTransfer Network Topology" width="860" />
</div>

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

## 3. TCP Transfer Protocol & Pipelined Engine Hardening

Because EtherTransfer operates directly on physical wire without a switch, the OS TCP stack doesn't always cleanly abort a connection immediately when a cable is pulled. To guarantee sustained line-rate throughput and prevent hangs:

- **32 MB Pipelined Channel Cushion**: Uses `System.Threading.Channels` double-buffering (16 &times; 2 MB chunks) to decouple NVMe/SATA disk reads from socket transmission, completely absorbing NTFS write flushes and real-time antivirus scan pauses.
- **Tuned Socket Buffers**: Configures 2 MB `SendBufferSize` and `ReceiveBufferSize` with `NoDelay = true` to prevent TCP Zero-Window stalls and sliding window collapses.
- **Raw Binary Folder Streaming**: Uses a zero-allocation length-prefixed binary stream `[4B PathLen][UTF-8 Path][8B FileSize][Payload]...[4B 0 EOF]` with receiver-side parallel disk ingestion (4–16 concurrent workers) to overcome Windows NTFS small-file latency.
- **Watchdog Timeouts**: Every network `ReadAsync` and `WriteAsync` call is protected with deterministic watchdogs (2s for metadata, 5s for payload chunks).
- **TCP Keep-Alives**: Explicitly enables native OS TCP Keep-Alives (`SocketOptionName.KeepAlive`) for idle prompt states.

---

## 4. Structured Diagnostics

The network layer utilizes `StructuredLogMessage`, allowing all network events to carry structured identifiers (`EventId`) and `LogLevel`.
- This separates diagnostic string parsing from UI rendering logic.
- The UI maps log levels and event IDs to distinct color codes in the debug console, allowing developers to immediately spot DHCP failures or timeout watchdogs.
