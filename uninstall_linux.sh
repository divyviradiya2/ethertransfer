#!/bin/bash

# Colors and formatting
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

# Progress bar characters
BAR_FILL="━"
BAR_EMPTY="─"
SPINNER_FRAMES=("⠋" "⠙" "⠹" "⠸" "⠼" "⠴" "⠦" "⠧" "⠇" "⠏")

TOTAL_STEPS=3

# ── Helper Functions ───────────────────────────────────────────

print_step() {
    echo -ne "\r\033[K${BLUE}[*]${NC} $1"
}

print_success() {
    echo -e "\r\033[K${GREEN}[✔]${NC} $1"
}

print_error() {
    echo -e "\r\033[K${RED}[✖]${NC} $1"
}

print_warning() {
    echo -e "\r\033[K${YELLOW}[!]${NC} $1"
}

# Draw step-based progress bar
# Usage: draw_step_progress <current_step> <total_steps> <step_label>
draw_step_progress() {
    local current=$1
    local total=$2
    local label=$3

    local bar_width=30
    local filled=$((current * bar_width / total))
    local empty=$((bar_width - filled))
    local percent=$((current * 100 / total))

    # Build the bar
    local bar=""
    if [ "$filled" -ge "$bar_width" ]; then
        bar=$(printf "${BAR_FILL}%.0s" $(seq 1 $bar_width))
    elif [ "$filled" -gt 0 ]; then
        bar=$(printf "${BAR_FILL}%.0s" $(seq 1 $filled))
        bar="${bar}$(printf "${BAR_EMPTY}%.0s" $(seq 1 $empty))"
    else
        bar=$(printf "${BAR_EMPTY}%.0s" $(seq 1 $bar_width))
    fi

    # Color based on progress
    local bar_color="${BLUE}"
    if [ "$percent" -ge 100 ]; then
        bar_color="${GREEN}"
    elif [ "$percent" -ge 66 ]; then
        bar_color="${CYAN}"
    fi

    echo -e "\n  ${GRAY}${DIM}Progress${NC}  ${bar_color}${bar}${NC}  ${BOLD}${percent}%%${NC}  ${DIM}(${current}/${total}) ${label}${NC}\n"
}

# Animated spinner
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

# ── Start ──────────────────────────────────────────────────────

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

# Check if EtherTransfer is actually installed
if [ ! -d "$INSTALL_DIR" ] && [ ! -f "$DESKTOP_FILE" ] && [ ! -L "$SYMLINK" ]; then
    print_warning "EtherTransfer does not appear to be installed on this system."
    echo ""
    exit 0
fi

# Confirmation prompt
echo -ne "${YELLOW}Are you sure you want to uninstall EtherTransfer? [y/N]: ${NC}"
read -r CONFIRM
if [[ ! "$CONFIRM" =~ ^[Yy]$ ]]; then
    echo -e "\n${RED}Uninstallation cancelled by user.${NC}\n"
    exit 0
fi
echo ""

draw_step_progress 0 $TOTAL_STEPS "Starting..."

# ── Step 1: Remove Firewall Rules ──────────────────────────────
print_step "Removing firewall rules..."
start_spinner "Cleaning up firewall rules..."
sleep 0.3

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
    print_warning "No known firewall detected. Skipping firewall clean up"
fi

draw_step_progress 1 $TOTAL_STEPS "Firewall cleaned"

# ── Step 2: Remove Desktop Integration ─────────────────────────
print_step "Removing desktop integration..."
start_spinner "Removing shortcuts and icon..."
sleep 0.3

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
    update-desktop-database /usr/share/applications > /dev/null 2>&1
fi

stop_spinner
print_success "Desktop shortcuts, icon, and terminal command removed"

draw_step_progress 2 $TOTAL_STEPS "Desktop cleaned"

# ── Step 3: Remove Application Files ──────────────────────────
print_step "Removing application files..."
if [ -d "$INSTALL_DIR" ]; then
    start_spinner "Removing $INSTALL_DIR..."
    rm -rf "$INSTALL_DIR"
    sleep 0.3
    stop_spinner
    print_success "Removed $INSTALL_DIR directory"
else
    print_warning "Application directory $INSTALL_DIR not found"
fi

draw_step_progress 3 $TOTAL_STEPS "All done"

echo -e "${GREEN}${BOLD}=== Uninstallation Complete ===${NC}"
echo "EtherTransfer has been completely removed from your system."
echo ""
