#!/bin/bash

# =========================================
# EtherTransfer Universal Linux Uninstaller
# =========================================

# 1. Require sudo privileges
if [ "$EUID" -ne 0 ]; then
  echo "Please run this uninstaller as root or with sudo:"
  echo "sudo $0"
  exit 1
fi

echo "========================================="
echo "  EtherTransfer Enterprise Uninstaller   "
echo "========================================="

INSTALL_DIR="/opt/ethertransfer"
DESKTOP_FILE="/usr/share/applications/ethertransfer.desktop"
ICON_DIR="/usr/share/pixmaps"
SYMLINK="/usr/local/bin/ethertransfer"

# 1. Remove Firewall Rules
echo "[+] Removing Firewall Rules..."
if command -v ufw > /dev/null; then
    echo "    -> UFW (Ubuntu/Debian) detected. Removing TCP/UDP 8840 rules..."
    ufw delete allow 8840/tcp > /dev/null 2>&1
    ufw delete allow 8840/udp > /dev/null 2>&1
elif command -v firewall-cmd > /dev/null; then
    echo "    -> Firewalld (Fedora/RHEL/CentOS) detected. Removing TCP/UDP 8840 rules..."
    firewall-cmd --permanent --remove-port=8840/tcp > /dev/null 2>&1
    firewall-cmd --permanent --remove-port=8840/udp > /dev/null 2>&1
    firewall-cmd --reload > /dev/null 2>&1
elif command -v iptables > /dev/null; then
    echo "    -> iptables detected. Removing TCP/UDP 8840 rules..."
    iptables -D INPUT -p tcp --dport 8840 -j ACCEPT > /dev/null 2>&1
    iptables -D INPUT -p udp --dport 8840 -j ACCEPT > /dev/null 2>&1
else
    echo "    -> No supported firewall running. Skipping firewall config."
fi

# 2. Unregister Desktop Application
echo "[+] Removing Desktop Application..."
if [ -f "$DESKTOP_FILE" ]; then
    rm -f "$DESKTOP_FILE"
    echo "    -> Removed desktop launcher."
fi

if [ -f "$ICON_DIR/ethertransfer.ico" ]; then
    rm -f "$ICON_DIR/ethertransfer.ico"
    echo "    -> Removed icon."
fi

if [ -L "$SYMLINK" ]; then
    rm -f "$SYMLINK"
    echo "    -> Removed terminal command symlink."
fi

# Update the desktop database so the icon disappears immediately
if command -v update-desktop-database > /dev/null 2>&1; then
    update-desktop-database /usr/share/applications
fi

# 3. Remove Application Files
echo "[+] Removing Application Files..."
if [ -d "$INSTALL_DIR" ]; then
    rm -rf "$INSTALL_DIR"
    echo "    -> Removed $INSTALL_DIR directory."
else
    echo "    -> Application directory $INSTALL_DIR not found."
fi

echo "========================================="
echo " [+] EtherTransfer Uninstallation Complete!"
echo "========================================="
