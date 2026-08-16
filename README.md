<div align="center">

<table border="0" cellpadding="16">
  <tr>
    <td align="center" width="200">
      <img src="EtherTransfer.UI/Assets/logo.png" alt="EtherTransfer" width="180" />
    </td>
    <td align="center">
      <h1 style="border: none; margin-bottom: 10px;">EtherTransfer</h1>
      <p><b>Lightning-fast, direct PC-to-PC file transfers, no router required.</b></p>
      <p>A cross-platform application designed for maximum reliability and enterprise-level robustness over direct Ethernet connections.</p>
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

<img src="docs/network_diagram.jpg" alt="How it works" width="880" style="border-radius: 12px; box-shadow: 0 4px 12px rgba(0,0,0,0.15);" />

<br><br>

</div>

---

EtherTransfer is a high-performance native desktop app built with .NET 10 and Avalonia UI. It allows two devices connected directly via an Ethernet cable to discover each other instantly and transfer deep folder structures at maximum link speed (1Gbps, 10Gbps, etc.).

There is no need for a router, DHCP server, or manual IP configuration.

<br>



## Features

<details>
<summary><b> &nbsp;True Plug-and-Play Discovery</b></summary>
<br>

- Connect two machines via an Ethernet cable, and they automatically discover each other using UDP broadcasts.
- Works purely offline. No router, internet, or DHCP configuration required.
- Instantly populates the peer list with available machines on the local link.

</details>

<details>
<summary><b> &nbsp;Blazing Fast Transfers</b></summary>
<br>

- Custom optimized TCP streaming protocol to maximize Ethernet throughput.
- Designed to saturate 1Gbps, 2.5Gbps, and 10Gbps links without memory bloat.
- Drag and drop gigabytes of files and watch them transfer in seconds.

</details>

<details>
<summary><b> &nbsp;Enterprise-Level Robustness</b></summary>
<br>

- Dynamic port binding and link-state monitoring.
- Built-in ultra-aggressive watchdogs: gracefully handles physical edge cases like unplugged cables or locked files without freezing.
- Automatically handles deep, complex folder structures, accurately recreating directory trees on the receiver side.
- Built-in path sanitization prevents directory traversal attacks (e.g. `../../Windows/System32`).

</details>

<details>
<summary><b> &nbsp;Hardware, Cable & Adapter Compatibility</b></summary>
<br>

- **Standard RJ-45 Ethernet Cables (Cat 5e, 6, 6a, 7, 8):** Fully supported. All modern Gigabit+ network interface cards (NICs) support **Auto-MDI/MDIX** (IEEE 802.3ab), which automatically negotiates transmit/receive pins over standard straight-through cables.
- **Legacy Crossover Cables:** 100% compatible out of the box.
- **Laptops Without RJ-45 Ports:** Works with standard USB-A, USB-C, and Thunderbolt Ethernet adapters/dongles (Realtek RTL8153, ASIX AX88179, etc.) with zero driver configuration.
- **Enterprise & High-End Creator Media:** Supports SFP+ Direct Attach Copper (DAC) cables and Fiber Optic (LC-LC OM3/OM4/OS2) links over PCIe 10G/25G/40G/100G network cards.
- **Direct Thunderbolt Bridging:** Supports direct Thunderbolt 3 / 4 / USB4 point-to-point cables via OS link-local Thunderbolt networking interfaces.

</details>

<details>
<summary><b> &nbsp;Network Topologies (Direct Cable vs Ethernet Switch)</b></summary>
<br>

- **Point-to-Point (1-to-1):** Connect two computers directly with a single cable without a router.
- **Multi-Device Switch (1-to-Many / Many-to-Many):** Connect multiple computers to an unmanaged or managed Ethernet switch. EtherTransfer broadcasts UDP discovery frames across the subnet, automatically discovering every connected machine and displaying them in the peer list for selective transfers.

</details>

<br>

## Installation

### Windows
Download the setup executable from the latest release and run it:
- [Windows 64-bit Setup](https://github.com/divyviradiya2/ethertransfer/releases/latest/download/EtherTransfer_Setup_x64.exe)
- [Windows 32-bit Setup](https://github.com/divyviradiya2/ethertransfer/releases/latest/download/EtherTransfer_Setup_x86.exe)

### Linux
Run this single command in your terminal to install instantly:
```bash
curl -sSL https://raw.githubusercontent.com/divyviradiya2/ethertransfer/master/install_linux.sh | sudo bash
```
*(To uninstall, simply run: `curl -sSL https://raw.githubusercontent.com/divyviradiya2/ethertransfer/master/uninstall_linux.sh | sudo bash`)*

<br>

## Quick start

```
1.  Open the application     →  Launch EtherTransfer on both computers.
2.  Connect the PCs          →  Plug a direct Ethernet cable between them.
3.  Wait for discovery       →  The computers will appear in the peer list automatically.
4.  Drag & Drop              →  Drop files/folders onto a peer to send.
5.  Accept transfer          →  The receiving computer prompts to accept and choose a save location.
```

<br>

## Developer Guide

<details>
<summary><b> &nbsp;Architecture</b></summary>
<br>

EtherTransfer is structured into four main components:
1. `EtherTransfer.Core`: Shared models, configuration, and settings logic.
2. `EtherTransfer.Network`: Core UDP discovery, TCP listener logic, and low-level IP interface parsing.
3. `EtherTransfer.Transfer`: High-performance TCP streaming protocol, file scanning, and serialization.
4. `EtherTransfer.UI`: The Avalonia-based graphical user interface and view models.

</details>

<details>
<summary><b> &nbsp;Build from source</b></summary>
<br>

**Prerequisites:** [.NET 10 SDK](https://dotnet.microsoft.com/)

```bash
git clone https://github.com/divyviradiya2/ethertransfer.git
cd ethertransfer

# Run the app locally
dotnet run --project EtherTransfer.UI

# Publish a self-contained release build for Windows x64
dotnet publish EtherTransfer.UI -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

</details>

<br>

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
