using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Horizon.Stealth.ViewModels;

namespace Horizon.Stealth.Services;

public static class TabExemptionService
{
    private const string SCRIPT_CHECK_DIRTY_STATE = @"
        (() => {
            try {
                const inputs = document.querySelectorAll('input:not([type=""hidden""]):not([type=""submit""]):not([type=""button""]):not([type=""reset""])');
                for (const el of inputs) {
                    if (el.type === 'checkbox' || el.type === 'radio') {
                        if (el.checked !== el.defaultChecked) return true;
                    } else if (el.value !== el.defaultValue) {
                        return true;
                    }
                }
                for (const el of document.querySelectorAll('textarea')) {
                    if (el.value !== el.defaultValue) return true;
                }
                for (const el of document.querySelectorAll('select')) {
                    for (const opt of el.options) {
                        if (opt.selected !== opt.defaultSelected) return true;
                    }
                }
                for (const el of document.querySelectorAll('[contenteditable=""true""]')) {
                    if (el.innerText && el.innerText.trim().length > 0) return true;
                }
                return false;
            } catch(e) {
                return true;
            }
        })()
    ";

    public static async Task<bool> IsTabExemptFromSuspensionAsync(TabViewModel tab, Controls.BrowserView? bv)
    {
        if (tab.NeverSleep) return true;
        if (tab.IsPlayingAudio || tab.IsActiveDownload) return true;

        if (!SettingsService.Current.EnableExemptionBasedSuspension)
            return false;

        if (bv?.MainWebView?.CoreWebView2 != null)
        {
            try
            {
                if (bv.MainWebView.CoreWebView2.IsDocumentPlayingAudio)
                    return true;

                if (bv.MainWebView.CoreWebView2.ContainsFullScreenElement)
                    return true;

                string resultJson = await bv.MainWebView.CoreWebView2.ExecuteScriptAsync(SCRIPT_CHECK_DIRTY_STATE);
                if (string.IsNullOrWhiteSpace(resultJson) || resultJson == "null")
                    return true;

                if (bool.TryParse(resultJson, out bool isDirty) && isDirty)
                    return true;
            }
            catch
            {
                return true;
            }
        }

        return false;
    }
}
