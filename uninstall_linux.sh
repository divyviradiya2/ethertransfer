#!/bin/bash

# Colors and formatting
RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
NC='\033[0m'
BOLD='\033[1m'

# Clear screen for TUI-like feel
clear

# EtherTransfer Logo
echo -e "${CYAN}${BOLD}"
echo "███████╗████████╗██╗  ██╗███████╗██████╗ ████████╗██████╗ █████╗ ███╗   ██╗███████╗███████╗███████╗██████╗"
echo "██╔════╝╚══██╔══╝██║  ██║██╔════╝██╔══██╗╚══██╔══╝██╔══██╗██╔══██╗████╗  ██║██╔════╝██╔════╝██╔════╝██╔══██╗"
echo "█████╗     ██║   ███████║█████╗  ██████╔╝   ██║   ██████╔╝███████║██╔██╗ ██║███████╗█████╗  █████╗  ██████╔╝"
echo "██╔══╝     ██║   ██╔══██║██╔══╝  ██╔══██╗   ██║   ██╔══██╗██╔══██║██║╚██╗██║╚════██║██╔══╝  ██╔══╝  ██╔══██╗"
echo "███████╗   ██║   ██║  ██║███████╗██║  ██║   ██║   ██║  ██║██║  ██║██║ ╚████║███████║██║     ███████╗██║  ██║"
echo "╚══════╝   ╚═╝   ╚═╝  ╚═╝╚══════╝╚═╝  ╚═╝   ╚═╝   ╚═╝  ╚═╝╚═╝  ╚═╝╚═╝  ╚═══╝╚══════╝╚═╝     ╚══════╝╚═╝  ╚═╝"
echo -e "${NC}"
echo -e "${BLUE}By DS Labs${NC}\n"
echo -e "${YELLOW}Uninstaller${NC}\n"

# Helpers
print_step() {
    echo -ne "\r\033[K${BLUE}[*]${NC} $1"
}

print_success() {
    echo -e "\r\033[K${GREEN}[✔]${NC} $1"
}

print_error() {
    echo -e "\r\033[K${RED}[x]${NC} $1"
}

print_warning() {
    echo -e "\r\033[K${YELLOW}[!]${NC} $1"
}

# 1. Permission check
if [ "$EUID" -ne 0 ]; then
  echo -e "${YELLOW}Administrator permissions (sudo) are required to uninstall EtherTransfer.${NC}\n"
  echo "We need this permission to:"
  echo "  1. Remove the app from your system's application folder (/opt)"
  echo "  2. Remove the firewall rules created during installation"
  echo "  3. Remove the app shortcut from your application menu"
  echo "  4. Remove the 'ethertransfer' terminal command"
  echo ""
  echo -e "Please run the uninstaller again using: ${CYAN}sudo bash uninstall_linux.sh${NC}"
  exit 1
fi

INSTALL_DIR="/opt/ethertransfer"
DESKTOP_FILE="/usr/share/applications/ethertransfer.desktop"
ICON_DIR="/usr/share/pixmaps"
SYMLINK="/usr/local/bin/ethertransfer"

# 1. Remove Firewall Rules
print_step "Removing firewall rules..."
if command -v ufw > /dev/null; then
    ufw delete allow 8840/tcp > /dev/null 2>&1
    ufw delete allow 8840/udp > /dev/null 2>&1
    print_success "UFW rules removed for port 8840"
elif command -v firewall-cmd > /dev/null; then
    firewall-cmd --permanent --remove-port=8840/tcp > /dev/null 2>&1
    firewall-cmd --permanent --remove-port=8840/udp > /dev/null 2>&1
    firewall-cmd --reload > /dev/null 2>&1
    print_success "Firewalld rules removed for port 8840"
elif command -v iptables > /dev/null; then
    iptables -D INPUT -p tcp --dport 8840 -j ACCEPT > /dev/null 2>&1
    iptables -D INPUT -p udp --dport 8840 -j ACCEPT > /dev/null 2>&1
    print_success "iptables rules removed for port 8840"
else
    print_warning "No known firewall detected. Skipping firewall clean up"
fi

# 2. Unregister Desktop Application
print_step "Removing desktop integration..."
if [ -f "$DESKTOP_FILE" ]; then
    rm -f "$DESKTOP_FILE"
fi

if [ -f "$ICON_DIR/ethertransfer.ico" ]; then
    rm -f "$ICON_DIR/ethertransfer.ico"
fi

if [ -L "$SYMLINK" ]; then
    rm -f "$SYMLINK"
fi

if command -v update-desktop-database > /dev/null 2>&1; then
    update-desktop-database /usr/share/applications
fi
print_success "Desktop shortcuts removed"

# 3. Remove Application Files
print_step "Removing application files..."
if [ -d "$INSTALL_DIR" ]; then
    rm -rf "$INSTALL_DIR"
    print_success "Removed $INSTALL_DIR directory"
else
    print_warning "Application directory $INSTALL_DIR not found"
fi

echo ""
echo -e "${GREEN}${BOLD}=== Uninstallation Complete ===${NC}"
echo "EtherTransfer has been completely removed from your system."
echo ""
