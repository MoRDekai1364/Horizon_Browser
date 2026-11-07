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
                        bool changed = RemoveRetiredBuiltIns();
                        changed |= EnsureBuiltIns();
                        if (changed) SaveShortcuts();
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

    private static bool EnsureBuiltIns()
    {
        bool changed = false;
        var defaults = GetDefaultShortcuts();
        foreach (var def in defaults)
        {
            if (!_shortcuts.Any(s => s.Id == def.Id || s.Title.Equals(def.Title, StringComparison.OrdinalIgnoreCase)))
            {
                _shortcuts.Add(def);
                changed = true;
            }
        }
        return changed;
    }

    private static readonly HashSet<string> RetiredBuiltInIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd_split_view"
    };

    private static bool RemoveRetiredBuiltIns()
    {
        return _shortcuts.RemoveAll(s => RetiredBuiltInIds.Contains(s.Id)) > 0;
    }

    public static readonly (string Command, string Label)[] KnownBrowserCommands =
    {
        ("new_tab", "New Tab"),
        ("close_tab", "Close Tab"),
        ("reopen_closed_tab", "Reopen Closed Tab"),
        ("toggle_sidebar", "Toggle Sidebar"),
        ("toggle_mini_mode", "Toggle Sidebar Mini Mode"),
        ("open_utilities", "Open Utilities"),
        ("open_notes", "Open Quick Notes"),
        ("open_timer", "Open Timer & Stopwatch"),
        ("screenshot", "Capture Page Screenshot"),
        ("mute_tab", "Mute / Unmute Current Tab"),
        ("open_settings", "Open Settings")
    };

    private static readonly HashSet<string> ReservedGestures = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ctrl+R", "Ctrl+T", "Ctrl+Tab", "Ctrl+W", "Ctrl+H",
        "Ctrl+Shift+F", "Ctrl+Shift+T",
        "Ctrl+D0", "Ctrl+D1", "Ctrl+D2", "Ctrl+D3", "Ctrl+D4", "Ctrl+D5", "Ctrl+D6", "Ctrl+D7", "Ctrl+D8", "Ctrl+D9",
        "Ctrl+OemPlus", "Ctrl+OemMinus",
        "F5", "F12"
    };

    private static bool TryParseKeyName(string name, out Key key)
    {
        key = Key.None;
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim();
        if (name.Length == 1 && char.IsDigit(name[0])) name = "D" + name;
        if (char.IsDigit(name[0])) return false;
        if (!Enum.TryParse(name, true, out key)) return false;
        return key != Key.None && Enum.IsDefined(typeof(Key), key);
    }

    public static bool TryNormalizeGesture(string? gesture, out string normalized, out string error)
    {
        normalized = "";
        error = "";
        if (string.IsNullOrWhiteSpace(gesture)) return true;

        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        bool ctrl = false, shift = false;
        string? keyName = null;

        foreach (var p in parts)
        {
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase))
                ctrl = true;
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                shift = true;
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                error = "Alt combinations are not supported. Use Ctrl, Ctrl+Shift or a function key.";
                return false;
            }
            else
            {
                if (keyName != null)
                {
                    error = "Only one key is allowed after the modifiers.";
                    return false;
                }
                keyName = p;
            }
        }

        if (keyName == null)
        {
            error = "Missing key. Example: Ctrl+Shift+K";
            return false;
        }

        if (!TryParseKeyName(keyName, out var key))
        {
            error = $"Unknown key \"{keyName}\". Example: Ctrl+Shift+K";
            return false;
        }

        bool isFunctionKey = key >= Key.F1 && key <= Key.F24;
        if (!ctrl && !isFunctionKey)
        {
            error = "Start with Ctrl (or use a function key) so normal typing is not hijacked.";
            return false;
        }

        normalized = (ctrl ? "Ctrl+" : "") + (shift ? "Shift+" : "") + key;
        return true;
    }

    public static string? ValidateGesture(string? gesture, string? excludeId, out string normalized)
    {
        if (!TryNormalizeGesture(gesture, out normalized, out var error)) return error;
        if (normalized.Length == 0) return null;

        if (ReservedGestures.Contains(normalized))
            return $"{normalized} is already used by the browser.";

        lock (_lock)
        {
            foreach (var s in _shortcuts)
            {
                if (s.Id == excludeId || string.IsNullOrWhiteSpace(s.KeyGesture)) continue;
                string other = TryNormalizeGesture(s.KeyGesture, out var n, out _) && n.Length > 0 ? n : s.KeyGesture.Trim();
                if (string.Equals(other, normalized, StringComparison.OrdinalIgnoreCase))
                    return $"{normalized} is already used by \"{s.Title}\".";
            }
        }

        return null;
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
                Id = "cmd_new_tab",
                Title = "New Tab",
                Description = "Open a blank new tab",
                Icon = "\u2795",
                Category = "Action",
                ActionType = ShortcutActionType.BrowserCommand,
                Payload = "new_tab",
                Keywords = "new tab blank open create",
                IsBuiltIn = true
            },
            new CustomShortcut
            {
                Id = "cmd_close_tab",
                Title = "Close Tab",
                Description = "Close the current tab",
                Icon = "\u2716",
                Category = "Action",
                ActionType = ShortcutActionType.BrowserCommand,
                Payload = "close_tab",
                Keywords = "close tab remove exit",
                IsBuiltIn = true
            },
            new CustomShortcut
            {
                Id = "cmd_reopen_tab",
                Title = "Reopen Closed Tab",
                Description = "Restore the most recently closed tab",
                Icon = "\u21A9",
                Category = "Action",
                ActionType = ShortcutActionType.BrowserCommand,
                Payload = "reopen_closed_tab",
                Keywords = "reopen restore undo closed tab recent",
                IsBuiltIn = true
            },
            new CustomShortcut
            {
                Id = "cmd_open_utilities",
                Title = "Open Utilities",
                Description = "Open the sidebar utilities tab",
                Icon = "\U0001F9F0",
                Category = "Tool",
                ActionType = ShortcutActionType.BrowserCommand,
                Payload = "open_utilities",
                Keywords = "utilities tools sidebar panel",
                IsBuiltIn = true
            },
            new CustomShortcut
            {
                Id = "cmd_open_settings",
                Title = "Open Settings",
                Description = "Open the browser settings window",
                Icon = "\u2699",
                Category = "Action",
                ActionType = ShortcutActionType.BrowserCommand,
                Payload = "open_settings",
                Keywords = "settings preferences options configure",
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

        if (TryParseKeyName(keyName, out var parsedKey))
        {
            return parsedKey == key;
        }

        return false;
    }
}
