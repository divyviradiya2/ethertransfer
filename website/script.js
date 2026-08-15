document.addEventListener('DOMContentLoaded', () => {
    // OS Detection for the primary download button
    const primaryBtn = document.getElementById('primary-download-btn');
    
    let userOS = "Unknown OS";
    let is64Bit = false;

    // Advanced OS string detection
    const ua = navigator.userAgent;
    if (ua.indexOf("Win") !== -1) {
        userOS = "Windows";
        if (ua.indexOf("WOW64") !== -1 || ua.indexOf("Win64") !== -1 || ua.indexOf("x64") !== -1) {
            is64Bit = true;
        }
    } else if (ua.indexOf("Linux") !== -1 || ua.indexOf("X11") !== -1) {
        userOS = "Linux";
    } else if (ua.indexOf("Mac") !== -1) {
        userOS = "MacOS";
    }

    // Update UI based on detected OS
    if (primaryBtn) {
        if (userOS === "Windows") {
            if (is64Bit) {
                primaryBtn.textContent = "Download for Windows (64-bit)";
                primaryBtn.href = "https://github.com/divyviradiya2/ethertransfer/releases/latest/download/EtherTransfer_Setup_x64.exe";
            } else {
                primaryBtn.textContent = "Download for Windows (32-bit)";
                primaryBtn.href = "https://github.com/divyviradiya2/ethertransfer/releases/latest/download/EtherTransfer_Setup_x86.exe";
            }
        } else if (userOS === "Linux") {
            primaryBtn.textContent = "Install for Linux";
            primaryBtn.href = "#"; // Prevent navigation
            primaryBtn.addEventListener('click', (e) => {
                e.preventDefault();
                const linuxModal = document.getElementById('linuxInstallModal');
                if (linuxModal) {
                    linuxModal.showModal();
                    document.body.style.overflow = 'hidden';
                }
            });
        } else {
            primaryBtn.textContent = "Download Release";
        }
    }

    // Theme Toggle Logic
    const themeToggleBtn = document.getElementById('theme-toggle');
    
    // Check for saved theme preference or use OS preference
    const savedTheme = localStorage.getItem('theme');
    const prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    
    if (savedTheme === 'dark' || (!savedTheme && prefersDark)) {
        document.documentElement.setAttribute('data-theme', 'dark');
    } else {
        document.documentElement.setAttribute('data-theme', 'light');
    }

    // Toggle theme on click
    themeToggleBtn.addEventListener('click', () => {
        const currentTheme = document.documentElement.getAttribute('data-theme');
        if (currentTheme === 'dark') {
            document.documentElement.setAttribute('data-theme', 'light');
            localStorage.setItem('theme', 'light');
        } else {
            document.documentElement.setAttribute('data-theme', 'dark');
            localStorage.setItem('theme', 'dark');
        }
    });

    // Privacy Modal Logic
    const privacyModal = document.getElementById('privacyModal');
    const privacyOpenBtn = document.getElementById('privacyOpenBtn');
    const privacyCloseBtn = document.getElementById('privacyCloseBtn');

    if (privacyModal && privacyOpenBtn && privacyCloseBtn) {
        privacyOpenBtn.addEventListener('click', (e) => {
            e.preventDefault();
            privacyModal.showModal();
            document.body.style.overflow = 'hidden'; // Disable background scroll
        });

        privacyCloseBtn.addEventListener('click', () => {
            privacyModal.close();
        });

        // The native 'close' event fires when closed via button OR Esc key
        privacyModal.addEventListener('close', () => {
            document.body.style.overflow = ''; // Restore background scroll
        });
        
        // Close modal when clicking outside of it
        privacyModal.addEventListener('click', (e) => {
            const dialogDimensions = privacyModal.getBoundingClientRect();
            if (
                e.clientX < dialogDimensions.left ||
                e.clientX > dialogDimensions.right ||
                e.clientY < dialogDimensions.top ||
                e.clientY > dialogDimensions.bottom
            ) {
                privacyModal.close();
            }
        });
    }

    // Linux Modal Logic
    const linuxModal = document.getElementById('linuxInstallModal');
    const linuxCloseBtn = document.getElementById('linuxInstallCloseBtn');

    if (linuxModal && linuxCloseBtn) {
        linuxCloseBtn.addEventListener('click', () => {
            linuxModal.close();
        });

        linuxModal.addEventListener('close', () => {
            document.body.style.overflow = ''; 
        });
        
        linuxModal.addEventListener('click', (e) => {
            const dialogDimensions = linuxModal.getBoundingClientRect();
            if (
                e.clientX < dialogDimensions.left ||
                e.clientX > dialogDimensions.right ||
                e.clientY < dialogDimensions.top ||
                e.clientY > dialogDimensions.bottom
            ) {
                linuxModal.close();
            }
        });
    }
});
