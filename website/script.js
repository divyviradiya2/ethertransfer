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
    
    // Check for saved theme preference safely (avoids crash if localStorage is blocked)
    let savedTheme = null;
    try {
        savedTheme = localStorage.getItem('theme');
    } catch (e) {
        console.warn('localStorage is blocked or unavailable.');
    }
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
            try { localStorage.setItem('theme', 'light'); } catch (e) {}
        } else {
            document.documentElement.setAttribute('data-theme', 'dark');
            try { localStorage.setItem('theme', 'dark'); } catch (e) {}
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

    // Mobile Navigation Drawer Logic
    const mobileMenuBtn = document.getElementById('mobile-menu-btn');
    const mobileNavDrawer = document.getElementById('mobile-nav-drawer');
    const mobileNavBackdrop = document.getElementById('mobile-nav-backdrop');
    const mobileNavLinks = document.querySelectorAll('.mobile-nav-link, .mobile-cta-btn');

    if (mobileMenuBtn && mobileNavDrawer) {
        const openMobileMenu = () => {
            mobileNavDrawer.classList.add('is-open');
            mobileMenuBtn.setAttribute('aria-expanded', 'true');
            mobileNavDrawer.setAttribute('aria-hidden', 'false');
            document.body.style.overflow = 'hidden';
        };

        const closeMobileMenu = () => {
            mobileNavDrawer.classList.remove('is-open');
            mobileMenuBtn.setAttribute('aria-expanded', 'false');
            mobileNavDrawer.setAttribute('aria-hidden', 'true');
            document.body.style.overflow = '';
        };

        mobileMenuBtn.addEventListener('click', () => {
            const isOpen = mobileNavDrawer.classList.contains('is-open');
            if (isOpen) {
                closeMobileMenu();
            } else {
                openMobileMenu();
            }
        });

        if (mobileNavBackdrop) {
            mobileNavBackdrop.addEventListener('click', closeMobileMenu);
        }

        mobileNavLinks.forEach(link => {
            link.addEventListener('click', () => {
                closeMobileMenu();
            });
        });

        // Close on Escape key press
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && mobileNavDrawer.classList.contains('is-open')) {
                closeMobileMenu();
            }
        });

        // Close mobile drawer automatically if viewport resized to desktop
        window.addEventListener('resize', () => {
            if (window.innerWidth > 768 && mobileNavDrawer.classList.contains('is-open')) {
                closeMobileMenu();
            }
        });
    }
});
