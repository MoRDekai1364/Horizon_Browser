using System;
using System.Windows;
using Horizon.Stealth.Services;
using Horizon.Stealth.ViewModels;

namespace Horizon.Stealth;

public partial class MainWindow : Window
{
    public void ExecuteBrowserCommand(string command)
    {
        switch (command.ToLowerInvariant())
        {
            case "toggle_sidebar":
                ToggleSidebar_Click(this, new RoutedEventArgs());
                break;
            case "toggle_mini_mode":
                SettingsService.Current.SidebarMiniMode = !SettingsService.Current.SidebarMiniMode;
                SettingsService.Save();
                ApplySidebarMode();
                break;
            case "split_view":
                ToggleSplitView();
                break;
            case "close_split_view":
                ExitSplitView();
                break;
            case "new_tab":
                CreateNewTab();
                break;
            case "close_tab":
                if (CurrentTab != null) CloseTab(CurrentTab);
                break;
            case "reopen_closed_tab":
                if (_closedTabHistory.Count > 0)
                {
                    var (u, _) = _closedTabHistory[^1];
                    _closedTabHistory.RemoveAt(_closedTabHistory.Count - 1);
                    CreateNewTab(u);
                }
                break;
            case "open_notes":
                OpenNotesWindow();
                break;
            case "open_timer":
                OpenTimerWindow();
                break;
            case "open_settings":
                BtnSettings_Click(this, new RoutedEventArgs());
                break;
            case "open_utilities":
                ToggleSidebar(true);
                RightSidebar?.ShowUtilitiesTab();
                break;
            case "screenshot":
                _ = CaptureScrollingScreenshotAsync();
                break;
            case "mute_tab":
                if (CurrentBrowser?.MainWebView?.CoreWebView2 != null && CurrentTab != null)
                {
                    var core = CurrentBrowser.MainWebView.CoreWebView2;
                    core.IsMuted = !core.IsMuted;
                    CurrentTab.IsMuted = core.IsMuted;
                }
                break;
        }
    }
}
