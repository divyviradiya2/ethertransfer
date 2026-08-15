document.addEventListener('DOMContentLoaded', () => {
    // OS Detection for the primary download button
    const osText = document.getElementById('os-text');
    const primaryBtn = document.getElementById('primary-download-btn');
    
    // Default GitHub Release URL
    const releaseUrl = 'https://github.com/divyviradiya2/ethertransfer/releases/latest';

    let userOS = "Unknown OS";
    let icon = "↓";
    let downloadPath = "";

    // Simple OS string detection
    if (navigator.userAgent.indexOf("Win") != -1) {
        userOS = "Windows";
        downloadPath = "/download/EtherTransfer_Setup_x64.exe"; // Update with actual generic asset name if known
    } else if (navigator.userAgent.indexOf("Linux") != -1) {
        userOS = "Linux";
        downloadPath = "/download/EtherTransfer-linux-x64.zip";
    } else if (navigator.userAgent.indexOf("Mac") != -1) {
        userOS = "MacOS";
        // Not officially supported yet, default to releases page
    }

    // Update UI based on detected OS
    if (userOS === "Windows" || userOS === "Linux") {
        primaryBtn.innerHTML = `<span class="btn-icon">${icon}</span> Download for ${userOS}`;
        // If we want to directly link to the file, we can construct the url:
        // primaryBtn.href = releaseUrl + downloadPath; 
        // For safety, pointing to the latest release page is usually best unless you have a hardcoded URL scheme.
        
        osText.textContent = `Auto-detected ${userOS}. Also available for other platforms.`;
    } else {
        primaryBtn.innerHTML = `<span class="btn-icon">${icon}</span> Download EtherTransfer`;
    }
    
    // Smooth scroll for anchor links
    document.querySelectorAll('a[href^="#"]').forEach(anchor => {
        anchor.addEventListener('click', function (e) {
            e.preventDefault();
            const target = document.querySelector(this.getAttribute('href'));
            if (target) {
                target.scrollIntoView({
                    behavior: 'smooth'
                });
            }
        });
    });

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
});
