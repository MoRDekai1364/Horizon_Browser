using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Horizon.Stealth.Views;

public partial class FeedbackWindow : Window
{
    private const string FeedbackUrl = "https://feedbackcol-nszemvwz.manus.space/feedback";
    private const string ProjectName = "Horizon_Browser";
    private const string GitHubUrl = "https://github.com/MoRDekai1364/Horizon_Browser.git";

    private const string AutoFillTemplate = """
(function () {
    var PROJECT = __PROJECT__;
    var GITHUB = __GITHUB__;
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

    function scan() {
        var els = document.querySelectorAll('input,textarea');
        for (var i = 0; i < els.length; i++) {
            var el = els[i];
            var type = (el.type || '').toLowerCase();
            if (el.dataset.hzFilled || el.value) continue;
            if (type === 'hidden' || type === 'checkbox' || type === 'radio' || type === 'file' || type === 'submit' || type === 'button') continue;
            var text = describe(el);
            if (/github[\s_-]*url/i.test(text)) {
                el.dataset.hzFilled = '1';
                setValue(el, GITHUB);
            } else if (/project[\s_-]*name/i.test(text)) {
                el.dataset.hzFilled = '1';
                setValue(el, PROJECT);
            }
        }
    }

    function schedule() {
        if (pending) return;
        pending = setTimeout(function () { pending = null; scan(); }, 120);
    }

    function start() {
        scan();
        new MutationObserver(schedule).observe(document.documentElement, { childList: true, subtree: true });
        setInterval(scan, 700);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
})();
""";

    private static FeedbackWindow _instance;

    private readonly WebView2 _web = new WebView2();

    public static void ShowSingleton(Window owner)
    {
        if (_instance != null)
        {
            if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
            _instance.Activate();
            return;
        }

        var window = new FeedbackWindow();
        if (owner != null)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        _instance = window;
        window.Show();
    }

    private FeedbackWindow()
    {
        Title = "Horizon Feedback";
        Width = 640;
        Height = 800;
        MinWidth = 420;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(17, 17, 17));
        Content = _web;
        Loaded += async (s, e) => await InitializeAsync();
        Closed += (s, e) =>
        {
            _instance = null;
            _web.Dispose();
        };
    }

    private async Task InitializeAsync()
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Horizon", "FeedbackWebView");
            Directory.CreateDirectory(folder);

            var env = await CoreWebView2Environment.CreateAsync(null, folder);
            await _web.EnsureCoreWebView2Async(env);

            var script = AutoFillTemplate
                .Replace("__PROJECT__", JsonSerializer.Serialize(ProjectName))
                .Replace("__GITHUB__", JsonSerializer.Serialize(GitHubUrl));
            await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);

            _web.CoreWebView2.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;
                _web.CoreWebView2.Navigate(e.Uri);
            };

            await RegisterContextAsync();

            _web.CoreWebView2.Navigate(FeedbackUrl);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Could not open the feedback page.\n\n" + ex.Message, "Horizon Feedback", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}