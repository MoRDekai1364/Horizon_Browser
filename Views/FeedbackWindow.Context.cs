using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Views;

public partial class FeedbackWindow
{
    private const long MaxLogBytes = 2L * 1024 * 1024;

    private static readonly string[] KnownChannels = { "alpha", "beta", "rc", "release", "stable" };

    private const string ContextScript = """
(function () {
    var ctx = null;
    var descEl = null;
    var pending = null;

    function describe(el) {
        var t = [el.placeholder, el.getAttribute('aria-label'), el.name, el.id].join(' ');
        if (el.labels) {
            for (var i = 0; i < el.labels.length; i++) t += ' ' + el.labels[i].textContent;
        }
        var by = el.getAttribute('aria-labelledby');
        if (by) {
            by.split(' ').forEach(function (id) {
                var n = document.getElementById(id);
                if (n) t += ' ' + n.textContent;
            });
        }
        var p = el.parentElement;
        for (var d = 0; d < 3 && p; d++, p = p.parentElement) {
            if (p.querySelectorAll('input,textarea').length !== 1) break;
            var l = p.querySelector('label');
            if (l) { t += ' ' + l.textContent; break; }
            var s = el.previousElementSibling;
            if (s) { t += ' ' + s.textContent; break; }
        }
        return t;
    }

    function setValue(el, v) {
        var proto = el.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
        Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, v);
        el.dispatchEvent(new Event('input', { bubbles: true }));
        el.dispatchEvent(new Event('change', { bubbles: true }));
    }

    function attachFiles(input) {
        if (input.dataset.hzCtx || !ctx || !ctx.files || !ctx.files.length) return;
        if (input.files && input.files.length) return;
        var dt = new DataTransfer();
        var list = input.multiple ? ctx.files : ctx.files.slice(0, 1);
        list.forEach(function (f) {
            var bin = atob(f.data);
            var bytes = new Uint8Array(bin.length);
            for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
            dt.items.add(new File([bytes], f.name, { type: f.type }));
        });
        input.dataset.hzCtx = '1';
        input.files = dt.files;
        input.dispatchEvent(new Event('input', { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
    }

    function fillDescription() {
        if (!ctx || !ctx.description) return;
        if (descEl && document.contains(descEl)) return;
        var candidates = Array.prototype.slice.call(document.querySelectorAll('textarea'))
            .concat(Array.prototype.slice.call(document.querySelectorAll('input')));
        for (var i = 0; i < candidates.length; i++) {
            var el = candidates[i];
            var type = (el.type || '').toLowerCase();
            if (type === 'hidden' || type === 'checkbox' || type === 'radio' || type === 'file' || type === 'submit' || type === 'button') continue;
            if (el.dataset.hzCtx || el.value) continue;
            if (/description/i.test(describe(el))) {
                el.dataset.hzCtx = '1';
                descEl = el;
                setValue(el, ctx.description);
                return;
            }
        }
    }

    function scan() {
        if (!ctx) return;
        var inputs = document.querySelectorAll('input[type=file]');
        for (var i = 0; i < inputs.length; i++) attachFiles(inputs[i]);
        fillDescription();
    }

    function schedule() {
        if (pending) return;
        pending = setTimeout(function () { pending = null; scan(); }, 120);
    }

    function start() {
        new MutationObserver(schedule).observe(document.documentElement, { childList: true, subtree: true });
        setInterval(scan, 700);
    }

    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', function (e) {
            ctx = e.data;
            scan();
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
})();
""";

    private string _contextJson = "{}";

    private async Task RegisterContextAsync()
    {
        var (version, channel) = ReadVersionInfo();
        string description = "Browser version: " + version + "\nUpdate channel: " + channel + "\n\n";
        string filesJson = await Task.Run(BuildFilesJson);
        _contextJson = "{\"description\":" + JsonSerializer.Serialize(description) + ",\"files\":" + filesJson + "}";

        var core = _web.CoreWebView2;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(ContextScript);
        core.NavigationCompleted += (s, e) =>
        {
            if (e.IsSuccess) core.PostWebMessageAsJson(_contextJson);
        };
    }

    private static List<string> SearchDirectories()
    {
        var paths = new List<string>();
        string currentDir = AppDomain.CurrentDomain.BaseDirectory;
        paths.Add(currentDir);

        string? traversalDir = currentDir;
        while (traversalDir != null)
        {
            if (File.Exists(Path.Combine(traversalDir, "start_mapper.bat")))
            {
                if (!paths.Contains(traversalDir)) paths.Add(traversalDir);
                break;
            }
            traversalDir = Directory.GetParent(traversalDir)?.FullName;
        }
        return paths;
    }

    private static (string Version, string Channel) ReadVersionInfo()
    {
        string raw = string.Empty;
        foreach (string dir in SearchDirectories())
        {
            string path = Path.Combine(dir, "version.txt");
            if (!File.Exists(path)) continue;
            try { raw = File.ReadAllText(path); } catch { }
            break;
        }

        string version = "unknown";
        string channel = "release";
        bool channelFound = false;

        foreach (string line in raw.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0) continue;

            string candidate = trimmed.StartsWith("version", StringComparison.OrdinalIgnoreCase)
                ? trimmed.Substring(7).Trim()
                : trimmed;

            string[] parts = candidate.Split('.');
            if (version == "unknown" && parts.All(p => int.TryParse(p.Trim(), out _)))
            {
                version = parts.Length == 1
                    ? parts[0].Trim() + ".0"
                    : string.Join(".", parts.Select(p => p.Trim()));
                continue;
            }

            if (channelFound) continue;
            foreach (string word in trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string w = word.Trim().ToLowerInvariant();
                if (KnownChannels.Contains(w))
                {
                    channel = w;
                    channelFound = true;
                    break;
                }
            }
        }

        return (version, channel);
    }

    private static List<string> LogDirectories()
    {
        var dirs = new List<string>();
        try
        {
            if (!string.IsNullOrEmpty(ConfigService.LogsRoot)) dirs.Add(ConfigService.LogsRoot);
        }
        catch { }
        dirs.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs"));
        return dirs.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? FindLatest(string pattern)
    {
        string? best = null;
        DateTime bestTime = DateTime.MinValue;

        foreach (string dir in LogDirectories())
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, pattern).ToList(); }
            catch { continue; }

            foreach (string file in files)
            {
                DateTime t;
                try { t = File.GetLastWriteTimeUtc(file); }
                catch { continue; }

                if (best == null || t > bestTime)
                {
                    best = file;
                    bestTime = t;
                }
            }
        }

        return best;
    }

    private static string? ReadTailBase64(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long length = fs.Length;
        if (length <= 0) return null;

        long start = Math.Max(0, length - MaxLogBytes);
        fs.Seek(start, SeekOrigin.Begin);

        var buffer = new byte[length - start];
        int read = 0;
        while (read < buffer.Length)
        {
            int n = fs.Read(buffer, read, buffer.Length - read);
            if (n <= 0) break;
            read += n;
        }

        return read == 0 ? null : Convert.ToBase64String(buffer, 0, read);
    }

    private static string BuildFilesJson()
    {
        var list = new List<object>();
        foreach (string? path in new[] { FindLatest("debug_*.log"), FindLatest("crash_tape*.err") })
        {
            if (string.IsNullOrEmpty(path)) continue;
            try
            {
                string? data = ReadTailBase64(path);
                if (data == null) continue;
                list.Add(new { name = Path.GetFileName(path), type = "text/plain", data });
            }
            catch { }
        }
        return JsonSerializer.Serialize(list);
    }
}