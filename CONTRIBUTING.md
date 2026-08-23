# Contributing to EtherTransfer

> **Interactive Documentation**: For the interactive web version with searchable architecture references and diagrams, visit the **[Developer Guide on the Docs Portal](https://divyviradiya2.github.io/ethertransfer/docs.html#developer-guide)**.

Thank you for your interest in contributing to EtherTransfer! We build EtherTransfer with a strong focus on **reliability, memory efficiency, and rock-solid cross-platform networking**.

This guide covers everything you need to know to set up your environment, understand the codebase architecture, run tests, and submit changes.

---

## Table of Contents

1. [Development Setup](#development-setup)
2. [Building, Running & Testing](#building-running--testing)
3. [Architecture & Solution Structure](#architecture--solution-structure)
4. [How the Core Systems Work](#how-the-core-systems-work)
5. [Code Guidelines & Standards](#code-guidelines--standards)
6. [Packaging & Publishing Releases](#packaging--publishing-releases)
7. [Submitting a Pull Request](#submitting-a-pull-request)

---

## Development Setup

### Prerequisites

- **.NET 10 SDK** (version 10.0 or later): [Download .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0)
- **IDE / Editor**:
  - Visual Studio 2026 / 2022 (with .NET desktop development workload)
  - JetBrains Rider 2024.3+ (with Avalonia plugin recommended)
  - VS Code (with C# Dev Kit and Avalonia for VS Code extensions)
- **Git**

### Platform Requirements for Development

- **Windows**: Windows 10 (1809+) or Windows 11.
- **Linux**: Any modern distribution with `glibc 2.27+` and `NetworkManager` (`nmcli`) installed if testing link detection locally.
- **macOS**: Supported for UI / protocol work (direct ethernet link management is in active development).

---

## Building, Running & Testing

### 1. Clone the Repository

```bash
git clone https://github.com/divyviradiya2/ethertransfer.git
cd ethertransfer
```

### 2. Build the Solution

```bash
dotnet build
```

### 3. Run the Desktop Application

```bash
dotnet run --project EtherTransfer.UI
```

### 4. Run the Test Suite

EtherTransfer has comprehensive unit and integration tests covering path sanitization, protocol framing, device discovery, link state monitoring, and transfer cancellations.

```bash
dotnet test
```

To run a specific test fixture or filter:

```bash
dotnet test --filter "FullyQualifiedName~PathSanitizerTests"
```

---

## Architecture & Solution Structure

EtherTransfer is structured into 6 focused projects:

```text
EtherTransfer/
 EtherTransfer.Core/        # Shared models, DTOs, network constants & settings manager
 EtherTransfer.Network/     # UDP discovery, TCP listener, interface detection & link state monitor
 EtherTransfer.Transfer/    # Streaming engine, framing serializer, ArrayPool buffers & PathSanitizer
 EtherTransfer.Services/    # High-level orchestration (DeviceService, TransferService, FirewallHelper)
 EtherTransfer.UI/          # Avalonia UI desktop client, views, dialogs, and assets
 EtherTransfer.Tests/       # NUnit unit & integration test suites
```

### Project Responsibilities

| Project | Responsibility | Key Classes |
| :--- | :--- | :--- |
| **`EtherTransfer.Core`** | Foundation types, protocol message definitions, app settings, and formatting helpers. | `TransferProtocol.cs`, `DiscoveryMessage.cs`, `SettingsManager.cs`, `NetworkConfig.cs` |
| **`EtherTransfer.Network`** | Low-level networking: UDP peer discovery, TCP server hosting, adapter classification, and platform link state detection. | `DiscoveryService.cs`, `TcpServer.cs`, `EthernetLinkMonitor.cs`, `WindowsNetworkInterfaceDetector.cs`, `LinuxNetworkInterfaceDetector.cs` |
| **`EtherTransfer.Transfer`** | File scanning, binary stream transmission, 4-byte length-prefix framing, path sanitization, and transfer cancellation. | `TransferSender.cs`, `TransferReceiver.cs`, `ProtocolHelper.cs`, `PathSanitizer.cs` |
| **`EtherTransfer.Services`** | Application service layer tying network discovery and transfer state machines to the UI. | `DeviceService.cs`, `TransferService.cs`, `FirewallHelper.cs` |
| **`EtherTransfer.UI`** | Cross-platform desktop interface built on Avalonia UI. | `MainWindow.axaml.cs`, `TransferDialog.axaml.cs`, `DebugWindow.axaml.cs`, `ScanDialog.axaml.cs` |
| **`EtherTransfer.Tests`** | Automated tests for protocol framing, path traversal security, link state transitions, and cancellation tokens. | `PathSanitizerTests.cs`, `ProtocolFramingTests.cs`, `TransferCancellationTests.cs` |

---

## How the Core Systems Work

### 1. Peer Discovery (UDP Port 50000)
- `DiscoveryService` broadcasts `HELLO` packets periodically across active Ethernet subnets using UDP broadcast (`255.255.255.255` and directed subnet broadcasts).
- Each running instance generates a random UUID `SessionId` at launch. Peers are indexed by `SessionId` rather than IP to gracefully handle dynamic link-local IP shifts without creating duplicate peers in the UI.
- When shutting down cleanly, `DiscoveryService` sends a burst of `BYE` messages so peers immediately drop the instance from their active list.

### 2. File Transfer Protocol (TCP Port 55000)
- All metadata messages are serialized as UTF-8 JSON preceded by a **4-byte little-endian length prefix**.
- Transfer sequence:
  1. Sender connects via TCP to receiver's port `55000`.
  2. Sender transmits `TRANSFER_REQUEST` (containing item count, total bytes, and root folder list).
  3. Receiver prompts the user. If accepted, receiver sends `TRANSFER_RESPONSE` with `Accepted: true`.
  4. For each file, sender emits `FILE_BEGIN` + `FileItemMetadata`, followed by raw binary chunks.
  5. Senders and receivers stream payloads in **1 MB chunks** using `ArrayPool<byte>.Shared` to avoid garbage collector pressure and maintain low RAM consumption (< 100 MB).
  6. Transmission finishes with `TRANSFER_END`.

### 3. Path Security & Sanitization
- Never write untrusted file paths directly to disk.
- All paths received over the network must pass through `PathSanitizer.SanitizeRelativePath()` to prevent directory traversal (`../`, absolute paths, root-relative paths).
- Windows reserved file names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`) are automatically prefixed (e.g. `_CON.txt`).
- `PathSanitizer.ResolveCollision()` safely renames files (`file (1).ext`) if a file already exists at the destination.

### 4. Link Monitoring & Auto-Configuration
- On Windows, APIPA assigns a link-local address (`169.254.x.x`) automatically.
- On Linux, `EthernetLinkMonitor` observes carrier changes via `nmcli` / `/sys/class/net` and automatically sets `ipv4.method link-local` on the connected interface, restoring prior settings on exit.

---

## Code Guidelines & Standards

When writing code for EtherTransfer, please keep the following principles in mind:

### 1. Robust Asynchronous Programming
- Use `async`/`await` consistently for I/O operations (network sockets, file streams, OS process execution).
- Always propagate and respect `CancellationToken`s throughout the call stack. When a user clicks "Cancel", active network streams and file handles must close immediately and cleanly.

### 2. No Silent Failures or Swallowed Exceptions
- Do not use empty `catch` blocks. If an error occurs (such as binding failure, disk full, or inaccessible file), either log it with context, bubble it up, or notify the user with a descriptive error message.
- If a catch block intentionally ignores an expected non-fatal condition, add a comment explaining why.

### 3. Memory & Resource Efficiency
- Use `ArrayPool<byte>.Shared` for chunk buffers instead of allocating `byte[]` repeatedly in loops.
- Wrap streams, sockets, and unmanaged resources in `using` statements or ensure deterministic disposal in `finally` blocks.

### 4. UI Responsiveness
- Never block the Avalonia UI dispatcher thread with synchronous file I/O or network socket calls.
- Dispatch UI updates from background threads safely using `Dispatcher.UIThread.Post(...)` or observable view model properties.

### 5. Cross-Platform Path Handling
- Always use `Path.Combine` or forward-slash normalized relative paths. Never hardcode backslashes (`\`) for file paths.

---

## Packaging & Publishing Releases

### Windows Single-File Builds

```bash
# Windows x64 (Self-contained, single-file)
dotnet publish EtherTransfer.UI -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/win-x64

# Windows x86 (32-bit compatibility)
dotnet publish EtherTransfer.UI -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -o publish/win-x86
```

### Linux Single-File Build

```bash
# Linux x64 (Self-contained, single-file)
dotnet publish EtherTransfer.UI -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o publish/linux-x64
```

### Building Windows Installers (Inno Setup)

Once the release binaries are published into `publish/win-x64` or `publish/win-x86`, compile the installer using Inno Setup:

```powershell
# 64-bit installer
iscc EtherTransfer.iss

# 32-bit installer
iscc EtherTransfer_x86.iss
```

The compiled setup executables will be output to `publish/installer/`.

---

## Submitting a Pull Request

1. **Open an Issue First** (for major features or architectural changes): Discuss your proposed changes beforehand so we can align on design and approach.
2. **Create a Feature Branch**:
   ```bash
   git checkout -b feature/your-feature-name
   ```
3. **Keep Changes Focused**: Small, targeted PRs are easier to review and merge than massive refactors.
4. **Add / Update Tests**: If you are fixing a bug or adding a feature in `EtherTransfer.Core`, `EtherTransfer.Network`, or `EtherTransfer.Transfer`, include corresponding unit tests in `EtherTransfer.Tests`.
5. **Run the Test Suite**: Ensure `dotnet test` passes with 0 failures before opening the PR.
6. **Write Clear Commit Messages**: Describe *what* changed and *why*.
