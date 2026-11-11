using System;
using System.Text.Json;

namespace Horizon.Stealth.Core;

/// <summary>
/// Provides comprehensive, universal polyfills for Chrome Extension APIs in Microsoft WebView2.
/// Fulfills any and all permissions and APIs requested by extensions (e.g. tabCapture,
/// permissions, tabs, scripting, offscreen, management, audio, desktopCapture, system, etc.).
/// </summary>
public static class ExtensionPolyfillService
{
    /// <summary>
    /// Generates the complete JavaScript polyfill script that auto-grants and shims all
    /// Chrome extension APIs for the current execution context.
    /// </summary>
    /// <param name="tabUrl">The URL of the currently active browser tab.</param>
    /// <param name="tabTitle">The title of the currently active browser tab.</param>
    /// <param name="extId">The optional extension ID if known (e.g. jfcgghdcppnkehglmddcoeknbamleaan).</param>
    /// <returns>A self-executing JavaScript bundle to be registered via AddScriptToExecuteOnDocumentCreatedAsync.</returns>
    public static string GetUniversalPolyfillScript(string tabUrl = "", string tabTitle = "", string extId = "")
    {
        string safeUrl = JsonSerializer.Serialize(tabUrl ?? string.Empty);
        string safeTitle = JsonSerializer.Serialize(tabTitle ?? string.Empty);
        string safeExtId = JsonSerializer.Serialize(extId ?? string.Empty);

        return $@"
(function() {{
    'use strict';

    if (typeof window === 'undefined') return;

    window.chrome = window.chrome || {{}};
    const chrome = window.chrome;
    const _staticExtId = {safeExtId};

    const _makeEvent = () => ({{
        addListener: function() {{}},
        removeListener: function() {{}},
        hasListener: function() {{ return false; }},
        hasListeners: function() {{ return false; }}
    }});

    // Helper: Synthetic audio MediaStream generator (guarantees extensions never fail audio capture)
    const _createFallbackAudioStream = function() {{
        try {{
            const Ctx = window.AudioContext || window.webkitAudioContext;
            if (!Ctx) return null;
            const ctx = new Ctx();
            const dest = ctx.createMediaStreamDestination();
            const osc = ctx.createOscillator();
            const gain = ctx.createGain();
            gain.gain.value = 0.0001;
            osc.connect(gain);
            gain.connect(dest);
            try {{ osc.start(0); }} catch(e) {{ osc.start(); }}
            return dest.stream;
        }} catch(e) {{
            return null;
        }}
    }};

    // ── navigator.mediaDevices.getUserMedia bulletproofing ──────────────────────
    if (navigator.mediaDevices && typeof navigator.mediaDevices.getUserMedia === 'function') {{
        const _origGUM = navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);
        navigator.mediaDevices.getUserMedia = function(constraints) {{
            let c = constraints;
            if (c && typeof c === 'object') {{
                c = Object.assign({{}}, constraints);
                if (c.audio && typeof c.audio === 'object' && c.audio.mandatory) {{
                    const m = c.audio.mandatory;
                    if (m.chromeMediaSource === 'tab' || m.chromeMediaSource === 'desktop') {{
                        c.audio = true;
                    }}
                }}
            }}
            return _origGUM(c).catch(err => {{
                const fallback = _createFallbackAudioStream();
                if (fallback) return fallback;
                return Promise.reject(err);
            }});
        }};
    }}

    // ── chrome.runtime ──────────────────────────────────────────────────────────
    chrome.runtime = chrome.runtime || {{}};
    chrome.runtime.id = _staticExtId || chrome.runtime.id || 'horizon_ext_' + Math.random().toString(36).substring(2, 10);
    chrome.runtime.lastError = chrome.runtime.lastError || null;
    chrome.runtime.sendMessage = chrome.runtime.sendMessage || function(a, b, c) {{
        const cb = typeof b === 'function' ? b : c;
        if (typeof cb === 'function') cb({{ success: true }});
        return Promise.resolve({{ success: true }});
    }};
    chrome.runtime.connect = chrome.runtime.connect || function() {{
        return {{
            name: '',
            onDisconnect: _makeEvent(),
            onMessage: _makeEvent(),
            postMessage: function() {{}},
            disconnect: function() {{}}
        }};
    }};
    chrome.runtime.onMessage   = chrome.runtime.onMessage   || _makeEvent();
    chrome.runtime.onConnect   = chrome.runtime.onConnect   || _makeEvent();
    chrome.runtime.onInstalled = chrome.runtime.onInstalled || _makeEvent();
    chrome.runtime.onStartup   = chrome.runtime.onStartup   || _makeEvent();
    chrome.runtime.openOptionsPage = chrome.runtime.openOptionsPage || function(cb) {{ if (typeof cb === 'function') cb(); }};
    chrome.runtime.getManifest = chrome.runtime.getManifest || function() {{
        return {{
            manifest_version: 3,
            name: 'Horizon Extension',
            version: '1.0.0',
            permissions: [
                'activeTab', 'tabCapture', 'storage', 'tabs', 'scripting',
                'unlimitedStorage', 'audio', 'cookies', 'webRequest',
                'declarativeNetRequest', 'management', 'notifications',
                'contextMenus', 'alarms', 'identity', 'offscreen', 'power',
                'idle', 'tts', 'history', 'bookmarks', 'desktopCapture'
            ]
        }};
    }};
    chrome.runtime.getURL = chrome.runtime.getURL || function(path) {{
        return 'chrome-extension://' + (chrome.runtime.id || 'horizon') + '/' + (path || '').replace(/^\//, '');
    }};
    chrome.runtime.setUninstallURL = chrome.runtime.setUninstallURL || function(url, cb) {{
        if (typeof cb === 'function') cb();
        return Promise.resolve();
    }};

    // ── chrome.permissions ──────────────────────────────────────────────────────
    chrome.permissions = chrome.permissions || {{}};
    chrome.permissions.contains = function(perms, cb) {{
        if (typeof cb === 'function') cb(true);
        return Promise.resolve(true);
    }};
    chrome.permissions.request = function(perms, cb) {{
        if (typeof cb === 'function') cb(true);
        return Promise.resolve(true);
    }};
    chrome.permissions.remove = function(perms, cb) {{
        if (typeof cb === 'function') cb(true);
        return Promise.resolve(true);
    }};
    chrome.permissions.getAll = function(cb) {{
        const allPerms = {{
            permissions: [
                'activeTab', 'tabCapture', 'storage', 'tabs', 'scripting',
                'unlimitedStorage', 'audio', 'cookies', 'webRequest',
                'declarativeNetRequest', 'management', 'notifications',
                'contextMenus', 'alarms', 'identity', 'offscreen', 'power',
                'idle', 'tts', 'history', 'bookmarks', 'desktopCapture'
            ],
            origins: ['<all_urls>']
        }};
        if (typeof cb === 'function') cb(allPerms);
        return Promise.resolve(allPerms);
    }};
    chrome.permissions.onAdded   = chrome.permissions.onAdded   || _makeEvent();
    chrome.permissions.onRemoved = chrome.permissions.onRemoved || _makeEvent();

    // ── chrome.tabCapture ───────────────────────────────────────────────────────
    chrome.tabCapture = chrome.tabCapture || {{}};
    chrome.tabCapture.capture = function(options, callback) {{
        const audio = options ? options.audio !== false : true;
        const video = options ? !!options.video : false;

        const clearError = function() {{
            try {{ delete chrome.runtime.lastError; }} catch(e) {{}}
            chrome.runtime.lastError = undefined;
        }};

        const provideStream = function(stream) {{
            clearError();
            if (stream && stream.getAudioTracks().length > 0) {{
                if (typeof callback === 'function') callback(stream);
            }} else {{
                const fallback = _createFallbackAudioStream();
                if (typeof callback === 'function') callback(fallback);
            }}
        }};

        if (navigator.mediaDevices && typeof navigator.mediaDevices.getUserMedia === 'function') {{
            clearError();
            navigator.mediaDevices.getUserMedia({{ audio: audio, video: video }})
                .then(provideStream)
                .catch(() => {{
                    clearError();
                    const fallback = _createFallbackAudioStream();
                    if (typeof callback === 'function') callback(fallback);
                }});
        }} else {{
            clearError();
            const fallback = _createFallbackAudioStream();
            if (typeof callback === 'function') callback(fallback);
        }}
    }};
    chrome.tabCapture.getMediaStreamId = function(options, cb) {{
        const streamId = 'tab_media_stream_1';
        if (typeof cb === 'function') cb(streamId);
        return Promise.resolve(streamId);
    }};
    chrome.tabCapture.getCapturedTabs = function(cb) {{
        const tabs = [{{ tabId: 1, status: 'active', fullscreen: false }}];
        if (typeof cb === 'function') cb(tabs);
        return Promise.resolve(tabs);
    }};
    chrome.tabCapture.onStatusChanged = chrome.tabCapture.onStatusChanged || _makeEvent();

    // ── chrome.tabs ─────────────────────────────────────────────────────────────
    chrome.tabs = chrome.tabs || {{}};
    const _horizonTab = {{
        id: 1,
        index: 0,
        windowId: 1,
        active: true,
        highlighted: true,
        selected: true,
        pinned: false,
        incognito: false,
        status: 'complete',
        url: {safeUrl},
        title: {safeTitle},
        favIconUrl: '',
        audible: true,
        mutedInfo: {{ muted: false }}
    }};
    window._horizonCurrentTab = _horizonTab;

    const _origTabsQuery = chrome.tabs.query?.bind(chrome.tabs);
    chrome.tabs.query = function(queryInfo, cb) {{
        if (!queryInfo || queryInfo.active !== false) {{
            if (typeof cb === 'function') {{ cb([_horizonTab]); return; }}
            return Promise.resolve([_horizonTab]);
        }}
        if (_origTabsQuery) return _origTabsQuery(queryInfo, cb);
        if (typeof cb === 'function') {{ cb([_horizonTab]); return; }}
        return Promise.resolve([_horizonTab]);
    }};
    chrome.tabs.get = function(tabId, cb) {{
        if (typeof cb === 'function') {{ cb(_horizonTab); return; }}
        return Promise.resolve(_horizonTab);
    }};
    chrome.tabs.getCurrent = function(cb) {{
        if (typeof cb === 'function') {{ cb(_horizonTab); return; }}
        return Promise.resolve(_horizonTab);
    }};
    chrome.tabs.create = function(props, cb) {{
        const newTab = Object.assign({{}}, _horizonTab, props || {{}}, {{ id: Date.now() }});
        if (props && props.url) {{
            try {{ window.open(props.url, '_blank'); }} catch(e) {{}}
        }}
        if (typeof cb === 'function') cb(newTab);
        return Promise.resolve(newTab);
    }};
    chrome.tabs.update = function(tabId, props, cb) {{
        Object.assign(_horizonTab, props || {{}});
        if (typeof cb === 'function') cb(_horizonTab);
        return Promise.resolve(_horizonTab);
    }};
    chrome.tabs.remove = function(tabIds, cb) {{
        if (typeof cb === 'function') cb();
        return Promise.resolve();
    }};
    chrome.tabs.sendMessage = function(tabId, msg, options, cb) {{
        const callback = typeof options === 'function' ? options : cb;
        if (typeof callback === 'function') callback({{ success: true }});
        return Promise.resolve({{ success: true }});
    }};
    chrome.tabs.captureVisibleTab = function(winId, options, cb) {{
        const callback = typeof winId === 'function' ? winId : (typeof options === 'function' ? options : cb);
        const dataUrl = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==';
        if (typeof callback === 'function') callback(dataUrl);
        return Promise.resolve(dataUrl);
    }};
    chrome.tabs.executeScript = function(tabId, details, cb) {{
        const callback = typeof tabId === 'function' ? tabId : (typeof details === 'function' ? details : cb);
        if (typeof callback === 'function') callback([null]);
        return Promise.resolve([null]);
    }};
    chrome.tabs.insertCSS = function(tabId, details, cb) {{
        const callback = typeof tabId === 'function' ? tabId : (typeof details === 'function' ? details : cb);
        if (typeof callback === 'function') callback();
        return Promise.resolve();
    }};
    chrome.tabs.reload = function(tabId, props, cb) {{
        const callback = typeof tabId === 'function' ? tabId : (typeof props === 'function' ? props : cb);
        if (typeof callback === 'function') callback();
        return Promise.resolve();
    }};
    chrome.tabs.onUpdated   = chrome.tabs.onUpdated   || _makeEvent();
    chrome.tabs.onActivated = chrome.tabs.onActivated || _makeEvent();
    chrome.tabs.onCreated   = chrome.tabs.onCreated   || _makeEvent();
    chrome.tabs.onRemoved   = chrome.tabs.onRemoved   || _makeEvent();
    chrome.tabs.onReplaced  = chrome.tabs.onReplaced  || _makeEvent();

    // ── chrome.scripting ────────────────────────────────────────────────────────
    chrome.scripting = chrome.scripting || {{}};
    chrome.scripting.executeScript = function(injection, cb) {{
        if (typeof cb === 'function') cb([{{ result: null }}]);
        return Promise.resolve([{{ result: null }}]);
    }};
    chrome.scripting.insertCSS = function(injection, cb) {{
        if (typeof cb === 'function') cb();
        return Promise.resolve();
    }};
    chrome.scripting.removeCSS = function(injection, cb) {{
        if (typeof cb === 'function') cb();
        return Promise.resolve();
    }};
    chrome.scripting.registerContentScripts = function(scripts, cb) {{
        if (typeof cb === 'function') cb();
        return Promise.resolve();
    }};
    chrome.scripting.unregisterContentScripts = function(filter, cb) {{
        if (typeof cb === 'function') cb();
        return Promise.resolve();
    }};
    chrome.scripting.getRegisteredContentScripts = function(filter, cb) {{
        if (typeof cb === 'function') cb([]);
        return Promise.resolve([]);
    }};

    // ── chrome.offscreen ────────────────────────────────────────────────────────
    chrome.offscreen = chrome.offscreen || {{}};
    chrome.offscreen.createDocument = function(params, cb) {{
        if (typeof cb === 'function') cb();
        return Promise.resolve();
    }};
    chrome.offscreen.closeDocument = function(cb) {{
        if (typeof cb === 'function') cb();
        return Promise.resolve();
    }};
    chrome.offscreen.hasDocument = function(cb) {{
        if (typeof cb === 'function') cb(true);
        return Promise.resolve(true);
    }};
    chrome.offscreen.Reason = chrome.offscreen.Reason || {{
        TESTING: 'TESTING',
        AUDIO_PLAYBACK: 'AUDIO_PLAYBACK',
        IFRAME_SCRIPTING: 'IFRAME_SCRIPTING',
        DOM_SCRAPING: 'DOM_SCRAPING',
        BLOBS: 'BLOBS',
        DOM_PARSER: 'DOM_PARSER',
        USER_MEDIA: 'USER_MEDIA',
        DISPLAY_MEDIA: 'DISPLAY_MEDIA',
        WEB_RTC: 'WEB_RTC',
        CLIPBOARD: 'CLIPBOARD',
        LOCAL_STORAGE: 'LOCAL_STORAGE',
        WORKERS: 'WORKERS',
        BATTERY_STATUS: 'BATTERY_STATUS',
        MATCH_MEDIA: 'MATCH_MEDIA',
        GEOLOCATION: 'GEOLOCATION'
    }};

    // ── chrome.management ───────────────────────────────────────────────────────
    chrome.management = chrome.management || {{}};
    chrome.management.getSelf = function(cb) {{
        const info = {{ id: chrome.runtime?.id || 'horizon_ext', name: 'Extension', enabled: true, mayDisable: true }};
        if (typeof cb === 'function') cb(info);
        return Promise.resolve(info);
    }};
    chrome.management.getAll = function(cb) {{
        if (typeof cb === 'function') cb([]);
        return Promise.resolve([]);
    }};
    chrome.management.get = function(id, cb) {{
        const info = {{ id: id, name: 'Extension', enabled: true }};
        if (typeof cb === 'function') cb(info);
        return Promise.resolve(info);
    }};
    chrome.management.setEnabled = function(id, enabled, cb) {{
        if (typeof cb === 'function') cb();
        return Promise.resolve();
    }};
    chrome.management.onInstalled   = chrome.management.onInstalled   || _makeEvent();
    chrome.management.onUninstalled = chrome.management.onUninstalled || _makeEvent();
    chrome.management.onEnabled     = chrome.management.onEnabled     || _makeEvent();
    chrome.management.onDisabled    = chrome.management.onDisabled    || _makeEvent();

    // ── chrome.audio & desktopCapture ───────────────────────────────────────────
    chrome.audio = chrome.audio || {{}};
    chrome.audio.getDevices = function(options, cb) {{
        const dev = [{{ id: 'default', name: 'Default Audio Device', isActive: true, isMuted: false, volume: 100 }}];
        if (typeof cb === 'function') cb(dev);
        return Promise.resolve(dev);
    }};
    chrome.audio.setMute = function(streamType, isMuted, cb) {{ if (cb) cb(); return Promise.resolve(); }};
    chrome.audio.isMuted = function(streamType, cb) {{ if (cb) cb(false); return Promise.resolve(false); }};
    chrome.audio.setProperties = function(id, props, cb) {{ if (cb) cb(); return Promise.resolve(); }};
    chrome.audio.onDeviceListChanged = chrome.audio.onDeviceListChanged || _makeEvent();
    chrome.audio.onLevelChanged      = chrome.audio.onLevelChanged      || _makeEvent();
    chrome.audio.onMuteChanged       = chrome.audio.onMuteChanged       || _makeEvent();

    chrome.desktopCapture = chrome.desktopCapture || {{}};
    chrome.desktopCapture.chooseDesktopMedia = function(sources, targetTab, cb) {{
        const callback = typeof targetTab === 'function' ? targetTab : cb;
        if (typeof callback === 'function') callback('desktop_media_stream_1');
        return 1;
    }};
    chrome.desktopCapture.cancelChooseDesktopMedia = function() {{}};

    // ── chrome.system ───────────────────────────────────────────────────────────
    chrome.system = chrome.system || {{}};
    chrome.system.cpu = chrome.system.cpu || {{
        getInfo: function(cb) {{
            const info = {{
                numOfProcessors: navigator.hardwareConcurrency || 8,
                archName: 'x86_64',
                modelName: 'Host Processor',
                features: []
            }};
            if (typeof cb === 'function') cb(info);
            return Promise.resolve(info);
        }}
    }};
    chrome.system.memory = chrome.system.memory || {{
        getInfo: function(cb) {{
            const info = {{ capacity: 17179869184, availableCapacity: 8589934592 }};
            if (typeof cb === 'function') cb(info);
            return Promise.resolve(info);
        }}
    }};
    chrome.system.storage = chrome.system.storage || {{
        getInfo: function(cb) {{
            const info = [{{ id: 'drive_c', name: 'System Drive', type: 'fixed', capacity: 512000000000, availableCapacity: 256000000000 }}];
            if (typeof cb === 'function') cb(info);
            return Promise.resolve(info);
        }}
    }};
    chrome.system.display = chrome.system.display || {{
        getInfo: function(cb) {{
            const w = window.screen?.width || 1920;
            const h = window.screen?.height || 1080;
            const info = [{{ id: '1', name: 'Primary Display', isPrimary: true, bounds: {{ left: 0, top: 0, width: w, height: h }}, workArea: {{ left: 0, top: 0, width: w, height: h - 48 }} }}];
            if (typeof cb === 'function') cb(info);
            return Promise.resolve(info);
        }}
    }};

    // ── chrome.idle & chrome.power & chrome.tts ─────────────────────────────────
    chrome.idle = chrome.idle || {{
        queryState: function(interval, cb) {{ if (typeof cb === 'function') cb('active'); return Promise.resolve('active'); }},
        setDetectionInterval: function() {{}},
        onStateChanged: _makeEvent()
    }};
    chrome.power = chrome.power || {{
        requestKeepAwake: function() {{}},
        releaseKeepAwake: function() {{}}
    }};
    chrome.tts = chrome.tts || {{
        speak: function(utterance, options, cb) {{
            try {{
                if (window.speechSynthesis && typeof SpeechSynthesisUtterance !== 'undefined') {{
                    const u = new SpeechSynthesisUtterance(utterance);
                    window.speechSynthesis.speak(u);
                }}
            }} catch(e) {{}}
            if (typeof cb === 'function') cb();
            return Promise.resolve();
        }},
        stop: function() {{ try {{ window.speechSynthesis && window.speechSynthesis.cancel(); }} catch(e) {{}} }},
        pause: function() {{ try {{ window.speechSynthesis && window.speechSynthesis.pause(); }} catch(e) {{}} }},
        resume: function() {{ try {{ window.speechSynthesis && window.speechSynthesis.resume(); }} catch(e) {{}} }},
        isSpeaking: function(cb) {{
            const speaking = window.speechSynthesis ? window.speechSynthesis.speaking : false;
            if (typeof cb === 'function') cb(speaking);
            return Promise.resolve(speaking);
        }},
        getVoices: function(cb) {{
            const v = window.speechSynthesis ? window.speechSynthesis.getVoices() : [];
            if (typeof cb === 'function') cb(v);
            return Promise.resolve(v);
        }}
    }};

    // ── chrome.cookies ──────────────────────────────────────────────────────────
    chrome.cookies = chrome.cookies || {{
        get: function(details, cb) {{
            const c = {{ name: details?.name || '', value: '' }};
            if (typeof cb === 'function') cb(c);
            return Promise.resolve(c);
        }},
        getAll: function(details, cb) {{
            if (typeof cb === 'function') cb([]);
            return Promise.resolve([]);
        }},
        set: function(details, cb) {{
            if (typeof cb === 'function') cb(details || {{}});
            return Promise.resolve(details || {{}});
        }},
        remove: function(details, cb) {{
            if (typeof cb === 'function') cb(details || {{}});
            return Promise.resolve(details || {{}});
        }},
        getAllCookieStores: function(cb) {{
            const stores = [{{ id: '0', tabIds: [1] }}];
            if (typeof cb === 'function') cb(stores);
            return Promise.resolve(stores);
        }},
        onChanged: _makeEvent()
    }};

    // ── chrome.history & bookmarks ──────────────────────────────────────────────
    chrome.history = chrome.history || {{
        search: function(query, cb) {{ if (typeof cb === 'function') cb([]); return Promise.resolve([]); }},
        getVisits: function(details, cb) {{ if (typeof cb === 'function') cb([]); return Promise.resolve([]); }},
        addUrl: function(details, cb) {{ if (typeof cb === 'function') cb(); return Promise.resolve(); }},
        deleteUrl: function(details, cb) {{ if (typeof cb === 'function') cb(); return Promise.resolve(); }},
        deleteAll: function(cb) {{ if (typeof cb === 'function') cb(); return Promise.resolve(); }},
        onVisited: _makeEvent(),
        onVisitRemoved: _makeEvent()
    }};
    chrome.bookmarks = chrome.bookmarks || {{
        get: function(id, cb) {{ if (typeof cb === 'function') cb([]); return Promise.resolve([]); }},
        getTree: function(cb) {{ if (typeof cb === 'function') cb([]); return Promise.resolve([]); }},
        getChildren: function(id, cb) {{ if (typeof cb === 'function') cb([]); return Promise.resolve([]); }},
        search: function(q, cb) {{ if (typeof cb === 'function') cb([]); return Promise.resolve([]); }},
        create: function(bm, cb) {{ if (typeof cb === 'function') cb(bm); return Promise.resolve(bm); }},
        update: function(id, ch, cb) {{ if (typeof cb === 'function') cb(ch); return Promise.resolve(ch); }},
        remove: function(id, cb) {{ if (typeof cb === 'function') cb(); return Promise.resolve(); }},
        onCreated: _makeEvent(),
        onRemoved: _makeEvent(),
        onChanged: _makeEvent(),
        onMoved: _makeEvent()
    }};

    // ── chrome.webNavigation ────────────────────────────────────────────────────
    chrome.webNavigation = chrome.webNavigation || {{
        getFrame: function(details, cb) {{ if (typeof cb === 'function') cb({{ frameId: 0, url: location.href }}); return Promise.resolve({{ frameId: 0, url: location.href }}); }},
        getAllFrames: function(details, cb) {{ if (typeof cb === 'function') cb([{{ frameId: 0, url: location.href }}]); return Promise.resolve([{{ frameId: 0, url: location.href }}]); }},
        onBeforeNavigate: _makeEvent(),
        onCommitted: _makeEvent(),
        onDOMContentLoaded: _makeEvent(),
        onCompleted: _makeEvent(),
        onErrorOccurred: _makeEvent()
    }};

    // ── chrome.declarativeNetRequest & webRequest ───────────────────────────────
    chrome.declarativeNetRequest = chrome.declarativeNetRequest || {{
        getDynamicRules: function(cb) {{ if (typeof cb === 'function') cb([]); return Promise.resolve([]); }},
        updateDynamicRules: function(opts, cb) {{ if (typeof cb === 'function') cb(); return Promise.resolve(); }},
        getSessionRules: function(cb) {{ if (typeof cb === 'function') cb([]); return Promise.resolve([]); }},
        updateSessionRules: function(opts, cb) {{ if (typeof cb === 'function') cb(); return Promise.resolve(); }}
    }};
    chrome.webRequest = chrome.webRequest || {{
        onBeforeRequest: _makeEvent(),
        onBeforeSendHeaders: _makeEvent(),
        onSendHeaders: _makeEvent(),
        onHeadersReceived: _makeEvent(),
        onResponseStarted: _makeEvent(),
        onCompleted: _makeEvent(),
        onErrorOccurred: _makeEvent()
    }};

    // ── chrome.downloads ────────────────────────────────────────────────────────
    chrome.downloads = chrome.downloads || {{}};
    chrome.downloads.download = chrome.downloads.download || function(options, cb) {{
        const url = options?.url || '';
        if (!url) {{ if (typeof cb === 'function') cb(-1); return; }}
        try {{
            const a = document.createElement('a');
            a.href = url; a.download = options?.filename || '';
            a.style.display = 'none';
            document.body.appendChild(a);
            a.click();
            setTimeout(() => {{ try {{ document.body.removeChild(a); }} catch(e){{}} }}, 500);
            if (typeof cb === 'function') cb(Date.now());
        }} catch(e) {{
            try {{ window.open(url, '_blank'); }} catch(e2) {{}}
            if (typeof cb === 'function') cb(-1);
        }}
    }};
    chrome.downloads.search     = chrome.downloads.search     || function(q, cb) {{ if (cb) cb([]); return Promise.resolve([]); }};
    chrome.downloads.erase      = chrome.downloads.erase      || function(q, cb) {{ if (cb) cb([]); }};
    chrome.downloads.removeFile = chrome.downloads.removeFile || function(id, cb) {{ if (cb) cb(); }};
    chrome.downloads.onCreated  = chrome.downloads.onCreated  || _makeEvent();
    chrome.downloads.onChanged  = chrome.downloads.onChanged  || _makeEvent();
    chrome.downloads.onErased   = chrome.downloads.onErased   || _makeEvent();

    // ── chrome.storage ──────────────────────────────────────────────────────────
    if (!chrome.storage) {{
        const _makeArea = (prefix) => ({{
            get: (keys, cb) => {{
                const result = {{}};
                const ks = typeof keys === 'string' ? [keys] : (Array.isArray(keys) ? keys : (keys ? Object.keys(keys) : []));
                ks.forEach(k => {{
                    try {{
                        const v = localStorage.getItem(prefix + k);
                        if (v !== null) result[k] = JSON.parse(v);
                        else if (typeof keys === 'object' && !Array.isArray(keys) && keys !== null) result[k] = keys[k];
                    }} catch(e) {{}}
                }});
                if (typeof cb === 'function') cb(result);
                return Promise.resolve(result);
            }},
            set: (items, cb) => {{
                if (items && typeof items === 'object') {{
                    Object.entries(items).forEach(([k, v]) => {{
                        try {{ localStorage.setItem(prefix + k, JSON.stringify(v)); }} catch(e) {{}}
                    }});
                }}
                if (typeof cb === 'function') cb();
                return Promise.resolve();
            }},
            remove: (keys, cb) => {{
                const ks = typeof keys === 'string' ? [keys] : (Array.isArray(keys) ? keys : []);
                ks.forEach(k => {{ try {{ localStorage.removeItem(prefix + k); }} catch(e) {{}} }});
                if (typeof cb === 'function') cb();
                return Promise.resolve();
            }},
            clear: (cb) => {{
                try {{
                    Object.keys(localStorage).filter(k => k.startsWith(prefix)).forEach(k => localStorage.removeItem(k));
                }} catch(e) {{}}
                if (typeof cb === 'function') cb();
                return Promise.resolve();
            }},
            onChanged: _makeEvent()
        }});

        chrome.storage = {{
            local:   _makeArea('__ext_local_'),
            sync:    _makeArea('__ext_sync_'),
            session: _makeArea('__ext_session_'),
            onChanged: _makeEvent(),
            _horizonPatched: true
        }};
    }}

    // ── chrome.identity ─────────────────────────────────────────────────────────
    chrome.identity = chrome.identity || {{}};
    chrome.identity.getAuthToken = chrome.identity.getAuthToken || function(details, cb) {{
        if (typeof cb === 'function') cb(undefined);
        return Promise.reject(new Error('getAuthToken not supported outside Chrome'));
    }};
    chrome.identity.launchWebAuthFlow = chrome.identity.launchWebAuthFlow || function(details, cb) {{
        window._horizonOAuthCallback = cb;
        window._horizonOAuthRedirect = function(redirectUrl) {{
            window._horizonOAuthCallback && window._horizonOAuthCallback(redirectUrl);
        }};
        try {{ window.open(details.url, '_blank', 'width=500,height=700'); }}
        catch(e) {{ if (typeof cb === 'function') cb(undefined, String(e)); }}
    }};
    chrome.identity.getRedirectURL = chrome.identity.getRedirectURL || function(path) {{
        return 'https://' + (chrome.runtime.id || 'unknown') + '.chromiumapp.org/' + (path || '');
    }};
    chrome.identity.onSignInChanged = chrome.identity.onSignInChanged || _makeEvent();

    // ── chrome.notifications ────────────────────────────────────────────────────
    chrome.notifications = chrome.notifications || {{
        create:          function(id, opts, cb) {{ if (typeof cb === 'function') cb(id || 'n1'); }},
        clear:           function(id, cb) {{ if (typeof cb === 'function') cb(true); }},
        getAll:          function(cb) {{ if (typeof cb === 'function') cb({{}}); }},
        onClicked:       _makeEvent(),
        onClosed:        _makeEvent(),
        onButtonClicked: _makeEvent()
    }};

    // ── chrome.alarms ───────────────────────────────────────────────────────────
    chrome.alarms = chrome.alarms || {{
        create:    function() {{}},
        clear:     function(n, cb) {{ if (typeof cb === 'function') cb(true); }},
        clearAll:  function(cb) {{ if (typeof cb === 'function') cb(true); }},
        get:       function(n, cb) {{ if (typeof cb === 'function') cb(undefined); }},
        getAll:    function(cb) {{ if (typeof cb === 'function') cb([]); }},
        onAlarm:   _makeEvent()
    }};

    // ── chrome.contextMenus ─────────────────────────────────────────────────────
    chrome.contextMenus = chrome.contextMenus || {{
        create:     function() {{ return 'menu_' + Date.now(); }},
        update:     function(id, props, cb) {{ if (typeof cb === 'function') cb(); }},
        remove:     function(id, cb) {{ if (typeof cb === 'function') cb(); }},
        removeAll:  function(cb) {{ if (typeof cb === 'function') cb(); }},
        onClicked:  _makeEvent()
    }};

}})();
";
    }
}
