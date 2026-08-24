<div align="center">

<img src="EtherTransfer.UI/Assets/logo.png" alt="EtherTransfer Logo" width="120" />

# EtherTransfer

**Direct peer-to-peer file transfer over physical Ethernet links.**

Fast, zero-configuration local data movement without routers, cloud servers, or setup.

[![Version](https://img.shields.io/badge/version-0.3.0-blue?style=flat-square)](https://github.com/divyviradiya2/ethertransfer/releases)
[![License MIT](https://img.shields.io/badge/license-MIT-green?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-lightgrey?style=flat-square)](#downloads)

[![Website](https://img.shields.io/badge/website-visit-blue?style=flat-square)](https://divyviradiya2.github.io/ethertransfer/)
[![Documentation](https://img.shields.io/badge/docs-guide-blue?style=flat-square)](https://divyviradiya2.github.io/ethertransfer/docs.html)
[![Downloads](https://img.shields.io/badge/downloads-releases-blue?style=flat-square)](#downloads)

<br>

<img src="docs/network_diagram.svg" alt="EtherTransfer Direct Point-to-Point Architecture Diagram" width="860" />

</div>

<br>

## Features

- **Zero Configuration**: Connect two PCs directly with an Ethernet cable. IPv4 Link-Local addresses negotiate automatically ([RFC 3927](https://datatracker.ietf.org/doc/html/rfc3927)).
- **Instant Discovery**: Automatic peer detection across local links via UDP broadcast on Port `50000`.
- **Pipelined Transfer Engine**: 32 MB double-buffered channel pipeline (`System.Threading.Channels`) with unbuffered direct kernel I/O for sustained 115 MB/s Gigabit wire saturation.
- **Ultra-Fast Folder Streaming**: Zero-allocation binary framing with receiver-side multi-worker disk ingestion (4–16 threads) to overcome NTFS small-file latency.
- **Metadata Preservation**: Retains original file and directory creation (`CreationTimeUtc`) and modification (`LastWriteTimeUtc`) timestamps across transfers.
- **Interface Isolation**: Saturates the physical Ethernet cable while your active Wi-Fi remains free for uninterrupted internet browsing.
- **Cross-Platform**: Windows 10/11 & Linux supported.

---

## Documentation

Full architectural specifications, protocol framing schemas, hardware benchmarks, and developer guides are published on the **[Documentation Portal](https://divyviradiya2.github.io/ethertransfer/docs.html)**.

- [Getting Started & Supported Topologies](https://divyviradiya2.github.io/ethertransfer/docs.html#getting-started)
- [Architecture & RFC 3927 Link-Local Networking](https://divyviradiya2.github.io/ethertransfer/docs.html#architecture-networking)
- [Transfer Engine & Protocol Framing](https://divyviradiya2.github.io/ethertransfer/docs.html#transfer-engine)
- [Speed Benchmarks & Hardware Prerequisites](https://divyviradiya2.github.io/ethertransfer/docs.html#performance-benchmarks)
- [Security Model & Path Sanitization](https://divyviradiya2.github.io/ethertransfer/docs.html#security-sandboxing)
- [Developer Guide & Solution Architecture](https://divyviradiya2.github.io/ethertransfer/docs.html#developer-guide)

---

## Downloads

### Windows

| Package | Architecture | Download |
| :--- | :--- | :--- |
| **Installer (.exe)** | 64-bit | [EtherTransfer_Setup_x64.exe](https://github.com/divyviradiya2/ethertransfer/releases/latest/download/EtherTransfer_Setup_x64.exe) |
| **Installer (.exe)** | 32-bit | [EtherTransfer_Setup_x86.exe](https://github.com/divyviradiya2/ethertransfer/releases/latest/download/EtherTransfer_Setup_x86.exe) |
| **Portable (.exe)** | 64-bit | [EtherTransfer_Portable_x64.exe](https://github.com/divyviradiya2/ethertransfer/releases/latest/download/EtherTransfer_Portable_x64.exe) |
| **Portable (.exe)** | 32-bit | [EtherTransfer_Portable_x86.exe](https://github.com/divyviradiya2/ethertransfer/releases/latest/download/EtherTransfer_Portable_x86.exe) |

### Linux

Install EtherTransfer directly using the automated setup script:

```bash
curl -sSL https://raw.githubusercontent.com/divyviradiya2/ethertransfer/master/install_linux.sh | sudo bash
```

To uninstall:

```bash
curl -sSL https://raw.githubusercontent.com/divyviradiya2/ethertransfer/master/uninstall_linux.sh | sudo bash
```

---

## Development

```bash
# Clone and test
git clone https://github.com/divyviradiya2/ethertransfer.git
cd ethertransfer
dotnet test

# Run UI
dotnet run --project EtherTransfer.UI
```

---

## License

Distributed under the [MIT License](LICENSE). Developed by [Divy Viradiya](https://github.com/divyviradiya2).
