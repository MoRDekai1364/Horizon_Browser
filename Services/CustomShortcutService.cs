using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Horizon.Stealth.Services;

namespace Horizon.Stealth.Services;

public static class CustomShortcutService
{
    private static readonly string ConfigPath = Path.Combine(ConfigService.UserDataRoot, "custom_shortcuts.json");
    private static List<CustomShortcut> _shortcuts = new();
    private static readonly object _lock = new();

    static CustomShortcutService()
    {
        LoadShortcuts();
    }

    public static List<CustomShortcut> GetAllShortcuts()
    {
        lock (_lock)
        {
            return _shortcuts.ToList();
        }
    }

    public static void LoadShortcuts()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var list = JsonSerializer.Deserialize<List<CustomShortcut>>(json);
                    if (list != null && list.Count > 0)
                    {
                        _shortcuts = list;
                        EnsureBuiltIns();
                        return;
                    }
                }
            }
            catch { }

            _shortcuts = GetDefaultShortcuts();
            SaveShortcuts();
        }
    }

    public static void SaveShortcuts()
    {
        lock (_lock)
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath) ?? "";
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_shortcuts, options);
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }
    }

    public static void AddShortcut(CustomShortcut shortcut)
    {
        lock (_lock)
        {
            _shortcuts.Add(shortcut);
            SaveShortcuts();
        }
    }

    public static void UpdateShortcut(CustomShortcut shortcut)
    {
        lock (_lock)
        {
            int idx = _shortcuts.FindIndex(s => s.Id == shortcut.Id);
            if (idx >= 0)
            {
                _shortcuts[idx] = shortcut;
                SaveShortcuts();
            }
        }
    }

    public static void DeleteShortcut(string id)
    {
        lock (_lock)
        {
            _shortcuts.RemoveAll(s => s.Id == id && !s.IsBuiltIn);
            SaveShortcuts();
        }
    }

    private static void EnsureBuiltIns()
    {
        var defaults = GetDefaultShortcuts();
        foreach (var def in defaults)
        {
            if (!_shortcuts.Any(s => s.Id == def.Id || s.Title.Equals(def.Title, StringComparison.OrdinalIgnoreCase)))
            {
                _shortcuts.Add(def);
            }
        }
    }

    public static List<CustomShortcut> GetDefaultShortcuts()
    {
        return new List<CustomShortcut>
        {
            // Productivity Creation Actions (.new)
            new CustomShortcut
            {
                Id = "new_google_doc",
                Title = "New Google Doc",
                Description = "Create a blank Google Document instantly",
                Icon = "\U0001F4C4",
                Category = "Productivity",
                ActionType = ShortcutActionType.Url,
                Payload = "https://docs.new",
                Keywords = "google doc text document word new",
                IsBuiltIn = true
            },
            new CustomShortcut
            {
                Id = "new_notion_page",
                Title = "New Notion Page",
                Description = "Create a new Notion workspace document",
                Icon = "\U0001F4D3",
                Category = "Productivity",
                ActionType = ShortcutActionType.Url,
                Payload = "https://notion.new",
                Keywords = "notion notes wiki document new",
                IsBuiltIn = true
            },
            new CustomShortcut
            {
                Id = "new_google_meet",
                Title = "New Google Meet",
                Description = "Start an instant video conference meeting",
                Icon = "\U0001F4F9",
                Category = "Communication",
                ActionType = ShortcutActionType.Url,
                Payload = "https://meet.new",
                Keywords = "meet video call meeting conference new",
                IsBuiltIn = true
            },
            new CustomShortcut
            {
                Id = "new_google_sheet",
                Title = "New Google Sheet",
                Description = "Create a new spreadsheet table",
                Icon = "\U0001F4CA",
                Category = "Productivity",
                ActionType = ShortcutActionType.Url,
                Payload = "https://sheets.new",
                Keywords = "sheet excel spreadsheet table new",
                IsBuiltIn = true
            },
            new CustomShortcut
            {
                Id = "new_github_repo",
                Title = "New GitHub Repo",
                Description = "Create a new Git repository on GitHub",
                Icon = "\U0001F419",
                Category = "Developer",
                ActionType = ShortcutActionType.Url,
                Payload = "https://repo.new",
                Keywords = "github git repository code developer new",
                IsBuiltIn = true
            },
            new CustomShortcut
            {
                Id = "new_excalidraw",
                Title = "New Excalidraw",
                Description = "Open an infinite virtual whiteboard canvas",
                Icon = "\U0001F3A8",
                Category = "Creative",
                ActionType = ShortcutActionType.Url,
                Payload = "https://excalidraw.com",
                Keywords = "canvas draw whiteboard sketch mindmap",
                IsBuiltIn = true
            },

            // JavaScript Script Actions
            new CustomShortcut
            {
                Id = "script_dark_invert",
                Title = "Toggle Page Dark Invert",
                Description = "Inverts page background and colors to dark theme (JavaScript)",
                Icon = "\U0001F319",
                Category = "Script",
                ActionType = ShortcutActionType.Script,
                Payload = @"(function(){
    var id = '__horizon_dark_toggle';
    var el = document.getElementById(id);
    if (el) { el.remove(); }
    else {
        var s = document.createElement('style');
        s.id = id;
        s.innerHTML = 'html { filter: invert(90%) hue-rotate(180deg) !important; background: #121212 !important; } img, video, canvas, iframe { filter: invert(100%) hue-rotate(180deg) !important; }';
        document.head.appendChild(s);
    }
})();",
                KeyGesture = "Ctrl+Shift+D",
                Keywords = "dark mode invert theme black night reader",
                IsBuiltIn = true
            },
            new CustomShortcut
            {
                Id = "script_copy_markdown",
                Title = "Copy as Markdown Link",
                Description = "Copies current page as [Title](URL) into clipboard (JavaScript)",
                Icon = "\U0001F4CB",
                Category = "Script",
                ActionType = ShortcutActionType.Script,
                Payload = @"(function(){
    var md = '[' + document.title + '](' + location.href + ')';
    navigator.clipboard.writeText(md).then(function(){
        var d = document.createElement('div');
        d.style = 'position:fixed;top:20px;left:50%;transform:translateX(-50%);background:#10B981;color:white;padding:10px 22px;border-radius:20px;font-family:Segoe UI, sans-serif;font-size:13px;font-weight:600;z-index:999999;box-shadow:0 4px 18px rgba(0,0,0,0.35);letter-spacing:0.3px;';
        d.textContent = 'Copied Markdown Link: ' + document.title.substring(0, 30) + '...';
        document.body.appendChild(d);
        setTimeout(function(){ d.remove(); }, 2200);
    });
})();",
                KeyGesture = "Ctrl+Shift+C",
                Keywords = "copy markdown link url clipboard share",
                IsBuiltIn = true
            },
            new CustomShortcut
            {
                Id = "script_auto_scroll",
                Title = "Smooth Scroll Page Down",
                Description = "Smoothly scrolls the current viewport down (JavaScript)",
                Icon = "\u2193",
                Category = "Script",
                ActionType = ShortcutActionType.Script,
                Payload = @"window.scrollBy({ top: window.innerHeight * 0.8, behavior: 'smooth' });",
                KeyGesture = "Ctrl+Shift+J",
                Keywords = "scroll down next page reading",
                IsBuiltIn = true
            },
            new CustomShortcut
            {
                Id = "script_clean_overlays",
                Title = "Dismiss Overlays & Modals",
                Description = "Removes paywall masks, consent popups and unlocks page scrolling (JavaScript)",
                Icon = "\U0001F9F9",
                Category = "Script",
                ActionType = ShortcutActionType.Script,
                Payload = @"(function(){
    var selectors = ['div[class*=""modal""]', 'div[class*=""overlay""]', 'div[class*=""backdrop""]', 'div[class*=""popup""]', 'div[class*=""consent""]', 'div[class*=""cookie""]'];
    selectors.forEach(function(s){
        document.querySelectorAll(s).forEach(function(el){
            if (el.offsetWidth > 100 && el.offsetHeight > 100) el.remove();
        });
    });
    document.body.style.overflow = 'auto !important';
    document.documentElement.style.overflow = 'auto !important';
})();",
                Keywords = "bypass paywall remove overlays modals clean popups cookie consent",
                IsBuiltIn = true
            },

            // Built-in Browser Actions
            new CustomShortcut
            {
                Id = "cmd_toggle_sidebar",
                Title = "Toggle Sidebar",
                Description = "Expand or collapse the browser tool and tab sidebar",
                Icon = "\U0001F5C2",
                Category = "Action",
                ActionType = ShortcutActionType.BrowserCommand,
                Payload = "toggle_sidebar",
                KeyGesture = "Ctrl+B",
                Keywords = "sidebar drawer tabs panes utilities collapse expand",
                IsBuiltIn = true
            },
            new CustomShortcut
            {
                Id = "cmd_toggle_mini_mode",
                Title = "Toggle Sidebar Mini Mode",
                Description = "Switch sidebar between compact 52px mini rail and full deck",
                Icon = "\u21C4",
                Category = "Action",
                ActionType = ShortcutActionType.BrowserCommand,
                Payload = "toggle_mini_mode",
                Keywords = "mini mode rail compact 52px slim",
                IsBuiltIn = true
            },
            new CustomShortcut
            {
                Id = "cmd_split_view",
                Title = "Split View (Dual Tab)",
                Description = "View two tabs side-by-side simultaneously",
                Icon = "\u25A6",
                Category = "Action",
                ActionType = ShortcutActionType.BrowserCommand,
                Payload = "split_view",
                Keywords = "split view dual side side side-by-side two tabs",
                IsBuiltIn = true
            },
            new CustomShortcut
            {
                Id = "cmd_take_screenshot",
                Title = "Capture Page Screenshot",
                Description = "Take a full-resolution snapshot of the current web page",
                Icon = "\U0001F4F8",
                Category = "Action",
                ActionType = ShortcutActionType.BrowserCommand,
                Payload = "screenshot",
                Keywords = "screenshot capture snapshot image photo",
                IsBuiltIn = true
            },
            new CustomShortcut
            {
                Id = "cmd_open_notes",
                Title = "Open Quick Notes",
                Description = "Open the floating scratchpad notes editor",
                Icon = "\U0001F4DD",
                Category = "Tool",
                ActionType = ShortcutActionType.BrowserCommand,
                Payload = "open_notes",
                Keywords = "notes memo scratchpad text editor write",
                IsBuiltIn = true
            },
            new CustomShortcut
            {
                Id = "cmd_open_timer",
                Title = "Open Timer & Stopwatch",
                Description = "Launch countdown timer and focus stopwatch",
                Icon = "\u23F1",
                Category = "Tool",
                ActionType = ShortcutActionType.BrowserCommand,
                Payload = "open_timer",
                Keywords = "timer stopwatch clock countdown pomodoro study",
                IsBuiltIn = true
            },
            new CustomShortcut
            {
                Id = "cmd_mute_tab",
                Title = "Mute / Unmute Current Tab",
                Description = "Silence audio output from the active webpage",
                Icon = "\U0001F507",
                Category = "Action",
                ActionType = ShortcutActionType.BrowserCommand,
                Payload = "mute_tab",
                Keywords = "mute audio sound volume silence quiet",
                IsBuiltIn = true
            }
        };
    }

    public static async Task ExecuteShortcutAsync(CustomShortcut shortcut, MainWindow window, string? searchArg = null)
    {
        if (shortcut == null || window == null) return;

        try
        {
            switch (shortcut.ActionType)
            {
                case ShortcutActionType.Script:
                {
                    var webView = window.CurrentBrowser?.MainWebView;
                    if (webView?.CoreWebView2 != null)
                    {
                        await webView.CoreWebView2.ExecuteScriptAsync(shortcut.Payload);
                    }
                    break;
                }

                case ShortcutActionType.Url:
                {
                    string url = shortcut.Payload;
                    string curUrl = window.CurrentTab?.Url ?? "";
                    string curTitle = window.CurrentTab?.Title ?? "";
                    string q = searchArg ?? "";

                    url = url.Replace("{query}", Uri.EscapeDataString(q))
                             .Replace("{url}", Uri.EscapeDataString(curUrl))
                             .Replace("{title}", Uri.EscapeDataString(curTitle));

                    window.CreateNewTab(url, activate: true);
                    break;
                }

                case ShortcutActionType.BrowserCommand:
                {
                    window.ExecuteBrowserCommand(shortcut.Payload);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CustomShortcutService] Error executing {shortcut.Title}: {ex.Message}");
        }
    }

    public static bool TryExecuteHotkey(KeyEventArgs e, MainWindow window)
    {
        if (e == null || window == null) return false;

        bool ctrl  = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift)   == ModifierKeys.Shift;
        bool alt   = (Keyboard.Modifiers & ModifierKeys.Alt)     == ModifierKeys.Alt;

        var shortcuts = GetAllShortcuts().Where(s => s.IsEnabled && !string.IsNullOrWhiteSpace(s.KeyGesture)).ToList();

        foreach (var s in shortcuts)
        {
            if (MatchesGesture(s.KeyGesture, ctrl, shift, alt, e.Key))
            {
                _ = ExecuteShortcutAsync(s, window);
                e.Handled = true;
                return true;
            }
        }

        return false;
    }

    private static bool MatchesGesture(string gesture, bool ctrl, bool shift, bool alt, Key key)
    {
        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool reqCtrl = false;
        bool reqShift = false;
        bool reqAlt = false;
        string keyName = "";

        foreach (var p in parts)
        {
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase))
                reqCtrl = true;
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                reqShift = true;
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                reqAlt = true;
            else
                keyName = p;
        }

        if (reqCtrl != ctrl || reqShift != shift || reqAlt != alt)
            return false;

        if (Enum.TryParse<Key>(keyName, true, out var parsedKey))
        {
            return parsedKey == key;
        }

        return false;
    }
}
