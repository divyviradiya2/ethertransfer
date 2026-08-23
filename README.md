<div align="center">

<table border="0" cellpadding="16">
  <tr>
    <td align="center" width="180">
      <img src="EtherTransfer.UI/Assets/logo.png" alt="EtherTransfer Logo" width="160" />
    </td>
    <td align="center">
      <h1 style="border: none; margin-bottom: 8px;">EtherTransfer</h1>
      <p><b>Direct peer-to-peer file and folder transfer over physical Ethernet links.</b></p>
      <p>Decentralized, zero-configuration data movement without routers, cloud servers, or manual IP setup.</p>
      <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 10" /></a>
      <img src="https://img.shields.io/badge/Windows-x64%20%2F%20x86-0078D4?style=flat-square&logo=windows" alt="Windows" />
      <img src="https://img.shields.io/badge/Linux-x64-E95420?style=flat-square&logo=linux" alt="Linux" />
      <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-22C55E?style=flat-square" alt="MIT License" /></a>
      <a href="https://divyviradiya2.github.io/ethertransfer/"><img src="https://img.shields.io/badge/Website-Live-2563EB?style=flat-square&logo=googlechrome&logoColor=white" alt="Live Website" /></a>
      <a href="https://divyviradiya2.github.io/ethertransfer/docs.html"><img src="https://img.shields.io/badge/Docs-Interactive_Portal-8B5CF6?style=flat-square&logo=gitbook&logoColor=white" alt="Documentation Portal" /></a>
      <br><br>
      <a href="https://github.com/divyviradiya2/ethertransfer/releases/latest/download/EtherTransfer_Setup_x64.exe"><img src="https://img.shields.io/badge/Download-Windows%2064--bit-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="Download 64-bit" /></a>
      <a href="https://github.com/divyviradiya2/ethertransfer/releases/latest/download/EtherTransfer_Setup_x86.exe"><img src="https://img.shields.io/badge/Download-Windows%2032--bit-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="Download 32-bit" /></a>
    </td>
  </tr>
</table>

<br>

<img src="docs/network_diagram.jpg" alt="EtherTransfer Direct Point-to-Point Topology" width="840" style="border-radius: 12px; box-shadow: 0 4px 16px rgba(0,0,0,0.12);" />

</div>

<br>

---

## Highlights

- **Zero Network Configuration**: Plug an Ethernet cable directly between two computers. Operating systems negotiate IPv4 Link-Local addresses automatically ([RFC 3927](https://datatracker.ietf.org/doc/html/rfc3927)).
- **Automatic Peer Discovery**: Instant peer detection across local physical links via UDP broadcast on Port `50000`.
- **High-Throughput TCP Streaming**: Framed binary streaming on Port `55000` with 1 MB reusable buffer pools ([`ArrayPool<byte>`](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.arraypool-1)), keeping process RAM under 100 MB.
- **Physical Interface Pinning**: File data saturates physical Ethernet while active Wi-Fi connections remain untouched for uninterrupted web browsing.
- **Deep Directory Tree Reconstruction**: Recursively transfers entire nested folder hierarchies without intermediate `.zip` or `.tar` compression.
- **Cross-Platform Interoperability**: Seamless file movement between Windows 10/11 and Linux distributions.

---

## Documentation Portal

Complete architectural specifications, protocol schemas, security analyses, and developer documentation are published in the **[EtherTransfer Documentation Portal](https://divyviradiya2.github.io/ethertransfer/docs.html)**:

| Category | Topics Covered | Link |
| :--- | :--- | :--- |
| **Getting Started** | Overview, problem space, Auto-MDIX ([IEEE 802.3ab](https://standards.ieee.org/)), cabling standards, supported topologies | [Read Chapter &rarr;](https://divyviradiya2.github.io/ethertransfer/docs.html#getting-started) |
| **Architecture & Networking** | RFC 3927 Link-Local APIPA, [`DiscoveryService.cs`](https://github.com/divyviradiya2/ethertransfer/blob/main/EtherTransfer.Network/UdpDiscovery/DiscoveryService.cs), UUID session identity, [`EthernetLinkMonitor.cs`](https://github.com/divyviradiya2/ethertransfer/blob/main/EtherTransfer.Network/NetworkInterfaces/EthernetLinkMonitor.cs) state machine, subnet isolation | [Read Chapter &rarr;](https://divyviradiya2.github.io/ethertransfer/docs.html#architecture-networking) |
| **Transfer Engine & Protocols** | TCP length-prefixed framing, 10 MB DoS guard, [`ArrayPool<byte>`](https://github.com/divyviradiya2/ethertransfer/blob/main/EtherTransfer.Transfer/TransferSender.cs) zero-allocation streaming, 2s/5s watchdog timeouts, atomic rollbacks | [Read Chapter &rarr;](https://divyviradiya2.github.io/ethertransfer/docs.html#transfer-engine) |
| **Performance & Hardware** | 1GbE / 2.5GbE / 5GbE / 10GbE benchmark matrix, single-core TCP interrupt bottlenecks, NVMe storage prerequisites, SLC cache dynamics | [Read Chapter &rarr;](https://divyviradiya2.github.io/ethertransfer/docs.html#performance-benchmarks) |
| **Security & System Transparency** | Plaintext local wire rationale, manual UI consent gate, [`PathSanitizer.cs`](https://github.com/divyviradiya2/ethertransfer/blob/main/EtherTransfer.Transfer/PathSanitizer.cs) traversal guards, installer firewall rules, zero telemetry | [Read Chapter &rarr;](https://divyviradiya2.github.io/ethertransfer/docs.html#security-sandboxing) |
| **Developer & Contributing** | 6-project solution architecture, .NET 10 SDK setup, test suites ([`PathSanitizerTests.cs`](https://github.com/divyviradiya2/ethertransfer/blob/main/EtherTransfer.Tests/PathSanitizerTests.cs)), single-file publishing, Inno Setup | [Read Chapter &rarr;](https://divyviradiya2.github.io/ethertransfer/docs.html#developer-guide) |

---

## Quick Start

```text
         Ethernet Cable (Cat 5e/6/6a/7/8)        
     Sender Computer         Receiver Computer    
  (EtherTransfer open)     UDP Discovery (Port 50000 Broadcast)     (EtherTransfer open)   
   IP: 169.254.x.x         Framed TCP Stream (Port 55000)      IP: 169.254.x.x       
                                                 
```

1. **Launch**: Open EtherTransfer on both computers.
2. **Connect**: Connect both computers with a standard RJ-45 Ethernet cable (or connect both to the same switch/LAN).
3. **Discover**: Devices detect each other within 1–5 seconds and appear in the Discovered Devices list.
4. **Select & Send**: Drag and drop files/folders or select them via the file picker, then click **Send**.
5. **Accept**: The receiving machine displays sender details and payload size. Select the destination folder and click **Accept**.

---

## Installation & Downloads

### Windows (10 & 11)
- **Installer (64-bit)**: [EtherTransfer_Setup_x64.exe](https://github.com/divyviradiya2/ethertransfer/releases/latest/download/EtherTransfer_Setup_x64.exe)
- **Installer (32-bit)**: [EtherTransfer_Setup_x86.exe](https://github.com/divyviradiya2/ethertransfer/releases/latest/download/EtherTransfer_Setup_x86.exe)
- **Portable Single-File**: Available under [Latest Releases](https://github.com/divyviradiya2/ethertransfer/releases/latest).

### Linux (Ubuntu, Debian, Fedora, Arch)
Run the automated installation script in your terminal:
```bash
curl -sSL https://raw.githubusercontent.com/divyviradiya2/ethertransfer/master/install_linux.sh | sudo bash
```

---

## Performance Summary

| Ethernet Tier | Physical Line Rate | Single-Stream Throughput | Multi-Stream Potential | Storage Requirement |
| :--- | :--- | :--- | :--- | :--- |
| **1 GbE (Gigabit)** | 125.0 MB/s | **110 – 115 MB/s** | **115 MB/s** (Saturated) | SATA SSD or 7200 RPM HDD |
| **2.5 GbE** | 312.5 MB/s | **270 – 285 MB/s** | **285 – 295 MB/s** | SATA III SSD (&ge;500 MB/s) |
| **5 GbE** | 625.0 MB/s | **450 – 540 MB/s** | **560 – 590 MB/s** | PCIe Gen 3 NVMe SSD |
| **10 GbE (Standard MTU 1500)** | 1250.0 MB/s | **450 – 850 MB/s** | **1,100 – 1,150 MB/s** | PCIe Gen 3/4 NVMe SSD |
| **10 GbE (Jumbo MTU 9000)** | 1250.0 MB/s | **850 – 1,100 MB/s** | **1,180 – 1,220 MB/s** | PCIe Gen 4 NVMe SSD |

*For architectural analysis of interrupt saturation and memory pipelining, visit the [Performance Documentation](https://divyviradiya2.github.io/ethertransfer/docs.html#performance-benchmarks).*

---

## Developer Quick Start

```bash
# Clone the repository
git clone https://github.com/divyviradiya2/ethertransfer.git
cd ethertransfer

# Run all test suites
dotnet test

# Launch the desktop UI application
dotnet run --project EtherTransfer.UI
```

For guidelines on asynchronous discipline, memory pooling, and packaging releases, check out the **[Contributing Guide](CONTRIBUTING.md)** and the **[Web Developer Guide](https://divyviradiya2.github.io/ethertransfer/docs.html#developer-guide)**.

---

## License

EtherTransfer is free and open-source software distributed under the **[MIT License](LICENSE)**.

Developed by **Divy Viradiya ([DS Labs](https://github.com/divyviradiya2))**.