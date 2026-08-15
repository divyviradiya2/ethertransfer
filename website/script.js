document.addEventListener('DOMContentLoaded', () => {
    // OS Detection for the primary download button
    const osText = document.getElementById('os-text');
    const primaryBtn = document.getElementById('primary-download-btn');
    
    let userOS = "Unknown OS";

    // Simple OS string detection
    if (navigator.userAgent.indexOf("Win") != -1) {
        userOS = "Windows";
    } else if (navigator.userAgent.indexOf("Linux") != -1) {
        userOS = "Linux";
    } else if (navigator.userAgent.indexOf("Mac") != -1) {
        userOS = "MacOS";
    }

    // Update UI based on detected OS
    if (userOS === "Windows" || userOS === "Linux") {
        primaryBtn.textContent = `Download for ${userOS}`;
        osText.textContent = `Auto-detected ${userOS}`;
    } else {
        primaryBtn.textContent = `Download Release`;
        osText.textContent = `Supports Windows & Linux`;
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
});
