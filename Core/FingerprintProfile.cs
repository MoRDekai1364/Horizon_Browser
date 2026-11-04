using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Horizon.Stealth.Services;
using Microsoft.Web.WebView2.Core;

namespace Horizon.Stealth.Core;

public static class FingerprintProfile
{
    private static readonly string SessionSeed =
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));

    public static string BuildScript() =>
        SCRIPT.Replace("__SEED__", SessionSeed).Replace("__EXEMPT__", FingerprintExemptions.ToJsArray());

    private sealed class ScriptHolder { public string Id = ""; }

    private static readonly ConditionalWeakTable<CoreWebView2, ScriptHolder> _holders = new();
    private static readonly List<WeakReference<CoreWebView2>> _views = new();

    public static async Task RegisterAsync(CoreWebView2 webView)
    {
        string id = await webView.AddScriptToExecuteOnDocumentCreatedAsync(BuildScript());
        _holders.AddOrUpdate(webView, new ScriptHolder { Id = id });
        lock (_views) _views.Add(new WeakReference<CoreWebView2>(webView));
        webView.ProcessFailed += (s, e) => OnProcessFailed(webView, e);
    }

    private static void OnProcessFailed(CoreWebView2 webView, CoreWebView2ProcessFailedEventArgs e)
    {
        if (e.ProcessFailedKind != CoreWebView2ProcessFailedKind.RenderProcessUnresponsive &&
            e.ProcessFailedKind != CoreWebView2ProcessFailedKind.RenderProcessExited) return;

        string host;
        try { host = new Uri(webView.Source).Host; } catch { return; }
        if (string.IsNullOrEmpty(host)) return;

        if (FingerprintExemptions.RecordFailure(host))
        {
            LogService.Write("FP", $"Auto-exempted {host} after repeated renderer failures");
            _ = RefreshAllAsync();
        }
    }

    public static async Task RefreshAllAsync()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        await (await dispatcher.InvokeAsync(async () =>
        {
            var alive = new List<CoreWebView2>();
            lock (_views)
            {
                _views.RemoveAll(w => !w.TryGetTarget(out _));
                foreach (var w in _views)
                    if (w.TryGetTarget(out var v)) alive.Add(v);
            }

            foreach (var v in alive)
            {
                try
                {
                    if (_holders.TryGetValue(v, out var holder))
                    {
                        if (holder.Id.Length > 0) v.RemoveScriptToExecuteOnDocumentCreated(holder.Id);
                        holder.Id = await v.AddScriptToExecuteOnDocumentCreatedAsync(BuildScript());
                    }
                }
                catch (Exception ex)
                {
                    LogService.Write("FP", $"Script refresh failed: {ex.Message}");
                }
            }
        }));
    }

    public static async Task<bool> ToggleAsync(string host)
    {
        bool nowExempt = FingerprintExemptions.Toggle(host);
        await RefreshAllAsync();
        return nowExempt;
    }

    public static string BuildWorkerScript() =>
        WORKER_SCRIPT.Replace("__SEED__", SessionSeed).Replace("__EXEMPT__", FingerprintExemptions.ToJsArray());

    private const string WORKER_SCRIPT = @"
        (function() {
            try {
                if (self.__hzFp) return;
                Object.defineProperty(self, '__hzFp', { value: 1 });
            } catch (e) { return; }

            var seed = '__SEED__';
            var host = '';
            try { host = new URL(self.location.origin).hostname; } catch (e) {}

            var fullHost = (host || '').toLowerCase();
            var exemptHosts = __EXEMPT__;
            for (var xi = 0; xi < exemptHosts.length; xi++) {
                var xe = exemptHosts[xi];
                if (fullHost === xe || fullHost.endsWith('.' + xe)) return;
            }
            host = host.split('.').slice(-2).join('.');

            function hash(s) {
                var x = 2166136261;
                for (var i = 0; i < s.length; i++) {
                    x ^= s.charCodeAt(i);
                    x = Math.imul(x, 16777619);
                }
                return x >>> 0;
            }

            var r = hash(seed + '|' + host);
            var cores = [4, 6, 8, 8, 12, 16][r % 6];
            var mem = [4, 8, 8][(r >>> 8) % 3];

            var spoofed = new WeakMap();
            var origToString = Function.prototype.toString;
            var newToString = function toString() {
                var s = spoofed.get(this);
                return s !== undefined ? s : origToString.call(this);
            };
            spoofed.set(newToString, 'function toString() { [native code] }');
            Object.defineProperty(Function.prototype, 'toString', {
                value: newToString, writable: true, configurable: true
            });

            function override(proto, name, value) {
                var d = Object.getOwnPropertyDescriptor(proto, name);
                if (!d || !d.get) return;
                var getter = function() { return value; };
                Object.defineProperty(getter, 'name', { value: 'get ' + name, configurable: true });
                spoofed.set(getter, 'function get ' + name + '() { [native code] }');
                Object.defineProperty(proto, name, {
                    get: getter, set: d.set,
                    enumerable: d.enumerable, configurable: d.configurable
                });
            }

            if (self.WorkerNavigator) {
                override(WorkerNavigator.prototype, 'hardwareConcurrency', cores);
                override(WorkerNavigator.prototype, 'deviceMemory', mem);
            }
        })();
    ";

    private const string SCRIPT = @"
        (function() {
            try {
                if (window.__hzFp) return;
                Object.defineProperty(window, '__hzFp', { value: 1 });
            } catch (e) { return; }

            var seed = '__SEED__';
            var host = '';
            try { host = window.top.location.hostname; } catch (e) {
                try {
                    var ao = location.ancestorOrigins;
                    if (ao && ao.length) host = new URL(ao[ao.length - 1]).hostname;
                } catch (e2) {}
            }
            if (!host) host = location.hostname || '';

            var fullHost = (host || '').toLowerCase();
            var exemptHosts = __EXEMPT__;
            for (var xi = 0; xi < exemptHosts.length; xi++) {
                var xe = exemptHosts[xi];
                if (fullHost === xe || fullHost.endsWith('.' + xe)) return;
            }
            host = host.split('.').slice(-2).join('.');

            function hash(s) {
                var x = 2166136261;
                for (var i = 0; i < s.length; i++) {
                    x ^= s.charCodeAt(i);
                    x = Math.imul(x, 16777619);
                }
                return x >>> 0;
            }

            var r = hash(seed + '|' + host);
            var cores = [4, 6, 8, 8, 12, 16][r % 6];
            var mem = [4, 8, 8][(r >>> 8) % 3];

            var spoofed = new WeakMap();
            var origToString = Function.prototype.toString;
            var newToString = function toString() {
                var s = spoofed.get(this);
                return s !== undefined ? s : origToString.call(this);
            };
            spoofed.set(newToString, 'function toString() { [native code] }');
            Object.defineProperty(Function.prototype, 'toString', {
                value: newToString, writable: true, configurable: true
            });

            function override(proto, name, value) {
                var d = Object.getOwnPropertyDescriptor(proto, name);
                if (!d || !d.get) return;
                var getter = function() { return value; };
                Object.defineProperty(getter, 'name', { value: 'get ' + name, configurable: true });
                spoofed.set(getter, 'function get ' + name + '() { [native code] }');
                Object.defineProperty(proto, name, {
                    get: getter, set: d.set,
                    enumerable: d.enumerable, configurable: d.configurable
                });
            }

            override(Navigator.prototype, 'hardwareConcurrency', cores);
            override(Navigator.prototype, 'deviceMemory', mem);

            var base = hash(seed + '|' + host + '|px');

            function mulberry(a) {
                return function() {
                    a |= 0; a = a + 0x6D2B79F5 | 0;
                    var t = Math.imul(a ^ a >>> 15, 1 | a);
                    t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t;
                    return ((t ^ t >>> 14) >>> 0) / 4294967296;
                };
            }

            function jit(i, salt) {
                var x = Math.imul(i + 1, 0x9E3779B1) ^ base ^ salt;
                x ^= x >>> 15; x = Math.imul(x, 0x85EBCA6B);
                x ^= x >>> 13; x = Math.imul(x, 0xC2B2AE35);
                x ^= x >>> 16;
                return (x >>> 0) / 4294967296 - 0.5;
            }

            function hook(proto, name, make) {
                var orig = proto[name];
                if (typeof orig !== 'function') return;
                var f = make(orig);
                Object.defineProperty(f, 'name', { value: name, configurable: true });
                Object.defineProperty(f, 'length', { value: orig.length, configurable: true });
                spoofed.set(f, 'function ' + name + '() { [native code] }');
                Object.defineProperty(proto, name, {
                    value: f, writable: true, configurable: true, enumerable: false
                });
            }

            if (window.HTMLCanvasElement && window.CanvasRenderingContext2D) {
                var skipCanvas = new WeakSet();
                var origGetImageData = CanvasRenderingContext2D.prototype.getImageData;
                var origToDataURL = HTMLCanvasElement.prototype.toDataURL;
                var origToBlob = HTMLCanvasElement.prototype.toBlob;

                function noisePixels(data, w, h) {
                    var n = data.length >> 2;
                    if (n < 256) return;
                    var rnd = mulberry(base ^ w ^ (h << 11));
                    for (var k = 0; k < 48; k++) {
                        var idx = Math.floor(rnd() * n) * 4;
                        var ch = Math.floor(rnd() * 3);
                        var d = rnd() < 0.5 ? -1 : 1;
                        if (data[idx + 3] === 0) continue;
                        data[idx + ch] = data[idx + ch] + d;
                    }
                }

                function noisyCopy(canvas) {
                    if (skipCanvas.has(canvas)) return null;
                    var w = canvas.width, h = canvas.height;
                    if (w * h < 256 || w * h > 16000000) return null;
                    var c2 = document.createElement('canvas');
                    c2.width = w; c2.height = h;
                    var cx = c2.getContext('2d');
                    cx.drawImage(canvas, 0, 0);
                    var img = origGetImageData.call(cx, 0, 0, w, h);
                    noisePixels(img.data, w, h);
                    cx.putImageData(img, 0, 0);
                    return c2;
                }

                hook(HTMLCanvasElement.prototype, 'getContext', function(orig) {
                    return function() {
                        var ctx = orig.apply(this, arguments);
                        if (ctx && arguments[0] !== '2d') skipCanvas.add(this);
                        return ctx;
                    };
                });

                hook(CanvasRenderingContext2D.prototype, 'getImageData', function(orig) {
                    return function() {
                        var img = orig.apply(this, arguments);
                        try { noisePixels(img.data, img.width, img.height); } catch (e) {}
                        return img;
                    };
                });

                hook(HTMLCanvasElement.prototype, 'toDataURL', function(orig) {
                    return function() {
                        try {
                            var c = noisyCopy(this);
                            if (c) return origToDataURL.apply(c, arguments);
                        } catch (e) {}
                        return orig.apply(this, arguments);
                    };
                });

                hook(HTMLCanvasElement.prototype, 'toBlob', function(orig) {
                    return function() {
                        var target = this;
                        try {
                            var c = noisyCopy(this);
                            if (c) target = c;
                        } catch (e) {}
                        return origToBlob.apply(target, arguments);
                    };
                });
            }

            if (window.AudioBuffer) {
                var audioDone = new WeakMap();

                hook(AudioBuffer.prototype, 'getChannelData', function(orig) {
                    return function(ch) {
                        var arr = orig.apply(this, arguments);
                        try {
                            var done = audioDone.get(this);
                            if (!done) { done = {}; audioDone.set(this, done); }
                            if (!done[ch] && arr.length <= 1000000) {
                                done[ch] = 1;
                                var salt = ch * 7919;
                                for (var i = 0; i < arr.length; i++) arr[i] += jit(i, salt) * 2e-7;
                            }
                        } catch (e) {}
                        return arr;
                    };
                });

                hook(AudioBuffer.prototype, 'copyFromChannel', function(orig) {
                    return function(dest, ch, start) {
                        var res = orig.apply(this, arguments);
                        try {
                            var done = audioDone.get(this);
                            if (!(done && done[ch]) && dest.length <= 1000000) {
                                var off = start || 0, salt = ch * 7919;
                                for (var i = 0; i < dest.length; i++) dest[i] += jit(i + off, salt) * 2e-7;
                            }
                        } catch (e) {}
                        return res;
                    };
                });
            }

            if (window.Screen) {
                var realW = screen.width, realH = screen.height;
                var realAW = screen.availWidth, realAH = screen.availHeight;
                var sizes = [[1366, 768], [1440, 900], [1536, 864], [1600, 900], [1920, 1080], [2560, 1440], [3440, 1440], [3840, 2160]];
                var pick = null;
                for (var si = 0; si < sizes.length; si++) {
                    var sz = sizes[si];
                    if (sz[0] >= realW && sz[1] >= realH && (!pick || sz[0] * sz[1] < pick[0] * pick[1])) pick = sz;
                }
                if (!pick) pick = [realW, realH];
                var offW = Math.max(0, realW - realAW), offH = Math.max(0, realH - realAH);
                override(Screen.prototype, 'width', pick[0]);
                override(Screen.prototype, 'height', pick[1]);
                override(Screen.prototype, 'availWidth', pick[0] - offW);
                override(Screen.prototype, 'availHeight', pick[1] - offH);
                override(Screen.prototype, 'colorDepth', 24);
                override(Screen.prototype, 'pixelDepth', 24);
            }

            if (Navigator.prototype.getBattery) {
                var battery = null;
                hook(Navigator.prototype, 'getBattery', function(orig) {
                    return function() {
                        if (!battery) {
                            battery = new EventTarget();
                            Object.defineProperties(battery, {
                                charging: { value: true, enumerable: true },
                                level: { value: 1, enumerable: true },
                                chargingTime: { value: 0, enumerable: true },
                                dischargingTime: { value: Infinity, enumerable: true }
                            });
                        }
                        return Promise.resolve(battery);
                    };
                });
            }

            var gpus = [
                ['Google Inc. (NVIDIA)', 'ANGLE (NVIDIA, NVIDIA GeForce GTX 1660 SUPER Direct3D11 vs_5_0 ps_5_0, D3D11)'],
                ['Google Inc. (NVIDIA)', 'ANGLE (NVIDIA, NVIDIA GeForce RTX 3060 Direct3D11 vs_5_0 ps_5_0, D3D11)'],
                ['Google Inc. (NVIDIA)', 'ANGLE (NVIDIA, NVIDIA GeForce RTX 2060 Direct3D11 vs_5_0 ps_5_0, D3D11)'],
                ['Google Inc. (Intel)', 'ANGLE (Intel, Intel(R) UHD Graphics 630 Direct3D11 vs_5_0 ps_5_0, D3D11)'],
                ['Google Inc. (AMD)', 'ANGLE (AMD, AMD Radeon RX 580 Series Direct3D11 vs_5_0 ps_5_0, D3D11)']
            ];
            var gpu = gpus[(r >>> 16) % gpus.length];

            [window.WebGLRenderingContext, window.WebGL2RenderingContext].forEach(function(C) {
                if (!C) return;
                hook(C.prototype, 'getParameter', function(orig) {
                    return function(p) {
                        if (p === 37445) return gpu[0];
                        if (p === 37446) return gpu[1];
                        return orig.apply(this, arguments);
                    };
                });
                hook(C.prototype, 'readPixels', function(orig) {
                    return function(x, y, w, h) {
                        var res = orig.apply(this, arguments);
                        try {
                            var buf = arguments[6];
                            if (buf instanceof Uint8Array && w * h >= 256 && typeof noisePixels === 'function')
                                noisePixels(buf, w, h);
                        } catch (e) {}
                        return res;
                    };
                });
            });

            if (window.AnalyserNode) {
                ['getFloatFrequencyData', 'getFloatTimeDomainData'].forEach(function(name) {
                    hook(AnalyserNode.prototype, name, function(orig) {
                        return function(arr) {
                            var res = orig.apply(this, arguments);
                            try {
                                for (var i = 0; i < arr.length; i++) arr[i] += jit(i, 13) * 1e-3;
                            } catch (e) {}
                            return res;
                        };
                    });
                });
            }
        })();
    ";
}