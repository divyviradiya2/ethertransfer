# System Modifications & Behind the Scenes

This document outlines everything EtherTransfer does to a user's operating system during installation and execution. We believe in complete transparency so administrators and developers know exactly what network bindings, firewall rules, and system services are modified.

---

## 1. Windows Systems

### Installation (`EtherTransfer_Setup.exe`)
When a user runs the Inno Setup installer, the following changes are made:
- **Filesystem**: 
  - Extracts application binaries to `C:\Program Files\EtherTransfer` (or `C:\Program Files (x86)\EtherTransfer` for 32-bit).
  - Creates shortcuts in the Start Menu and on the Desktop.
- **Windows Defender Firewall**:
  - The installer runs a hidden `netsh` command to automatically whitelist the application.
  - `netsh advfirewall firewall add rule name="EtherTransfer" dir=in action=allow program="{app}\EtherTransfer.exe" enable=yes profile=private,public`
  - This ensures peer discovery (UDP) and direct transfers (TCP) work seamlessly without Windows blocking the connection.
- **Uninstallation**: 
  - The uninstaller cleanly removes all application files, shortcuts, and silently executes `netsh advfirewall firewall delete rule` to remove the firewall whitelist entry.

### Runtime Behavior
- **Network Sockets**: Opens UDP broadcast sockets for discovery and binds TCP listener sockets when a transfer is initiated.
- **TCP Keep-Alives**: Modifies the underlying socket options via Windows API to enable OS-level TCP Keep-Alives (used to detect physically disconnected ethernet cables during idle UI prompts).

---

## 2. Linux Systems

### Installation (`install_linux.sh`)
The universal Linux installation script requires `sudo` (root) privileges and performs extensive environment preparation:

- **Filesystem**:
  - Downloads and extracts the application payload directly to `/opt/ethertransfer`.
  - Downloads the application icon to `/usr/share/pixmaps/ethertransfer.ico`.
  - Creates a standard desktop launcher at `/usr/share/applications/ethertransfer.desktop` so it appears in application menus (GNOME, KDE, etc.).
  - Creates a symbolic link at `/usr/local/bin/ethertransfer` allowing the app to be launched directly from any terminal.

- **Dynamic Firewall Configuration**:
  The script detects the active firewall manager and opens **Port 50000 (UDP Discovery)** and **Port 55000 (TCP File Transfer)**, which are required for peer discovery and high-speed direct transfers.
  - If **UFW** (Ubuntu/Debian) is detected: Runs `ufw allow 50000/udp` and `ufw allow 55000/tcp`.
  - If **Firewalld** (Fedora/RHEL/CentOS) is detected: Runs `firewall-cmd --permanent --add-port=50000/udp` and `--add-port=55000/tcp`.
  - If **iptables** is detected: Appends ACCEPT rules directly to the INPUT chain.

- **Dependency Injection (NetworkManager)**:
  EtherTransfer relies on `nmcli` for low-level network interface monitoring. The script checks for it, and if missing, it detects the system's package manager (`apt`, `dnf`, `pacman`, `zypper`, or `yum`) to automatically download and install the `network-manager` package. It then uses `systemctl` to enable and start the service.

### Runtime Behavior
- **Socket Binding**: Binds to `0.0.0.0:50000` for UDP peer discovery broadcasts and `0.0.0.0:55000` for incoming TCP transfer streams.
- **Path Sanitization**: When saving received files, EtherTransfer heavily sanitizes paths to prevent malicious directory traversal attacks against Linux filesystems (e.g., stripping `../` attempts to prevent overwriting `/etc/` or `/usr/` files).

---

## 3. Storage & Artifacts (All Platforms)

- **Temporary Buffers**: During active transfers, EtherTransfer streams directly from disk to network and network to disk. It uses strict 1MB memory buffers per chunk to prevent RAM bloat, meaning system RAM usage remains stable regardless of file size.
- **No Telemetry**: EtherTransfer does not install any background tracking services, telemetry agents, or startup analytics hooks. It only runs when explicitly launched by the user.
