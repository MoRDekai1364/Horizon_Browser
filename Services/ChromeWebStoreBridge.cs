using System;

namespace Horizon.Stealth.Services;

public static class ChromeWebStoreBridge
{
    public static string GetScript() => @"
(function() {
    if (window.__hzCwsBridgeActive) return;
    window.__hzCwsBridgeActive = true;

    function getExtensionId() {
        const match = window.location.pathname.match(/\/detail\/(?:[^\/]+\/)?([a-p]{32})/i);
        return match ? match[1] : '';
    }

    function getExtensionTitle() {
        const h1 = document.querySelector('h1');
        if (h1 && h1.textContent.trim()) return h1.textContent.trim();
        return document.title.replace(/- Chrome Web Store$/i, '').trim();
    }

    // Capture click on Add to Chrome buttons at top phase
    document.addEventListener('click', function(e) {
        const btn = e.target.closest('button, [role=""button""]');
        if (!btn) return;

        const text = (btn.innerText || btn.textContent || '').trim();
        const isAddBtn = /add to (?:chrome|brave|edge)|install/i.test(text) ||
                         btn.classList.contains('Bd8P3') ||
                         btn.getAttribute('jsname') === 'wQO0od';

        if (isAddBtn) {
            const extId = getExtensionId();
            if (extId) {
                e.preventDefault();
                e.stopPropagation();
                e.stopImmediatePropagation();

                window.chrome?.webview?.postMessage({
                    type: 'CWS_NATIVE_INSTALL_CLICK',
                    extensionId: extId,
                    title: getExtensionTitle(),
                    url: window.location.href
                });
            }
        }
    }, true);
})();
";
}
