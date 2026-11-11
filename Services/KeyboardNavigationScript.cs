using System;

namespace Horizon.Stealth.Services;

/// <summary>
/// Injected script providing browser-wide keyboard-only element navigation.
/// 
/// Key Bindings:
/// - ` (Backtick): Single-use mode (badges show once, typing a badge clicks it and immediately exits).
/// - Tab + ` (or Shift + `, or Tab within 1.5s then Backtick): Permanent / sticky mode.
///   Badges stay permanently visible and usable across repeated clicks and page navigations until Tab+` or Escape is pressed.
/// - Escape: Dismiss badges at any time.
/// 
/// Visual Appearance:
/// Preserves the exact Vimium-style yellow badge visuals (-webkit-linear-gradient(#FFF785, #FFC542), 
/// #E3BE23 border, 11px bold black text) and HUD footer as displayed in the user interface.
/// 
/// Player Shortcut Immunity:
/// When hints are inactive, all video/audio player shortcuts on YouTube, Vimeo, Netflix, Twitch, 
/// Spotify, SoundCloud, Bilibili, and any HTML5 player (f for fullscreen, space/k for play/pause, 
/// j/l for seek, m for mute, c for captions, 0-9 for seek) remain 100% unintercepted and native.
/// </summary>
public static class KeyboardNavigationScript
{
    public static string GetScript()
    {
        return @"
(function() {
    'use strict';
    if (window.__hzKeyboardNavInitialized) return;
    window.__hzKeyboardNavInitialized = true;

    const HINT_CHARS = 'SADFJKLEWCMPGH';
    // Web elements use prefixes S, A, D, F, K, L, C, M, P, G (10 prefixes * 14 = 140 two-letter codes)
    // Reserved for Native Header: H, J
    // Reserved for Native Sidebar: W, E
    const WEB_PREFIXES = ['S', 'A', 'D', 'F', 'K', 'L', 'C', 'M', 'P', 'G'];
    const NATIVE_PREFIXES = ['H', 'J', 'W', 'E'];

    let isHintsActive = false;
    let isStickyMode = false;
    let currentQuery = '';
    let currentHints = []; // { key, element, marker }
    let container = null;
    let hud = null;
    let isTabDown = false;
    let lastTabTimestamp = 0;

    const CSS = `
        #hz-hints-container {
            position: fixed !important;
            top: 0 !important;
            left: 0 !important;
            width: 100vw !important;
            height: 100vh !important;
            z-index: 2147483647 !important;
            pointer-events: none !important;
            overflow: hidden !important;
        }
        .hz-hint-marker {
            position: fixed !important;
            background: #ffe833 !important;
            background: -webkit-linear-gradient(top, #FFF785 0%, #FFC542 100%) !important;
            border: 1px solid #E3BE23 !important;
            border-radius: 3px !important;
            box-shadow: 0 1px 3px rgba(0, 0, 0, 0.45) !important;
            color: #000000 !important;
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif !important;
            font-size: 11px !important;
            font-weight: bold !important;
            line-height: 12px !important;
            padding: 1px 3px !important;
            text-transform: uppercase !important;
            pointer-events: none !important;
            white-space: nowrap !important;
            display: inline-block !important;
            user-select: none !important;
        }
        .hz-hint-marker-match {
            color: #D35400 !important;
            background: #FFFFFF !important;
            border-radius: 2px !important;
            padding: 0 1px !important;
        }
        .hz-hint-hud {
            position: fixed !important;
            bottom: 12px !important;
            right: 16px !important;
            background: rgba(18, 18, 18, 0.92) !important;
            border: 1px solid rgba(255, 255, 255, 0.25) !important;
            border-radius: 6px !important;
            color: #FFFFFF !important;
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif !important;
            font-size: 12px !important;
            font-weight: 500 !important;
            padding: 4px 12px !important;
            box-shadow: 0 4px 12px rgba(0, 0, 0, 0.5) !important;
            z-index: 2147483647 !important;
            pointer-events: none !important;
            user-select: none !important;
        }
    `;

    function injectStyles() {
        if (document.getElementById('hz-hints-style')) return;
        const style = document.createElement('style');
        style.id = 'hz-hints-style';
        style.textContent = CSS;
        (document.head || document.documentElement).appendChild(style);
    }

    function isElementVisible(el) {
        if (!el || el.nodeType !== Node.ELEMENT_NODE) return false;
        const rect = el.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) return false;
        if (rect.bottom < 0 || rect.top > window.innerHeight || rect.right < 0 || rect.left > window.innerWidth) return false;

        const style = window.getComputedStyle(el);
        if (style.visibility === 'hidden' || style.display === 'none' || parseFloat(style.opacity || '1') < 0.05) return false;
        return true;
    }

    function collectInteractiveElements() {
        const set = new Set();
        const selectors = [
            'a[href]',
            'button:not([disabled])',
            'input:not([type=""hidden""]):not([disabled])',
            'textarea:not([disabled])',
            'select:not([disabled])',
            '[role=""button""]',
            '[role=""link""]',
            '[role=""menuitem""]',
            '[role=""tab""]',
            '[role=""checkbox""]',
            '[role=""radio""]',
            '[role=""switch""]',
            '[tabindex]:not([tabindex=""-1""])',
            '[onclick]',
            '[data-action]',
            // Player specifics (YouTube, Vimeo, Twitch, etc.)
            '.ytp-button',
            '.ytp-play-button',
            '.ytp-volume-panel',
            '.ytp-subtitles-button',
            '.ytp-settings-button',
            '.ytp-miniplayer-button',
            '.ytp-size-button',
            '.ytp-fullscreen-button',
            '.ytp-next-button',
            '.ytp-prev-button',
            '.ytp-autonav-toggle-button',
            'ytd-thumbnail a#thumbnail',
            'ytd-rich-item-renderer a#thumbnail',
            'ytd-compact-video-renderer a#thumbnail',
            'ytd-video-renderer a#thumbnail',
            'yt-icon-button button',
            'ytd-searchbox button',
            'ytd-topbar-logo-renderer',
            'ytd-guide-entry-renderer a',
            'tp-yt-paper-button',
            'video',
            'audio'
        ];

        try {
            const matches = document.querySelectorAll(selectors.join(','));
            for (let i = 0; i < matches.length; i++) {
                if (isElementVisible(matches[i])) set.add(matches[i]);
            }
        } catch (e) {}

        return Array.from(set);
    }

    function generateKeySequence(count) {
        const keys = [];
        const pLen = WEB_PREFIXES.length;
        const cLen = HINT_CHARS.length;

        // Two-letter combinations using WEB_PREFIXES
        for (let i = 0; i < pLen && keys.length < count; i++) {
            for (let j = 0; j < cLen && keys.length < count; j++) {
                keys.push(WEB_PREFIXES[i] + HINT_CHARS[j]);
            }
        }

        // Three-letter combinations if count exceeds 140
        for (let i = 0; i < pLen && keys.length < count; i++) {
            for (let j = 0; j < cLen && keys.length < count; j++) {
                for (let k = 0; k < cLen && keys.length < count; k++) {
                    keys.push(WEB_PREFIXES[i] + HINT_CHARS[j] + HINT_CHARS[k]);
                }
            }
        }
        return keys;
    }

    function createBadge(key, el) {
        const rect = el.getBoundingClientRect();
        const marker = document.createElement('div');
        marker.className = 'hz-hint-marker';
        marker.dataset.key = key;

        let top = Math.max(0, rect.top);
        let left = Math.max(0, rect.left);

        if (top > window.innerHeight - 20) top = window.innerHeight - 20;
        if (left > window.innerWidth - 30) left = window.innerWidth - 30;

        marker.style.top = top + 'px';
        marker.style.left = left + 'px';
        marker.textContent = key;

        return marker;
    }

    function activateHints(sticky, notifyHost) {
        injectStyles();
        exitHints(false);

        isHintsActive = true;
        isStickyMode = !!sticky;
        currentQuery = '';

        if (isStickyMode) {
            try { sessionStorage.setItem('__hzStickyNav', 'true'); } catch (e) {}
        } else {
            try { sessionStorage.removeItem('__hzStickyNav'); } catch (e) {}
        }

        container = document.createElement('div');
        container.id = 'hz-hints-container';

        const elements = collectInteractiveElements();
        const keys = generateKeySequence(elements.length);

        currentHints = [];
        for (let i = 0; i < elements.length; i++) {
            const key = keys[i];
            const el = elements[i];
            const marker = createBadge(key, el);
            container.appendChild(marker);
            currentHints.push({ key, element: el, marker });
        }

        hud = document.createElement('div');
        hud.className = 'hz-hint-hud';
        hud.textContent = isStickyMode
            ? 'Permanent Keyboard Nav \u2014 Tab+` to exit'
            : 'Open link in current tab.';
        container.appendChild(hud);

        (document.body || document.documentElement).appendChild(container);

        if (notifyHost !== false) {
            try {
                window.chrome?.webview?.postMessage({ type: 'hzHintToggle', sticky: isStickyMode, active: true });
            } catch (e) {}
        }
    }

    function refreshHints() {
        if (!isHintsActive && !isStickyMode) return;
        activateHints(isStickyMode, false);
    }

    function exitHints(notifyHost) {
        isHintsActive = false;
        isStickyMode = false;
        currentQuery = '';
        currentHints = [];
        try { sessionStorage.removeItem('__hzStickyNav'); } catch (e) {}

        if (container && container.parentNode) {
            container.parentNode.removeChild(container);
        }
        container = null;
        hud = null;

        if (notifyHost !== false) {
            try {
                window.chrome?.webview?.postMessage({ type: 'hzHintExited' });
            } catch (e) {}
        }
    }

    function filterHints() {
        if (!isHintsActive || !container) return;
        const q = currentQuery.toUpperCase();

        for (let i = 0; i < currentHints.length; i++) {
            const item = currentHints[i];
            if (stringStartsWith(item.key, q)) {
                item.marker.style.display = 'inline-block';
                if (q.length > 0) {
                    item.marker.innerHTML = '<span class=""hz-hint-marker-match"">' +
                        item.key.substring(0, q.length) +
                        '</span>' +
                        item.key.substring(q.length);
                } else {
                    item.marker.textContent = item.key;
                }
            } else {
                item.marker.style.display = 'none';
            }
        }
    }

    function stringStartsWith(str, prefix) {
        return str.indexOf(prefix) === 0;
    }

    function triggerElement(el) {
        if (!el) return;

        if (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable) {
            el.focus();
            if (typeof el.select === 'function' && el.tagName === 'INPUT') {
                try { el.select(); } catch (e) {}
            }
        } else {
            try { el.scrollIntoView?.({ block: 'nearest', inline: 'nearest' }); } catch (e) {}
            const mouseOpts = { bubbles: true, cancelable: true, view: window };
            try { el.dispatchEvent(new MouseEvent('mouseover', mouseOpts)); } catch (e) {}
            try { el.dispatchEvent(new MouseEvent('mousedown', mouseOpts)); } catch (e) {}
            try { el.dispatchEvent(new MouseEvent('mouseup', mouseOpts)); } catch (e) {}
            try { el.click?.(); } catch (e) {}
        }

        if (isStickyMode) {
            currentQuery = '';
            // Immediately unfilter all markers so user can immediately type next badge
            filterHints();
            // Re-scan and refresh markers to reflect dynamic UI/DOM changes
            setTimeout(() => { if (isStickyMode) refreshHints(); }, 60);
            setTimeout(() => { if (isStickyMode) refreshHints(); }, 250);
            setTimeout(() => { if (isStickyMode) refreshHints(); }, 600);
            setTimeout(() => { if (isStickyMode) refreshHints(); }, 1200);
        } else {
            exitHints(true);
        }
    }

    function handleQueryMatch() {
        const q = currentQuery.toUpperCase();
        const exact = currentHints.find(h => h.key === q);
        if (exact) {
            triggerElement(exact.element);
            return;
        }

        // If prefix is dedicated to native elements (Header: H, J; Sidebar: W, E),
        // hide all webpage markers cleanly without undoing the typed character.
        if (q.length > 0 && NATIVE_PREFIXES.includes(q[0])) {
            for (let i = 0; i < currentHints.length; i++) {
                currentHints[i].marker.style.display = 'none';
            }
            return;
        }

        const prefixMatches = currentHints.filter(h => stringStartsWith(h.key, q));
        if (prefixMatches.length === 0) {
            currentQuery = currentQuery.slice(0, -1);
            filterHints();
        } else {
            filterHints();
        }
    }

    // Capture-phase keydown listener
    window.addEventListener('keydown', (e) => {
        if (e.key === 'Tab') {
            isTabDown = true;
            lastTabTimestamp = Date.now();
        }

        const isBacktick = (e.key === '`' || e.code === 'Backquote' || e.key === '~');
        if (isBacktick) {
            const isTabCombo = isTabDown || e.shiftKey || (Date.now() - lastTabTimestamp < 1500);

            if (isTabCombo) {
                e.preventDefault();
                e.stopImmediatePropagation();
                if (isHintsActive && isStickyMode) {
                    exitHints(true);
                } else {
                    activateHints(true /* sticky mode */, true);
                }
                return;
            }

            const activeEl = document.activeElement;
            const isTyping = activeEl && (
                activeEl.tagName === 'INPUT' ||
                activeEl.tagName === 'TEXTAREA' ||
                activeEl.isContentEditable
            );

            if (!isTyping) {
                e.preventDefault();
                e.stopImmediatePropagation();
                if (isHintsActive) {
                    exitHints(true);
                } else {
                    activateHints(false /* single-use mode */, true);
                }
                return;
            }
        }

        if (isHintsActive) {
            if (e.key === 'Escape') {
                e.preventDefault();
                e.stopImmediatePropagation();
                exitHints(true);
                return;
            }

            if (e.key === 'Backspace') {
                e.preventDefault();
                e.stopImmediatePropagation();
                try {
                    window.chrome?.webview?.postMessage({ type: 'hzHintKey', key: 'BACKSPACE' });
                } catch (err) {}
                if (currentQuery.length > 0) {
                    currentQuery = currentQuery.slice(0, -1);
                    filterHints();
                }
                return;
            }

            if (e.key && e.key.length === 1 && !e.ctrlKey && !e.altKey && !e.metaKey) {
                const char = e.key.toUpperCase();
                if (HINT_CHARS.includes(char)) {
                    e.preventDefault();
                    e.stopImmediatePropagation();
                    try {
                        window.chrome?.webview?.postMessage({ type: 'hzHintKey', key: char });
                    } catch (err) {}
                    currentQuery += char;
                    handleQueryMatch();
                    return;
                }
            }
        }
    }, true);

    window.addEventListener('keyup', (e) => {
        if (e.key === 'Tab') {
            isTabDown = false;
        }
    }, true);

    // Automatic sticky re-scan on SPA navigation (YouTube, Netflix, etc.)
    window.addEventListener('popstate', () => { if (isStickyMode) setTimeout(refreshHints, 200); });
    window.addEventListener('hashchange', () => { if (isStickyMode) setTimeout(refreshHints, 200); });
    window.addEventListener('yt-navigate-finish', () => { if (isStickyMode) setTimeout(refreshHints, 300); });
    window.addEventListener('yt-page-data-updated', () => { if (isStickyMode) setTimeout(refreshHints, 300); });

    // Restore sticky mode if page was navigated while sticky was on
    try {
        if (sessionStorage.getItem('__hzStickyNav') === 'true') {
            setTimeout(() => {
                activateHints(true, false);
            }, 100);
        }
    } catch (e) {}

    // Global hooks called from WPF host
    window.__hzToggleHints = function(sticky) {
        if (isHintsActive && !sticky) {
            exitHints(false);
        } else {
            activateHints(!!sticky, false);
        }
    };

    window.__hzExitHints = function() {
        exitHints(false);
    };

    window.__hzHandleKey = function(key) {
        if (!isHintsActive) return;
        if (key === 'ESCAPE') {
            exitHints(false);
        } else if (key === 'BACKSPACE') {
            if (currentQuery.length > 0) {
                currentQuery = currentQuery.slice(0, -1);
                filterHints();
            }
        } else if (typeof key === 'string' && key.length === 1) {
            const char = key.toUpperCase();
            if (HINT_CHARS.includes(char)) {
                currentQuery += char;
                handleQueryMatch();
            }
        }
    };
})();
";
    }
}
