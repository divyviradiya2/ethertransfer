#!/bin/bash

RED='\033[0;31m'
GREEN='\033[0;32m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
MAGENTA='\033[0;35m'
NC='\033[0m'
BOLD='\033[1m'
DIM='\033[2m'
GRAY='\033[0;90m'

SPINNER_FRAMES=("⠋" "⠙" "⠹" "⠸" "⠼" "⠴" "⠦" "⠧" "⠇" "⠏")

print_success() {
    echo -e "\r\033[K${GREEN}[✔]${NC} $1"
}

print_error() {
    echo -e "\r\033[K${RED}[✖]${NC} $1"
}

print_warning() {
    echo -e "\r\033[K${YELLOW}[!]${NC} $1"
}

SPINNER_PID=""
start_spinner() {
    local msg="$1"
    (
        local i=0
        while true; do
            printf "\r\033[K    ${MAGENTA}%s${NC} ${DIM}%s${NC}" "${SPINNER_FRAMES[$i]}" "$msg"
            i=$(( (i + 1) % ${#SPINNER_FRAMES[@]} ))
            sleep 0.1
        done
    ) &
    SPINNER_PID=$!
    disown "$SPINNER_PID" 2>/dev/null
}

stop_spinner() {
    if [ -n "$SPINNER_PID" ]; then
        kill "$SPINNER_PID" 2>/dev/null
        wait "$SPINNER_PID" 2>/dev/null
        SPINNER_PID=""
    fi
    printf "\r\033[K"
}

clear

echo -e "${CYAN}${BOLD}"
echo "███████╗████████╗██╗  ██╗███████╗██████╗ ████████╗██████╗ █████╗ ███╗   ██╗███████╗███████╗███████╗██████╗"
echo "██╔════╝╚══██╔══╝██║  ██║██╔════╝██╔══██╗╚══██╔══╝██╔══██╗██╔══██╗████╗  ██║██╔════╝██╔════╝██╔════╝██╔══██╗"
echo "█████╗     ██║   ███████║█████╗  ██████╔╝   ██║   ██████╔╝███████║██╔██╗ ██║███████╗█████╗  █████╗  ██████╔╝"
echo "██╔══╝     ██║   ██╔══██║██╔══╝  ██╔══██╗   ██║   ██╔══██╗██╔══██║██║╚██╗██║╚════██║██╔══╝  ██╔══╝  ██╔══██╗"
echo "███████╗   ██║   ██║  ██║███████╗██║  ██║   ██║   ██║  ██║██║  ██║██║ ╚████║███████║██║     ███████╗██║  ██║"
echo "╚══════╝   ╚═╝   ╚═╝  ╚═╝╚══════╝╚═╝  ╚═╝   ╚═╝   ╚═╝  ╚═╝╚═╝  ╚═╝╚═╝  ╚═══╝╚══════╝╚═╝     ╚══════╝╚═╝  ╚═╝"
echo -e "${NC}"
echo -e "${BLUE}By DS Labs${NC} ${GRAY}•${NC} ${DIM}Open source under MIT${NC}\n"
echo -e "${YELLOW}Uninstaller${NC}\n"

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

if [ ! -d "$INSTALL_DIR" ] && [ ! -f "$DESKTOP_FILE" ] && [ ! -L "$SYMLINK" ]; then
    print_warning "EtherTransfer does not appear to be installed on this system."
    echo ""
    exit 0
fi

echo -ne "${YELLOW}Are you sure you want to uninstall EtherTransfer? [y/N]: ${NC}"
read -r CONFIRM < /dev/tty
if [[ ! "$CONFIRM" =~ ^[Yy]$ ]]; then
    echo -e "\n${RED}Uninstallation cancelled by user.${NC}\n"
    exit 0
fi
echo ""

start_spinner "Removing firewall rules..."

if command -v ufw > /dev/null; then
    ufw delete allow 8840/tcp > /dev/null 2>&1
    ufw delete allow 8840/udp > /dev/null 2>&1
    stop_spinner
    print_success "UFW rules removed for port 8840"
elif command -v firewall-cmd > /dev/null; then
    firewall-cmd --permanent --remove-port=8840/tcp > /dev/null 2>&1
    firewall-cmd --permanent --remove-port=8840/udp > /dev/null 2>&1
    firewall-cmd --reload > /dev/null 2>&1
    stop_spinner
    print_success "Firewalld rules removed for port 8840"
elif command -v iptables > /dev/null; then
    iptables -D INPUT -p tcp --dport 8840 -j ACCEPT > /dev/null 2>&1
    iptables -D INPUT -p udp --dport 8840 -j ACCEPT > /dev/null 2>&1
    stop_spinner
    print_success "iptables rules removed for port 8840"
else
    stop_spinner
    print_warning "No firewall detected, skipped"
fi

start_spinner "Removing desktop integration..."

[ -f "$DESKTOP_FILE" ] && rm -f "$DESKTOP_FILE"
[ -f "$ICON_DIR/ethertransfer.ico" ] && rm -f "$ICON_DIR/ethertransfer.ico"
[ -L "$SYMLINK" ] && rm -f "$SYMLINK"
command -v update-desktop-database > /dev/null 2>&1 && update-desktop-database /usr/share/applications > /dev/null 2>&1

stop_spinner
print_success "Desktop shortcut, icon, and terminal command removed"

if [ -d "$INSTALL_DIR" ]; then
    start_spinner "Removing $INSTALL_DIR..."
    rm -rf "$INSTALL_DIR"
    stop_spinner
    print_success "Removed $INSTALL_DIR"
else
    print_warning "$INSTALL_DIR not found, skipped"
fi

echo ""
echo -e "${GREEN}${BOLD}Uninstallation Complete${NC}"
echo "EtherTransfer has been completely removed from your system."
echo ""
