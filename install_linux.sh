#!/bin/bash

# =========================================
# EtherTransfer Universal Linux Installer
# =========================================

# 1. Require sudo privileges (prompts for password if run normally)
if [ "$EUID" -ne 0 ]; then
  echo "Please run this installer as root or with sudo:"
  echo "sudo $0"
  exit 1
fi

echo "=============================="
echo "  EtherTransfer Installer  "
echo "=============================="

# 1. Detect System Architecture
ARCH=$(uname -m)
if [ "$ARCH" = "x86_64" ]; then
    ET_ARCH="x64"
else
    echo "[-] Unsupported architecture: $ARCH. Only x86_64 (64-bit) is supported."
    exit 1
fi
echo "[+] Detected Architecture: $ARCH ($ET_ARCH)"

# 2. Download from GitHub Releases
# Note: Ensure you upload your zip files to your GitHub Releases so this URL works!
DOWNLOAD_URL="https://github.com/divyviradiya2/ethertransfer/releases/latest/download/EtherTransfer-linux-${ET_ARCH}.zip"

echo "[+] Downloading EtherTransfer..."
TMP_DIR=$(mktemp -d)
wget -q --show-progress "$DOWNLOAD_URL" -O "$TMP_DIR/ethertransfer.zip"

if [ ! -s "$TMP_DIR/ethertransfer.zip" ]; then
    echo "[-] Failed to download EtherTransfer."
    echo "[-] Please ensure you have created a GitHub Release and uploaded the .zip artifacts!"
    rm -rf "$TMP_DIR"
    exit 1
fi

# Extract to standard Linux /opt directory
INSTALL_DIR="/opt/ethertransfer"

if [ -d "$INSTALL_DIR" ] && [ -f "$INSTALL_DIR/EtherTransfer" ]; then
    echo "[+] Existing installation found. Updating app files and verifying configuration..."
else
    echo "[+] Extracting files to $INSTALL_DIR..."
    mkdir -p "$INSTALL_DIR"
fi

unzip -o -q "$TMP_DIR/ethertransfer.zip" -d "$INSTALL_DIR"
chmod +x "$INSTALL_DIR/EtherTransfer"

# 3. Dynamic Universal Firewall Configuration
echo "[+] Configuring Firewall..."
if command -v ufw > /dev/null; then
    echo "    -> UFW (Ubuntu/Debian) detected. Allowing TCP/UDP 8840..."
    ufw allow 8840/tcp comment 'EtherTransfer TCP'
    ufw allow 8840/udp comment 'EtherTransfer UDP'
elif command -v firewall-cmd > /dev/null; then
    echo "    -> Firewalld (Fedora/RHEL/CentOS) detected. Allowing TCP/UDP 8840..."
    firewall-cmd --permanent --add-port=8840/tcp
    firewall-cmd --permanent --add-port=8840/udp
    firewall-cmd --reload
elif command -v iptables > /dev/null; then
    echo "    -> iptables detected. Allowing TCP/UDP 8840..."
    iptables -A INPUT -p tcp --dport 8840 -j ACCEPT
    iptables -A INPUT -p udp --dport 8840 -j ACCEPT
else
    echo "    -> No supported firewall running. Skipping firewall config."
fi

# 4. Intelligent NetworkManager (nmcli) Detection & Installation
echo "[+] Checking for NetworkManager (nmcli)..."
if ! command -v nmcli > /dev/null; then
    echo "    -> nmcli not found. Automatically installing NetworkManager..."
    
    # Detect the distro's package manager and install accordingly
    if command -v apt-get > /dev/null; then
        apt-get update && apt-get install -y network-manager
    elif command -v dnf > /dev/null; then
        dnf install -y NetworkManager
    elif command -v pacman > /dev/null; then
        pacman -S --noconfirm networkmanager
    elif command -v zypper > /dev/null; then
        zypper install -y NetworkManager
    elif command -v yum > /dev/null; then
        yum install -y NetworkManager
    else
        echo "    [-] Could not determine package manager. Please install NetworkManager manually."
    fi
    
    # Ensure the service is enabled and started
    if command -v systemctl > /dev/null; then
        systemctl enable NetworkManager
        systemctl start NetworkManager
    fi
else
    echo "    -> nmcli is already installed."
fi

# 5. Desktop Application Registration
echo "[+] Registering Desktop Application..."
# Download the raw icon directly from the GitHub repository
ICON_URL="https://raw.githubusercontent.com/divyviradiya2/ethertransfer/master/EtherTransfer.UI/Assets/logo.ico"
ICON_DIR="/usr/share/pixmaps"
wget -q "$ICON_URL" -O "$ICON_DIR/ethertransfer.ico"

# Create the standard Linux .desktop launcher file
DESKTOP_FILE="/usr/share/applications/ethertransfer.desktop"
cat << EOF > "$DESKTOP_FILE"
[Desktop Entry]
Name=EtherTransfer
Comment=Enterprise-grade local file transfer
Exec=$INSTALL_DIR/EtherTransfer
Icon=$ICON_DIR/ethertransfer.ico
Terminal=false
Type=Application
Categories=Network;FileTransfer;Utility;
EOF
chmod 644 "$DESKTOP_FILE"

# Create a symlink so users can run it from the terminal via 'ethertransfer'
ln -sf "$INSTALL_DIR/EtherTransfer" "/usr/local/bin/ethertransfer"

# Cleanup Temp Files
rm -rf "$TMP_DIR"

echo "========================================="
echo " [+] EtherTransfer Installation Complete!"
echo " [+] You can launch it from your App Menu or by typing 'ethertransfer' in terminal."
echo "========================================="
