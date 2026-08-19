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
  echo -e "${YELLOW}Administrator permissions (sudo) are required to install EtherTransfer.${NC}\n"
  echo "We need this permission to:"
  echo "  1. Save the app to your system's application folder (/opt)"
  echo "  2. Configure your firewall to allow direct file transfers"
  echo "  3. Add the app shortcut to your application menu"
  echo "  4. Set up the 'ethertransfer' terminal command"
  echo ""
  echo -e "Please run the installer again using: ${CYAN}sudo bash install_linux.sh${NC}"
  exit 1
fi

# 2. Confirmation prompt
echo -ne "\n${YELLOW}Do you want to proceed with the installation of EtherTransfer? [y/N]: ${NC}"
read -r CONFIRM
if [[ ! "$CONFIRM" =~ ^[Yy]$ ]]; then
    echo -e "\n${RED}Installation cancelled by user.${NC}\n"
    exit 0
fi
echo ""

# 3. Architecture check
print_step "Checking system architecture..."
ARCH=$(uname -m)
if [ "$ARCH" = "x86_64" ]; then
    ET_ARCH="x64"
    print_success "Architecture x86_64 supported"
else
    print_error "Unsupported architecture: $ARCH. Only x86_64 is supported."
    exit 1
fi

INSTALL_DIR="/opt/ethertransfer"

# 4. Check existing installation
SKIP_DOWNLOAD=false
if [ -d "$INSTALL_DIR" ] && [ -f "$INSTALL_DIR/EtherTransfer" ]; then
    echo -ne "\n${YELLOW}EtherTransfer is already installed. Do you want to update/reinstall the app files? [y/N]: ${NC}"
    read -r UPDATE_CONFIRM
    if [[ "$UPDATE_CONFIRM" =~ ^[Yy]$ ]]; then
        SKIP_DOWNLOAD=false
        echo ""
    else
        print_success "EtherTransfer app files update skipped (Skipped)"
        SKIP_DOWNLOAD=true
    fi
fi

if [ "$SKIP_DOWNLOAD" = false ]; then
    # 5. Download
    TMP_DIR=$(mktemp -d)
    DOWNLOAD_URL="https://github.com/divyviradiya2/ethertransfer/releases/latest/download/EtherTransfer-linux-${ET_ARCH}.zip"

    print_step "Downloading EtherTransfer..."
    MAX_RETRIES=3
    RETRY_COUNT=0
    DOWNLOAD_SUCCESS=false

    while [ $RETRY_COUNT -lt $MAX_RETRIES ]; do
        if command -v curl > /dev/null; then
            curl -L --progress-bar --connect-timeout 15 "$DOWNLOAD_URL" -o "$TMP_DIR/ethertransfer.zip"
        elif command -v wget > /dev/null; then
            wget -q --show-progress --timeout=15 --tries=1 "$DOWNLOAD_URL" -O "$TMP_DIR/ethertransfer.zip"
        else
            print_error "Neither curl nor wget is installed. Cannot download."
            rm -rf "$TMP_DIR"
            exit 1
        fi

        if [ -s "$TMP_DIR/ethertransfer.zip" ]; then
            DOWNLOAD_SUCCESS=true
            print_success "Download complete"
            break
        else
            RETRY_COUNT=$((RETRY_COUNT+1))
            print_warning "Download failed. Retrying ($RETRY_COUNT/$MAX_RETRIES)..."
            sleep 2
        fi
    done

    if [ "$DOWNLOAD_SUCCESS" = false ]; then
        print_error "Failed to download EtherTransfer after multiple attempts."
        rm -rf "$TMP_DIR"
        exit 1
    fi

    # 6. Extract
    print_step "Extracting files to $INSTALL_DIR..."
    mkdir -p "$INSTALL_DIR"

    if command -v unzip > /dev/null; then
        unzip -o -q "$TMP_DIR/ethertransfer.zip" -d "$INSTALL_DIR"
    else
        print_error "unzip is not installed. Please install unzip and try again."
        rm -rf "$TMP_DIR"
        exit 1
    fi
    chmod +x "$INSTALL_DIR/EtherTransfer"
    print_success "Files extracted successfully"
    rm -rf "$TMP_DIR"
fi

# 5. Firewall
print_step "Configuring firewall..."
if command -v ufw > /dev/null; then
    if ufw status | grep -q "8840" || ufw show added 2>/dev/null | grep -q "8840"; then
        print_success "UFW rules already exist (Skipped)"
    else
        ufw allow 8840/tcp > /dev/null 2>&1
        ufw allow 8840/udp > /dev/null 2>&1
        print_success "UFW rules added for port 8840"
    fi
elif command -v firewall-cmd > /dev/null; then
    if firewall-cmd --query-port=8840/tcp > /dev/null 2>&1; then
        print_success "Firewalld rules already exist (Skipped)"
    else
        firewall-cmd --permanent --add-port=8840/tcp > /dev/null 2>&1
        firewall-cmd --permanent --add-port=8840/udp > /dev/null 2>&1
        firewall-cmd --reload > /dev/null 2>&1
        print_success "Firewalld rules added for port 8840"
    fi
elif command -v iptables > /dev/null; then
    if iptables -C INPUT -p tcp --dport 8840 -j ACCEPT >/dev/null 2>&1; then
        print_success "iptables rules already exist (Skipped)"
    else
        iptables -A INPUT -p tcp --dport 8840 -j ACCEPT
        iptables -A INPUT -p udp --dport 8840 -j ACCEPT
        print_success "iptables rules added for port 8840"
    fi
else
    print_warning "No known firewall detected. Skipping firewall config"
fi

# 6. NetworkManager
print_step "Checking NetworkManager..."
if ! command -v nmcli > /dev/null; then
    print_warning "NetworkManager not found. Installing..."
    if command -v apt-get > /dev/null; then
        apt-get update -qq && apt-get install -y -qq network-manager
    elif command -v dnf > /dev/null; then
        dnf install -y -q NetworkManager
    elif command -v pacman > /dev/null; then
        pacman -S --noconfirm --quiet networkmanager
    elif command -v zypper > /dev/null; then
        zypper install -y --quiet NetworkManager
    elif command -v yum > /dev/null; then
        yum install -y -q NetworkManager
    else
        print_error "Could not install NetworkManager automatically"
    fi
    
    if command -v systemctl > /dev/null; then
        systemctl enable NetworkManager > /dev/null 2>&1
        systemctl start NetworkManager > /dev/null 2>&1
    fi
    print_success "NetworkManager installed and started"
else
    print_success "NetworkManager is already installed (Skipped)"
fi

# 7. Desktop Integration
print_step "Setting up desktop integration..."
ICON_URL="https://raw.githubusercontent.com/divyviradiya2/ethertransfer/master/EtherTransfer.UI/Assets/logo.ico"
ICON_DIR="/usr/share/pixmaps"
DESKTOP_FILE="/usr/share/applications/ethertransfer.desktop"
SYMLINK="/usr/local/bin/ethertransfer"

if [ -f "$ICON_DIR/ethertransfer.ico" ]; then
    print_success "Icon already exists (Skipped)"
else
    if command -v curl > /dev/null; then
        curl -sSL "$ICON_URL" -o "$ICON_DIR/ethertransfer.ico"
    elif command -v wget > /dev/null; then
        wget -q "$ICON_URL" -O "$ICON_DIR/ethertransfer.ico"
    fi
    print_success "Icon downloaded"
fi

if [ -f "$DESKTOP_FILE" ] && grep -q "Exec=$INSTALL_DIR/EtherTransfer" "$DESKTOP_FILE"; then
    print_success "Desktop shortcut already exists (Skipped)"
else
    cat << EOF > "$DESKTOP_FILE"
[Desktop Entry]
Name=EtherTransfer
Comment=Local file transfer app by DS Labs
Exec=$INSTALL_DIR/EtherTransfer
Icon=$ICON_DIR/ethertransfer.ico
Terminal=false
Type=Application
Categories=Network;FileTransfer;Utility;
EOF
    chmod 644 "$DESKTOP_FILE"
    print_success "Desktop shortcut created"
fi

if [ -L "$SYMLINK" ] && [ "$(readlink "$SYMLINK")" = "$INSTALL_DIR/EtherTransfer" ]; then
    print_success "Terminal command already exists (Skipped)"
else
    ln -sf "$INSTALL_DIR/EtherTransfer" "$SYMLINK"
    print_success "Terminal command created/fixed"
fi

# Cleanup
rm -rf "$TMP_DIR"

echo ""
echo -e "${GREEN}${BOLD}=== Installation Complete ===${NC}"
echo -e "You can launch EtherTransfer from your app menu or run ${CYAN}ethertransfer${NC} in your terminal."
echo ""
