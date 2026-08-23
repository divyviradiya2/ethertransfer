/**
 * EtherTransfer Documentation Engine
 * Handles Category Tab Switching, Hash Routing, Instant Search, Multi-OS Switcher, Theme Sync, and Live ScrollSpy
 */
document.addEventListener('DOMContentLoaded', () => {

    const topNav = document.querySelector('.top-nav');
    const subNav = document.querySelector('.docs-subnav');
    const categoryTabs = document.querySelectorAll('.docs-cat-tab');
    const categoryPanes = document.querySelectorAll('.docs-category-pane');
    const sidebarLinks = document.querySelectorAll('.sidebar-doc-link');
    const tocList = document.getElementById('tocNavList');

    let currentCategoryId = null;
    let currentActiveTargetId = null;
    let isProgrammaticScroll = false;
    let scrollRafId = null;

    // Calculate dynamic header height offset
    function getHeaderOffset() {
        const topHeight = topNav ? topNav.offsetHeight : 62;
        const subHeight = subNav ? subNav.offsetHeight : 46;
        return topHeight + subHeight + 16;
    }

    // Direct scroll to an element with precise header offset (No-Op if already in position)
    function scrollToTarget(element) {
        if (!element) return;
        const offset = getHeaderOffset();
        const elementRect = element.getBoundingClientRect();

        // 1. If element is already properly positioned at the top offset (+/- 15px), do nothing!
        if (Math.abs(elementRect.top - offset) < 15) {
            return;
        }

        const absoluteElementTop = elementRect.top + window.pageYOffset;
        const targetScrollY = Math.max(0, absoluteElementTop - offset);
        const maxScroll = Math.max(0, document.documentElement.scrollHeight - window.innerHeight);
        const clampedTargetY = Math.min(targetScrollY, maxScroll);

        // 2. If current window scroll is already at or near clamped target (+/- 10px), do nothing!
        if (Math.abs(window.pageYOffset - clampedTargetY) < 10) {
            return;
        }

        isProgrammaticScroll = true;
        window.scrollTo({
            top: clampedTargetY,
            behavior: 'smooth'
        });

        setTimeout(() => {
            isProgrammaticScroll = false;
            updateScrollSpy();
        }, 500);
    }

    // Live ScrollSpy: Updates TOC and sidebar active links as user manually scrolls
    function updateScrollSpy() {
        if (isProgrammaticScroll) return;

        const activePane = document.getElementById(`pane-${currentCategoryId}`);
        if (!activePane) return;

        const headings = Array.from(activePane.querySelectorAll('h2[id], h3[id]'));
        if (headings.length === 0) return;

        const offset = getHeaderOffset() + 35;
        const scrollY = window.pageYOffset;
        const windowHeight = window.innerHeight;
        const documentHeight = document.documentElement.scrollHeight;

        let activeHeadingId = null;

        // If at the very bottom of the document, activate the last heading
        if (scrollY + windowHeight >= documentHeight - 50) {
            activeHeadingId = headings[headings.length - 1].id;
        } else {
            for (let i = 0; i < headings.length; i++) {
                const rect = headings[i].getBoundingClientRect();
                if (rect.top <= offset) {
                    activeHeadingId = headings[i].id;
                } else {
                    break;
                }
            }

            // If user is at the top of the page before first heading, default to first heading
            if (!activeHeadingId && headings.length > 0) {
                activeHeadingId = headings[0].id;
            }
        }

        if (activeHeadingId && activeHeadingId !== currentActiveTargetId) {
            currentActiveTargetId = activeHeadingId;
            highlightActiveTocLink(activeHeadingId);
            highlightActiveSidebarLink(activeHeadingId);
        }
    }

    function onScrollThrottled() {
        if (scrollRafId) return;
        scrollRafId = requestAnimationFrame(() => {
            updateScrollSpy();
            scrollRafId = null;
        });
    }

    window.addEventListener('scroll', onScrollThrottled, { passive: true });
    window.addEventListener('resize', onScrollThrottled, { passive: true });

    // Build Table of Contents for the active category
    function updateTOC(catId) {
        if (!tocList) return;
        tocList.innerHTML = '';

        const activePane = document.getElementById(`pane-${catId}`);
        if (!activePane) return;

        const headings = activePane.querySelectorAll('h2[id], h3[id]');
        headings.forEach(heading => {
            const li = document.createElement('li');
            li.style.margin = '0';

            const a = document.createElement('a');
            a.href = `#${heading.id}`;
            a.className = heading.tagName === 'H3' ? 'toc-nav-link sub-item' : 'toc-nav-link';
            a.textContent = heading.textContent.replace('#', '').trim();

            a.addEventListener('click', (e) => {
                e.preventDefault();

                // If already on this exact topic, DO ABSOLUTELY NOTHING
                if (currentActiveTargetId === heading.id) {
                    return;
                }

                currentActiveTargetId = heading.id;
                history.pushState(null, null, `#${heading.id}`);
                scrollToTarget(heading);
                highlightActiveTocLink(heading.id);
                highlightActiveSidebarLink(heading.id);
            });

            li.appendChild(a);
            tocList.appendChild(li);
        });

        // Initialize active state for TOC
        requestAnimationFrame(() => {
            updateScrollSpy();
        });
    }

    function highlightActiveTocLink(targetId) {
        document.querySelectorAll('.toc-nav-link').forEach(link => {
            if (link.getAttribute('href') === `#${targetId}`) {
                link.classList.add('active');
            } else {
                link.classList.remove('active');
            }
        });
    }

    function highlightActiveSidebarLink(targetId) {
        sidebarLinks.forEach(link => {
            if (link.getAttribute('href') === `#${targetId}`) {
                link.classList.add('active');
            } else {
                link.classList.remove('active');
            }
        });
    }

    // Switch Category Pane
    function switchCategory(catId, targetHeadingId = null) {
        const categoryChanged = (currentCategoryId !== catId);
        currentCategoryId = catId;

        // 1. Update Category Tabs
        categoryTabs.forEach(tab => {
            if (tab.dataset.category === catId) {
                tab.classList.add('active');
                try {
                    tab.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'nearest' });
                } catch(e) {}
            } else {
                tab.classList.remove('active');
            }
        });

        // 2. Show active category pane, hide others
        if (categoryChanged) {
            categoryPanes.forEach(pane => {
                if (pane.id === `pane-${catId}`) {
                    pane.style.display = 'block';
                    pane.classList.add('active');
                } else {
                    pane.style.display = 'none';
                    pane.classList.remove('active');
                }
            });

            // 3. Highlight sidebar items for this category
            sidebarLinks.forEach(link => {
                const parentBlock = link.closest('.sidebar-category-block');
                if (parentBlock && parentBlock.dataset.category === catId) {
                    link.style.opacity = '1';
                } else {
                    link.style.opacity = '0.6';
                }
            });

            // 4. Update Table of Contents
            updateTOC(catId);
        }

        // 5. Scroll to target heading or top
        if (targetHeadingId) {
            currentActiveTargetId = targetHeadingId;
            const targetEl = document.getElementById(targetHeadingId);
            if (targetEl) {
                highlightActiveSidebarLink(targetHeadingId);
                highlightActiveTocLink(targetHeadingId);
                if (categoryChanged) {
                    requestAnimationFrame(() => {
                        setTimeout(() => {
                            scrollToTarget(targetEl);
                        }, 40);
                    });
                } else {
                    scrollToTarget(targetEl);
                }
            }
        } else {
            currentActiveTargetId = null;
            if (window.pageYOffset > 10) {
                isProgrammaticScroll = true;
                window.scrollTo({ top: 0, behavior: 'smooth' });
                setTimeout(() => {
                    isProgrammaticScroll = false;
                    updateScrollSpy();
                }, 500);
            } else {
                updateScrollSpy();
            }
        }
    }

    // Find category ID for any section target ID
    function getCategoryForTarget(targetId) {
        // Direct category ID match
        const directCategoryPane = document.getElementById(`pane-${targetId}`);
        if (directCategoryPane) return targetId;

        // Heading ID within a category pane
        const targetEl = document.getElementById(targetId);
        if (targetEl) {
            const parentPane = targetEl.closest('.docs-category-pane');
            if (parentPane) {
                return parentPane.id.replace('pane-', '');
            }
        }

        // Check sidebar block mapping
        const matchingLink = document.querySelector(`.sidebar-doc-link[href="#${targetId}"]`);
        if (matchingLink) {
            const parentBlock = matchingLink.closest('.sidebar-category-block');
            if (parentBlock && parentBlock.dataset.category) {
                return parentBlock.dataset.category;
            }
        }

        return 'getting-started';
    }

    // Handle Hash Route
    function navigateToHash(hash) {
        const cleanHash = (hash || '').replace('#', '').trim();
        if (!cleanHash) {
            switchCategory('getting-started');
            return;
        }

        const catId = getCategoryForTarget(cleanHash);
        const isHeading = (cleanHash !== catId);

        switchCategory(catId, isHeading ? cleanHash : null);
    }

    // Category Tab Click
    categoryTabs.forEach(tab => {
        tab.addEventListener('click', (e) => {
            e.preventDefault();
            const catId = tab.dataset.category;
            
            // If already on this category tab and at the top, DO ABSOLUTELY NOTHING
            if (currentCategoryId === catId && window.pageYOffset < 10) {
                return;
            }

            currentActiveTargetId = null;
            history.pushState(null, null, `#${catId}`);
            switchCategory(catId);
        });
    });

    // Sidebar Link Click
    sidebarLinks.forEach(link => {
        link.addEventListener('click', (e) => {
            const href = link.getAttribute('href');
            if (href && href.startsWith('#')) {
                e.preventDefault();
                const targetId = href.substring(1);

                // If this sidebar link is ALREADY active/selected, DO ABSOLUTELY NOTHING
                if (currentActiveTargetId === targetId || link.classList.contains('active')) {
                    const sidebar = document.getElementById('docsSidebarNav');
                    if (sidebar) sidebar.classList.remove('is-open');
                    return;
                }

                currentActiveTargetId = targetId;
                history.pushState(null, null, `#${targetId}`);
                navigateToHash(targetId);

                const sidebar = document.getElementById('docsSidebarNav');
                if (sidebar) sidebar.classList.remove('is-open');
            }
        });
    });

    // Stepper buttons (Next / Prev Chapter)
    document.querySelectorAll('.docs-stepper-btn').forEach(btn => {
        btn.addEventListener('click', (e) => {
            const targetCat = btn.dataset.targetCategory;
            if (targetCat) {
                e.preventDefault();
                currentActiveTargetId = null;
                history.pushState(null, null, `#${targetCat}`);
                switchCategory(targetCat);
            }
        });
    });

    // ================= Theme Synchronization =================
    const themeToggleBtn = document.getElementById('theme-toggle');
    let savedTheme = null;
    try {
        savedTheme = localStorage.getItem('theme');
    } catch (e) {
        console.warn('localStorage is unavailable.');
    }
    const prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;

    if (savedTheme === 'dark' || (!savedTheme && prefersDark)) {
        document.documentElement.setAttribute('data-theme', 'dark');
    } else {
        document.documentElement.setAttribute('data-theme', 'light');
    }

    if (themeToggleBtn) {
        themeToggleBtn.addEventListener('click', () => {
            const currentTheme = document.documentElement.getAttribute('data-theme');
            const newTheme = currentTheme === 'dark' ? 'light' : 'dark';
            document.documentElement.setAttribute('data-theme', newTheme);
            try { localStorage.setItem('theme', newTheme); } catch (e) {}
        });
    }

    // ================= Multi-OS Switcher =================
    let preferredOS = 'windows';
    try {
        preferredOS = localStorage.getItem('preferredOS') || 'windows';
    } catch(e) {}

    function applyPreferredOS(os) {
        document.querySelectorAll('.docs-os-switcher').forEach(switcher => {
            const btns = switcher.querySelectorAll('.docs-os-tab-btn');
            const panes = switcher.querySelectorAll('.docs-os-pane');

            btns.forEach(btn => {
                if (btn.dataset.os === os) {
                    btn.classList.add('active');
                } else {
                    btn.classList.remove('active');
                }
            });

            panes.forEach(pane => {
                if (pane.dataset.os === os) {
                    pane.classList.add('active');
                } else {
                    pane.classList.remove('active');
                }
            });
        });
    }

    document.querySelectorAll('.docs-os-tab-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            if (btn.classList.contains('active')) {
                return; // Already selected, do nothing
            }
            const os = btn.dataset.os;
            try { localStorage.setItem('preferredOS', os); } catch(e) {}
            applyPreferredOS(os);
        });
    });

    applyPreferredOS(preferredOS);

    // ================= Copy Code Buttons =================
    document.querySelectorAll('.copy-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            const codeBox = btn.closest('.code-box') || btn.closest('.code-wrapper');
            if (!codeBox) return;
            const codeEl = codeBox.querySelector('pre') || codeBox.querySelector('code');
            if (!codeEl) return;

            const textToCopy = codeEl.innerText.trim();
            navigator.clipboard.writeText(textToCopy).then(() => {
                const originalText = btn.innerText;
                btn.innerText = 'Copied!';
                setTimeout(() => {
                    btn.innerText = originalText;
                }, 2000);
            }).catch(err => {
                console.error('Failed to copy: ', err);
            });
        });
    });

    // ================= Instant Client-Side Search =================
    const searchModal = document.getElementById('searchOverlay');
    const searchTriggerBtn = document.getElementById('subnavSearchBtn');
    const searchCloseBtn = document.getElementById('searchCloseBtn');
    const searchInput = document.getElementById('searchInputField');
    const searchResultsList = document.getElementById('searchResultsContainer');

    const searchIndex = [];
    document.querySelectorAll('.docs-category-pane').forEach(pane => {
        const catId = pane.id.replace('pane-', '');
        const catTitle = pane.dataset.categoryTitle || catId;

        pane.querySelectorAll('.docs-chapter-section').forEach(section => {
            const heading = section.querySelector('h2, h3');
            if (heading && heading.id) {
                const paragraphs = Array.from(section.querySelectorAll('p, li')).map(p => p.textContent).join(' ');
                searchIndex.push({
                    categoryId: catId,
                    categoryTitle: catTitle,
                    sectionId: heading.id,
                    title: heading.textContent.replace('#', '').trim(),
                    content: paragraphs
                });
            }
        });
    });

    function openSearch() {
        if (searchModal) {
            searchModal.classList.add('active');
            if (searchInput) {
                searchInput.value = '';
                searchInput.focus();
                renderSearchResults('');
            }
            document.body.style.overflow = 'hidden';
        }
    }

    function closeSearch() {
        if (searchModal) {
            searchModal.classList.remove('active');
            document.body.style.overflow = '';
        }
    }

    if (searchTriggerBtn) searchTriggerBtn.addEventListener('click', openSearch);
    if (searchCloseBtn) searchCloseBtn.addEventListener('click', closeSearch);

    if (searchModal) {
        searchModal.addEventListener('click', (e) => {
            if (e.target === searchModal) closeSearch();
        });
    }

    document.addEventListener('keydown', (e) => {
        if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
            e.preventDefault();
            if (searchModal && searchModal.classList.contains('active')) {
                closeSearch();
            } else {
                openSearch();
            }
        }
        if (e.key === 'Escape' && searchModal && searchModal.classList.contains('active')) {
            closeSearch();
        }
    });

    function renderSearchResults(query) {
        if (!searchResultsList) return;
        const q = query.toLowerCase().trim();

        if (!q) {
            searchResultsList.innerHTML = `
                <div class="search-no-results">
                    <p>Type keywords to search across all documentation chapters...</p>
                </div>
            `;
            return;
        }

        const matches = searchIndex.filter(item => {
            return item.title.toLowerCase().includes(q) || item.content.toLowerCase().includes(q) || item.categoryTitle.toLowerCase().includes(q);
        }).slice(0, 10);

        if (matches.length === 0) {
            searchResultsList.innerHTML = `
                <div class="search-no-results">
                    <p>No results found for "${query}". Try searching for discovery, framing, bandwidth, or C#.</p>
                </div>
            `;
            return;
        }

        searchResultsList.innerHTML = matches.map(item => {
            let snippet = item.content;
            const matchIndex = snippet.toLowerCase().indexOf(q);
            if (matchIndex !== -1) {
                const start = Math.max(0, matchIndex - 40);
                const end = Math.min(snippet.length, matchIndex + 120);
                snippet = (start > 0 ? '...' : '') + snippet.substring(start, end) + (end < snippet.length ? '...' : '');
            } else {
                snippet = snippet.substring(0, 120) + '...';
            }

            return `
                <a href="#${item.sectionId}" class="search-hit-item" data-category="${item.categoryId}" data-section="${item.sectionId}">
                    <span class="search-hit-category">${item.categoryTitle}</span>
                    <h4 class="search-hit-title">${item.title}</h4>
                    <p class="search-hit-snippet">${snippet}</p>
                </a>
            `;
        }).join('');

        searchResultsList.querySelectorAll('.search-hit-item').forEach(resultEl => {
            resultEl.addEventListener('click', (e) => {
                e.preventDefault();
                const sectionId = resultEl.dataset.section;
                closeSearch();
                if (currentActiveTargetId === sectionId) {
                    return;
                }
                history.pushState(null, null, `#${sectionId}`);
                navigateToHash(sectionId);
            });
        });
    }

    if (searchInput) {
        searchInput.addEventListener('input', (e) => {
            renderSearchResults(e.target.value);
        });
    }

    // ================= Mobile Navigation Drawer & Sidebar Drawer =================
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
            if (isOpen) closeMobileMenu();
            else openMobileMenu();
        });

        if (mobileNavBackdrop) mobileNavBackdrop.addEventListener('click', closeMobileMenu);
        mobileNavLinks.forEach(link => link.addEventListener('click', closeMobileMenu));
    }

    // Mobile Chapter Sidebar Toggle
    const mobileSidebarToggle = document.getElementById('mobileDocsSidebarToggle');
    const docsSidebarNav = document.getElementById('docsSidebarNav');

    if (mobileSidebarToggle && docsSidebarNav) {
        mobileSidebarToggle.addEventListener('click', () => {
            docsSidebarNav.classList.toggle('is-open');
        });

        document.addEventListener('click', (e) => {
            if (docsSidebarNav.classList.contains('is-open') && !docsSidebarNav.contains(e.target) && !mobileSidebarToggle.contains(e.target)) {
                docsSidebarNav.classList.remove('is-open');
            }
        });
    }

    // Initialize Routing on Page Load & Handle Popstate (Browser History)
    navigateToHash(window.location.hash);
    window.addEventListener('popstate', () => {
        navigateToHash(window.location.hash);
    });
});
