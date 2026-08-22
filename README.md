<div align="center">

<table border="0" cellpadding="16">
  <tr>
    <td align="center" width="200">
      <img src="EtherTransfer.UI/Assets/logo.png" alt="EtherTransfer" width="180" />
    </td>
    <td align="center">
      <h1 style="border: none; margin-bottom: 10px;">EtherTransfer</h1>
      <p><b>Direct peer-to-peer file and folder transfer application over physical Ethernet links.</b></p>
      <p>Cross-platform, decentralized data movement without intermediate cloud servers, routers, or manual IP assignment.</p>
      <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 10" /></a>
      <img src="https://img.shields.io/badge/Windows-x64%20%2F%20x86-0078D4?style=flat-square&logo=windows" alt="Windows" />
      <img src="https://img.shields.io/badge/Linux-x64-E95420?style=flat-square&logo=linux" alt="Linux" />
      <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-22C55E?style=flat-square" alt="MIT License" /></a>
      <a href="https://divyviradiya2.github.io/ethertransfer/"><img src="https://img.shields.io/badge/Website-Live-2563EB?style=flat-square&logo=googlechrome&logoColor=white" alt="Live Website" /></a>
      <br><br>
      <a href="https://github.com/divyviradiya2/ethertransfer/releases/latest/download/EtherTransfer_Setup_x64.exe"><img src="https://img.shields.io/badge/Download-Windows%2064--bit-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="Download 64-bit" /></a>
      <a href="https://github.com/divyviradiya2/ethertransfer/releases/latest/download/EtherTransfer_Setup_x86.exe"><img src="https://img.shields.io/badge/Download-Windows%2032--bit-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="Download 32-bit" /></a>
    </td>
  </tr>
</table>

<br>

<img src="docs/network_diagram.jpg" alt="EtherTransfer Direct Point-to-Point Topology" width="880" style="border-radius: 12px; box-shadow: 0 4px 12px rgba(0,0,0,0.15);" />

<br><br>

</div>

---

## 1. Overview

**EtherTransfer** is an open-source, peer-to-peer desktop file transfer application built with **.NET 10** and **Avalonia UI**. It is designed specifically for direct computer-to-computer data movement over physical Ethernet connections (point-to-point cables, unmanaged switches, or local subnets) without requiring an existing router, DHCP server, Internet connectivity, or manual network address configuration.

EtherTransfer uses UDP broadcast on port `50000` for decentralized peer discovery and a length-prefixed TCP streaming protocol on port `55000` for file and directory tree transmission.

---

## 2. Why EtherTransfer Exists & What It Solves

> *"Making something complex is easy. Making something simple is much harder."*

In modern networking, moving a 100 GB folder between two computers sitting next to each other should take 10 seconds of human effort: plug a cable, select the folder, and hit send. 

Instead, traditional solutions demand extensive system administration:
- **SMB / Windows File Sharing**: Riddled with credential prompts, cross-platform Windows-to-Linux permission headaches, NetBIOS discovery failures, and public/private profile firewall blocks.
- **Manual Static IP / Netcat**: Requires opening terminal consoles, calculating non-conflicting IP subnets on both machines, creating tarballs manually, and piping raw sockets.
- **External USB Drives**: Requires a two-phase transfer (copying 100 GB onto the drive, waiting, unplugging, copying off the drive, waiting again) and fails if the file exceeds the drive's free capacity.
- **Cloud & Relay Solutions**: Route local data across external internet connections, capped by ISP upload limits and third-party servers.

**EtherTransfer exists to eliminate these rituals.** It provides a dedicated, direct peer-to-peer data pipeline over physical Ethernet links:
- **Automated Link-Local Discovery**: Discovers connected machines automatically via UDP broadcast on the local link without a router or manual IP configuration.
- **Interface Pinning**: Isolates physical Ethernet adapters from active Wi-Fi connections, ensuring large transfers saturate the physical cable while you continue browsing the web on Wi-Fi.
- **Deep Folder Structure Preservation**: Recursively scans, serializes, and recreates arbitrary directory trees on the destination drive without prior `.zip` or `.tar` archiving.
- **Cross-Platform Interoperability**: Moves data between Windows and Linux without platform-specific sharing protocols or OS user account mapping.

---

## 3. What EtherTransfer Does Not Solve

EtherTransfer is focused strictly on ad-hoc, direct file movement. It is **not**:
- **A Continuous Synchronization Engine**: Does not synchronize folders, track file deltas, or monitor file system change events (like Syncthing or Dropbox).
- **A Cloud Storage System or Remote Relay**: Does not provide internet-based file transfer, NAT traversal, or relay servers. Both devices must be on the same physical link or Layer 2 broadcast domain.
- **A Network Attached Storage (NAS) Service**: Does not maintain persistent shared drives or mountable network file systems.
- **An Encrypted/Zero-Trust Transport**: Transfers are currently unencrypted plaintext TCP on local private physical links. It is not intended for use across hostile or untrusted public networks.
- **Resumable Transport**: If a transfer is interrupted mid-stream, incomplete files are cleaned up; transfers cannot resume from an arbitrary byte offset.

---

## 4. Quick Start

```text
┌─────────────────────────┐         Ethernet Cable (Cat 5e/6/6a/7/8)        ┌─────────────────────────┐
│     Sender Computer     ├─────────────────────────────────────────────────┤    Receiver Computer    │
│  (EtherTransfer open)   │ ◄── UDP Discovery (Port 50000 Broadcast) ────► │  (EtherTransfer open)   │
│   IP: 169.254.x.x       │ ─── Framed TCP Stream (Port 55000) ──────────►  │   IP: 169.254.x.x       │
└─────────────────────────┘                                                 └─────────────────────────┘
```

1. **Launch**: Open EtherTransfer on both computers.
2. **Connect**: Connect the two computers directly using a standard Ethernet cable (or connect both to the same switch/LAN).
3. **Discover**: Within 1–5 seconds, each machine will detect the other via UDP broadcast and display the peer in the device list.
4. **Select & Send**: Drag and drop files or folders onto the target peer, or select them via the file picker and click **Send**.
5. **Accept**: The receiving machine will display a prompt showing the sender name, file count, and total payload size. Select the destination folder and click **Accept**.

---

## 5. Requirements

### Hardware Requirements
- **Network Interface**: One 10/100/1000 Mbps, 2.5 Gbps, 5 Gbps, or 10 Gbps Ethernet Network Interface Card (NIC) or USB-to-Ethernet adapter (USB 3.0 / USB-C recommended for Gigabit+).
- **Auto-MDIX (IEEE 802.3ab)**: Standard on virtually all Gigabit (1000BASE-T) and multi-gigabit NICs produced since 2000. Allows standard straight-through cables to work directly point-to-point without a crossover cable.
- **Cables**: Standard RJ-45 Category 5e, 6, 6a, 7, or 8 straight-through patch cable. (Legacy 100BASE-TX adapters without Auto-MDIX require a crossover cable).
- **Storage**: Storage read/write throughput on both machines determines effective transfer rates (e.g., mechanical HDDs will bottleneck multi-gigabit links).

### Software Requirements
- **Windows**: Windows 10 (1809+) or Windows 11 (x64 or x86). Supported out of the box with self-contained builds (no separate .NET runtime installation required).
- **Linux**: x64 Linux distribution running `glibc 2.27+` (Ubuntu 20.04+, Debian 11+, Fedora 36+, Arch Linux). NetworkManager (`nmcli`) is required for automated link-local configuration.
- **macOS**: Experimental / community testing (under development).

---

## 6. Supported Network Topologies

| Topology | Configuration Required | Description |
| :--- | :--- | :--- |
| **Direct Point-to-Point (1-to-1)** | None (Zero manual config) | A single Ethernet cable directly linking two computers. Operating systems negotiate IPv4 Link-Local addresses (`169.254.0.0/16`). |
| **Unmanaged Switch (1-to-Many)** | None (Zero manual config) | Multiple computers plugged into an unmanaged Ethernet switch without a router. All peers negotiate link-local addresses and discover each other. |
| **Existing LAN / Router (with DHCP)** | None | Computers connected to a home/office router. EtherTransfer uses the DHCP-assigned IPv4 addresses (`192.168.x.x`, `10.x.x.x`) across the subnet. |
| **Mixed Wi-Fi + Ethernet Cable** | None | Computers connected to Wi-Fi for Internet while simultaneously connected via direct Ethernet cable. EtherTransfer isolates and binds strictly to the physical Ethernet adapter. |

---

## 7. Direct Ethernet Operation

When connecting two computers directly with a cable without a DHCP server:

1. **Link-Local IP Negotiation (RFC 3927)**:
   - Operating systems automatically assign an IPv4 link-local address in the range `169.254.1.0` through `169.254.254.255` with subnet mask `255.255.0.0`.
   - **Windows**: Native Automatic Private IP Addressing (APIPA) assigns an IP within 2–6 seconds.
   - **Linux**: NetworkManager historically does not enable link-local negotiation automatically on unmanaged ports. EtherTransfer includes an active state machine (`EthernetLinkMonitor`) that automatically invokes `nmcli device modify <iface> ipv4.method link-local` when a physical cable is detected, and restores the original network configuration (`nmcli device reapply <iface>`) upon disconnection or application exit.
2. **Auto-MDIX**:
   - Modern Gigabit and multi-gigabit PHYs automatically configure internal transmit/receive circuitry to match pinouts. Straight-through cables function identically to crossover cables.

---

## 8. Automatic Discovery

Decentralized peer discovery is managed by `DiscoveryService.cs`:

- **Transport**: UDP Broadcast over port **`50000`** (using `255.255.255.255` and interface-specific directed broadcast addresses).
- **Identity Model**: Each application instance generates a unique UUID `SessionId` at startup. Peers are tracked by `SessionId`, ensuring that transient IP address changes (e.g., during link-local renegotiation) update the existing peer record in-place rather than creating duplicate ghost devices.
- **Broadcast Interval**:
  - Initial burst on startup: 250ms, 500ms, 1000ms.
  - Steady state: Broadcasts a `HELLO` packet every 2000ms.
  - Heartbeat / Stale Eviction: Peers silent for 45 seconds or whose IP address disappears from active Ethernet subnets are automatically evicted.
- **Teardown**: Upon normal shutdown, the client transmits a burst of 3 `BYE` packets to immediately unregister from peer lists.
- **Physical Interface Filtering**: Incoming UDP discovery packets are evaluated against `NetworkHelper.IsIpInActiveSubnets()`. Packets arriving from non-Ethernet or Wi-Fi subnets are dropped to prevent accidental transmission across slower wireless routes.
- **Subnet Boundary**: UDP broadcast packets do not cross Layer 3 routers. Discovery operates strictly within the local Layer 2 broadcast domain / subnet.

```json
// Discovery Broadcast Packet Schema (Port 50000)
{
  "Type": "HELLO",
  "Id": "EtherTransferApp-V1",
  "SessionId": "c4b8e21a-7b3f-4e89-9a12-8f6a91d2345e",
  "ComputerName": "Workstation-Desktop",
  "TcpPort": 55000,
  "OS": "Windows",
  "SequenceNumber": 42
}
```

---

## 9. Transfer Architecture

```text
Sender                                                                Receiver
  │                                                                      │
  │─── 1. TCP Handshake (Port 55000) ───────────────────────────────────►│
  │─── 2. TRANSFER_REQUEST [TotalFiles, TotalSize, RootElements] ───────►│
  │                                                                      │ [Prompt User]
  │◄── 3. TRANSFER_RESPONSE [Accepted: true/false, Reason] ──────────────│
  │                                                                      │
  │    === For each file in selection ===                                │
  │─── 4. FILE_BEGIN ───────────────────────────────────────────────────►│
  │─── 5. FileItemMetadata [RelativePath, RootName, Size] ──────────────►│
  │─── 6. Binary Payload Stream (1 MB sequential chunks) ───────────────►│ [Write to Disk]
  │                                                                      │
  │    === End of transmission ===                                       │
  │─── 7. TRANSFER_END ─────────────────────────────────────────────────►│ [Finalize Result]
  │                                                                      │
```

- **Framing**: Every metadata message is preceded by a **4-byte length prefix** (32-bit integer, little-endian) followed by the UTF-8 encoded JSON payload.
- **Sanity Limit**: Receiver enforces a strict **10 MB maximum size limit** on length-prefixed metadata headers to prevent memory allocation denial-of-service attacks.
- **Streaming Pipeline**: File payloads are read into and written from reusable 1 MB buffers using `System.Buffers.ArrayPool<byte>.Shared` to maintain a constant, minimal RAM footprint (<100 MB total process working set) regardless of file size.
- **Single TCP Stream**: Each transfer session runs across a dedicated TCP connection (`SocketOptionName.KeepAlive` enabled). Single-stream architecture guarantees strict in-order arrival for directory hierarchies and individual files.

---

## 10. Performance & Link Speed Specifications

### Comprehensive Throughput Matrix Across Ethernet Tiers

| Ethernet Tier | Physical Line Rate | Practical TCP Max (MTU 1500) | Single-Stream App Throughput | Multi-Stream / Pipelined Potential | Storage Requirement | Minimum Cable Standard |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **1 GbE (Gigabit)** | 1000 Mbps (125.0 MB/s) | ~118.5 MB/s | **110 – 115 MB/s** | **115 MB/s** (Line rate saturated) | Fast HDD or SATA SSD | Cat 5e (up to 100m) |
| **2.5 GbE** | 2500 Mbps (312.5 MB/s) | ~296.0 MB/s | **270 – 285 MB/s** | **285 – 295 MB/s** (Line rate saturated) | SATA III SSD (≥500 MB/s) or NVMe | Cat 5e / Cat 6 |
| **5 GbE** | 5000 Mbps (625.0 MB/s) | ~592.0 MB/s | **450 – 540 MB/s** | **560 – 590 MB/s** | PCIe Gen 3 NVMe SSD (≥1500 MB/s) | Cat 6 (up to 100m) |
| **10 GbE (Standard MTU 1500)** | 10000 Mbps (1250.0 MB/s) | ~1184.0 MB/s | **450 – 850 MB/s** | **1100 – 1150 MB/s** (Multi-Stream) | PCIe Gen 3/4 NVMe SSD (≥2000 MB/s) | Cat 6 (up to 55m) / Cat 6a / SFP+ DAC |
| **10 GbE (Jumbo MTU 9000)** | 10000 Mbps (1250.0 MB/s) | ~1235.0 MB/s | **850 – 1100 MB/s** | **1180 – 1220 MB/s** | PCIe Gen 4 NVMe SSD (≥3500 MB/s) | Cat 6a / SFP+ DAC / OM3 Fiber |

---

### Technical Bottlenecks & Optimization Breakdown

#### 1. Why Single-Stream Plain TCP Tops Out at ~850 MB/s on 10 GbE
- **Packet-per-Second Interrupt Load**: On standard 1500-byte MTU, transferring at 10 Gbps requires processing **~800,000 packets per second**. A single TCP socket executes its protocol state machine and ACK processing on a single CPU core, causing interrupt handling saturation.
- **Synchronous I/O Ping-Pong**: In a basic read-write loop (`Read from disk -> Write to socket`), the network transmitter sits idle while disk blocks are fetched, and the disk queue sits idle while socket buffers flush.

#### 2. How Multi-Stream & Pipelining Achieve 1.15 GB/s (10 GbE Saturation)
- **Multi-Stream TCP (4–8 Concurrent Channels)**: Spreads the packet transmission workload across multiple CPU cores, eliminating single-core interrupt bottlenecks.
- **Asynchronous Pipelining (`System.Threading.Channels`)**: Dedicated worker threads read from NVMe storage into memory buffers while network threads simultaneously transmit previous buffers, maintaining 100% continuous duty cycle on both storage and network hardware.
- **Socket Options**: Disabling Nagle's algorithm (`NoDelay = true`) and expanding socket buffer windows (`SendBufferSize` / `ReceiveBufferSize` = 4 MB) prevents window stalling across high bandwidth-delay product links.

#### 3. Storage Hardware Prerequisites
- **1 GbE (115 MB/s)**: Any modern SATA SSD or fast 7200 RPM mechanical HDD.
- **2.5 GbE (285 MB/s)**: SATA III SSD (Samsung 870 EVO, Crucial MX500) or any NVMe SSD.
- **5 GbE (570 MB/s)**: PCIe Gen 3 NVMe SSD (standard SATA SSDs cap at ~550 MB/s).
- **10 GbE (1,150 MB/s)**: PCIe Gen 3 ×4 or Gen 4 NVMe SSD with sustained sequential write capabilities outside of SLC cache.

---

## 11. Reliability

- **Aggressive Physical Link Watchdogs**: Direct point-to-point Ethernet links do not always immediately signal OS socket aborts when a physical cable is unplugged. EtherTransfer wraps every socket read and write with active timeout watchdogs:
  - **Metadata Operations**: 2-second strict watchdog timeout.
  - **Payload Chunks**: 5-second watchdog per 1 MB chunk. If a chunk cannot be read or written within 5 seconds on a local cable, the connection is treated as physically severed and cleanly aborted.
- **Failure Recovery & Cleanup**:
  - **Single-Item Transfer Failure**: If a transfer containing a single file or directory is cancelled or interrupted mid-stream, `TransferReceiver` cleanly disposes of the file handle and deletes the partial/corrupt file and empty directories from disk.
  - **Multi-Item Transfer Failure**: If a transfer contains multiple distinct items (e.g., 5 files), any item that was 100% completed prior to the interruption is preserved; only the in-flight incomplete item is rolled back.
- **Skipped File Handling**: If a file cannot be read on the sender side due to locked file handles (`FileShare.ReadWrite`), permissions (`UnauthorizedAccessException`), or sudden deletion, the sender emits a `FILE_SKIP` message. The receiver safely skips the entry without aborting the entire multi-gigabyte session.

---

## 12. Security Model

| Property | Status | Implementation Details |
| :--- | :--- | :--- |
| **Transport Encryption** | **None (Plaintext)** | Data is streamed as unencrypted binary TCP payloads. Designed for direct physical cables or trusted private switches. |
| **Peer Authentication** | **Unauthenticated** | Any device running EtherTransfer on the broadcast domain will appear in the peer list. |
| **Authorization Gate** | **Manual UI Consent** | Receivers must explicitly click **Accept** or **Decline** upon receiving a `TRANSFER_REQUEST`. Transfers cannot start without receiver consent. |
| **Destination Sandboxing** | **Enforced** | All paths are verified via `PathSanitizer.SanitizeRelativePath()`. Traversals (`../`), null bytes, control characters, and absolute paths escaping the destination folder are rejected. |
| **Collision Handling** | **Enforced** | `PathSanitizer.ResolveCollision()` automatically appends an incrementing counter (`file (1).ext`) if a file of the same name already exists at the destination. |
| **Cryptographic Integrity** | **Planned** | Protocol defines `FileChecksumMessage` (SHA-256), but on-the-wire hash verification is currently planned. Current transfers rely on TCP CRC and exact byte-length checks. |

---

## 13. File and Folder Behavior

- **Deep Directory Trees**: Scans subfolders recursively using `Directory.EnumerateFiles` with `IgnoreInaccessible = true`. Directory structures are reconstructed identically on the receiver.
- **Filename Sanitization**: Windows reserved names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`) are automatically prefixed with an underscore (`_CON.txt`) to maintain safety across cross-platform transfers.
- **Character Encoding**: File and folder names are transmitted as UTF-8 JSON strings, supporting full Unicode characters, spaces, and international alphabets.
- **Symlinks & Special Files**: Symbolic links and special system device pipes are skipped during scanning to prevent circular directory loops or unreadable system streams.

---

## 14. Portable Mode

EtherTransfer provides both installer-based and portable distributions:

- **Self-Contained Executable**: Portable releases are built as fully self-contained single-file executables with the .NET runtime bundled directly inside. No external .NET runtime installation is required.
- **Settings Storage**: User preferences (such as the custom peer name) are stored in `%AppData%\EtherTransfer\settings.json` (Windows) or `~/.config/EtherTransfer/settings.json` (Linux). This ensures your device identity and name persist across runs even if the portable executable is moved between directories or launched from a removable USB flash drive.
- **Firewall Integration**: On Windows, portable builds request administrative execution rights (`app.portable.manifest`) only when necessary to automatically register an inbound Windows Defender Firewall rule for the executable, ensuring peer discovery and transfer sockets function without manual firewall configuration.

---

## 15. Platform Support

| Platform | Architecture | Status | Notes |
| :--- | :--- | :--- | :--- |
| **Windows 11 / 10** | x64 | **Verified & Tested** | Inno Setup installer and standalone portable builds available. |
| **Windows 11 / 10** | x86 (32-bit) | **Verified & Tested** | Full 32-bit compatibility build. |
| **Linux (glibc 2.27+)** | x64 | **Verified & Tested** | Tested on Ubuntu, Debian, Fedora, Arch. Requires NetworkManager (`nmcli`). |
| **macOS (Sonoma / Sequoia)** | Apple Silicon / Intel | **Experimental / In Progress** | Avalonia UI builds compile; link-local integration undergoing active validation. |

---

## 16. Firewall Requirements

EtherTransfer requires open local communication on two ports:

- **UDP Port 50000**: Inbound & Outbound for peer discovery broadcast and heartbeats.
- **TCP Port 55000** (or dynamic fallback port): Inbound listener for incoming file transfer streams.

### Windows
- The **Inno Setup Installer** automatically creates an inbound firewall rule for `EtherTransfer.exe` covering Private and Public network profiles.
- The **Portable Build** includes runtime firewall verification via `FirewallHelper.EnsureFirewallRule()`.

### Linux
- The `install_linux.sh` installer automatically configures the active firewall:
  - **UFW**: `ufw allow 50000/udp && ufw allow 55000/tcp`
  - **Firewalld**: `firewall-cmd --permanent --add-port=50000/udp --add-port=55000/tcp`
  - **iptables**: Appends inbound `ACCEPT` rules for ports 50000 and 55000.

---

## 17. Troubleshooting

<details>
<summary><b>Peer device does not appear in the peer list</b></summary>
<br>

1. **Verify Link State**: Ensure the cable is securely connected. The bottom status bar in EtherTransfer should show `Ready` with a valid IP (e.g. `169.254.x.x` or `192.168.x.x`).
2. **Check Firewall**: Ensure your OS firewall is not blocking UDP port `50000` or TCP port `55000`. On Windows, check Windows Defender Firewall under "Allowed Apps".
3. **Corporate VPNs**: Certain enterprise VPN clients (Cisco AnyConnect, GlobalProtect, Zscaler) enforce full-tunnel route interception or disable local LAN discovery. Disconnect the VPN during local transfers if peers cannot discover each other.
4. **Linux Link-Local Assignment**: If your Linux interface is stuck in `Configuring` or `ConfigError`, ensure `NetworkManager` is installed and running (`systemctl status NetworkManager`). Click **Retry** in the application interface.
</details>

<details>
<summary><b>Transfer rate is lower than expected</b></summary>
<br>

1. **Storage Bottlenecks**: Transfers over 2.5GbE or 10GbE are frequently constrained by destination storage write speeds. Verify that the target drive is a fast SATA or NVMe SSD.
2. **USB-Ethernet Adapters**: Ensure USB-C or USB-A Ethernet adapters are plugged into USB 3.0+ (SuperSpeed) ports. USB 2.0 ports physically bottleneck transfers to ~35–40 MB/s.
</details>

---

## 18. Comparison With Existing Solutions

| Feature | EtherTransfer | LocalSend | Windows/Samba SMB | Syncthing | USB External Drive |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Primary Focus** | Direct cable / unmanaged Ethernet transfers | Local Wi-Fi / LAN multi-device sharing | Network file sharing & persistent drive mounts | Continuous bi-directional folder sync | Physical portable storage |
| **Direct Cable Workflow** | **Automated** (pins physical Ethernet, ignores Wi-Fi, auto-configures Linux) | Semi-manual (defaults to Wi-Fi route unless link-local IP is typed manually) | Requires manual static IP or NetBIOS discovery | Complex (device pairing & folder pairing required) | Plug in, copy twice |
| **Zero Router Required** | **Yes** (Link-Local APIPA / `nmcli` auto-config) | Yes (over APIPA/Hotspot), but requires manual IP entry if Wi-Fi is active | Requires manual IP configuration without router | Requires local network or relay server | **Yes** (Offline physical media) |
| **Mobile OS Support** | Desktop (Windows, Linux, macOS) | **Yes** (Android, iOS, macOS, Windows, Linux) | Limited on mobile without 3rd party apps | Android only | OTG Adapter required |
| **Deep Folder Trees** | **Yes** (Native recursion) | Yes | Yes (Native file explorer) | Yes | Yes |
| **Encryption** | None (Plaintext local) | **Yes** (TLS / HTTPS) | **Yes** (SMB 3.1.1 AES-128/256) | **Yes** (TLS 1.3) | Optional (BitLocker) |
| **Transfer Type** | Ad-hoc bulk transfer | Ad-hoc message/file | File streaming / browsing | Delta sync / block level | Physical copy |
| **Resumable** | No (Rolls back partial files) | No | **Yes** (via Robocopy/Explorer) | **Yes** (Block level) | **Yes** (Manual resume) |
| **Portability** | Standalone Single-File EXE | Packaged app / Portable | Built into OS | Standalone binary | N/A |

---

## 19. Limitations

1. **No Application-Level Cryptographic Hashes (Yet)**: Hash verification (`FileChecksumMessage`) is currently on the roadmap; current integrity checking relies on TCP transport-layer checksums and byte-length verification.
2. **No Byte-Offset Resume**: Interrupted transfers cannot resume where they left off; single-item transfers roll back partial files, requiring the session to be restarted.
3. **No Layer 3 Routing**: Discovery uses UDP subnet broadcasts and cannot discover peers across routers or distinct subnets without direct IP entry.
4. **Unencrypted Plaintext**: Sockets transmit raw bytes over the local wire without TLS; do not use across untrusted, shared public networks.
5. **Single-Stream TCP**: Transfers use a single TCP connection per session, which may not saturate 10GbE links on standard 1500 MTU without multi-streaming.

---

## 20. FAQ

#### Q: Do I need a crossover cable?
**A:** No, not on any modern computer. All Gigabit Ethernet (1000BASE-T) and faster network cards support **Auto-MDI/MDIX** (IEEE 802.3ab), which automatically handles pin crossover inside the hardware. A standard straight-through Cat 5e / Cat 6 patch cable is all that is required. Crossover cables are only necessary for legacy 10/100 Mbps NICs lacking Auto-MDIX.

#### Q: Can I use this if both computers are also on Wi-Fi?
**A:** Yes. EtherTransfer inspects active network adapters, detects physical Ethernet adapters, and binds sockets directly to the physical Ethernet link-local IP. You can continue browsing the web on Wi-Fi while gigabyte files stream across the cable.

#### Q: Can I transfer Steam games or large software folders?
**A:** Yes. You can drag and drop entire directory hierarchies (e.g. `steamapps/common/<GameFolder>` and the corresponding `appmanifest_<id>.acf` file). EtherTransfer recreates the folder tree accurately on the destination drive.

#### Q: Does it work through an Ethernet switch?
**A:** Yes. Connecting multiple machines to an unmanaged or managed Ethernet switch allows EtherTransfer to discover all peers connected to that switch.

---

## 21. Architecture

```text
EtherTransfer Solution Structure
├── EtherTransfer.Core          # Shared data models, protocol schemas, and SettingsManager
├── EtherTransfer.Network       # UDP discovery service, TCP server, interface detection & link state monitor
├── EtherTransfer.Transfer      # TCP streaming protocol, file scanning, ArrayPool buffers & PathSanitizer
├── EtherTransfer.Services      # DeviceService, TransferService orchestration, and FirewallHelper
├── EtherTransfer.UI            # Avalonia UI XAML views, view models, and asset converters
└── EtherTransfer.Tests         # NUnit test suites for link monitoring, path security, and framing
```

---

## 22. Development

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2026, JetBrains Rider, or VS Code with C# Dev Kit.

### Building & Running Locally
```bash
# Clone the repository
git clone https://github.com/divyviradiya2/ethertransfer.git
cd ethertransfer

# Run all unit and integration tests
dotnet test

# Launch the desktop UI in debug mode
dotnet run --project EtherTransfer.UI
```

### Publishing Standalone Releases
```bash
# Self-contained Windows x64 single-file executable
dotnet publish EtherTransfer.UI -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Self-contained Linux x64 single-file executable
dotnet publish EtherTransfer.UI -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
```

---

## 23. Roadmap

- [ ] **On-The-Wire SHA-256 Checksum Verification**: Integrate `FileChecksumMessage` into the active receive loop to verify file integrity cryptographically.
- [ ] **Multi-Stream TCP Transmission**: Implement concurrent TCP streams for 10GbE / multi-gigabit connections to maximize utilization on high-bandwidth links.
- [ ] **Optional TLS Encryption Mode**: Provide optional TLS encryption for transfers over shared office LANs.
- [ ] **Multicast DNS / Discovery**: Support mDNS / SSDP discovery to enable discovery across complex subnets.
- [ ] **macOS Native Validation**: Complete testing and packaging for macOS (DMG / Homebrew).

---

## 24. License & Open-Source Philosophy

EtherTransfer is free and open-source software provided under the **[MIT License](LICENSE)**.

> **A Note on Tool Choice**:
> EtherTransfer was built to solve a concrete real-world problem with maximum simplicity: moving large files directly across an Ethernet cable without network administration overhead. 
> 
> Under the MIT License, you have the full freedom to use it, inspect it, modify it, or completely ignore it if your existing tools (SMB, LocalSend, Syncthing, Netcat) already satisfy your needs. EtherTransfer exists for anyone who values a zero-fuss, plug-and-transfer application that just works out of the box.
