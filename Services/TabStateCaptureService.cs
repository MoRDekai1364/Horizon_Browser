using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Horizon.Stealth.Controls;

namespace Horizon.Stealth.Services;

public class CapturedFormField
{
    public string? selector { get; set; }
    public int index { get; set; }
    public string type { get; set; } = string.Empty;
    public string? value { get; set; }
}

public class TabCapturedState
{
    public string? url { get; set; }
    public double scrollX { get; set; }
    public double scrollY { get; set; }
    public List<CapturedFormField> fields { get; set; } = new();
    public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class TabStateCaptureService
{
    private const string SCRIPT_CAPTURE_STATE = @"
        (() => {
            try {
                const escapeIdent = (str) => {
                    if (!str) return '';
                    if (window.CSS && window.CSS.escape) return window.CSS.escape(str);
                    return str.replace(/([ #;?%&,.+*~\':""!^$[\]()=>|\/@])/g, '\\$1');
                };
                const formFields = [];
                const inputs = document.querySelectorAll('input:not([type=""hidden""]):not([type=""password""]):not([type=""submit""]):not([type=""button""]):not([type=""reset""])');
                for (let i = 0; i < inputs.length; i++) {
                    const el = inputs[i];
                    const sel = el.id ? ('#' + escapeIdent(el.id)) : (el.name ? ('[name=""' + escapeIdent(el.name) + '""]') : null);
                    formFields.push({
                        selector: sel,
                        index: i,
                        type: el.type || 'text',
                        value: (el.type === 'checkbox' || el.type === 'radio') ? (el.checked ? 'true' : 'false') : (el.value || '')
                    });
                }
                const textareas = document.querySelectorAll('textarea');
                for (let i = 0; i < textareas.length; i++) {
                    const el = textareas[i];
                    const sel = el.id ? ('#' + escapeIdent(el.id)) : (el.name ? ('[name=""' + escapeIdent(el.name) + '""]') : null);
                    formFields.push({
                        selector: sel,
                        index: i,
                        type: 'textarea',
                        value: el.value || ''
                    });
                }
                const selects = document.querySelectorAll('select');
                for (let i = 0; i < selects.length; i++) {
                    const el = selects[i];
                    const sel = el.id ? ('#' + escapeIdent(el.id)) : (el.name ? ('[name=""' + escapeIdent(el.name) + '""]') : null);
                    formFields.push({
                        selector: sel,
                        index: i,
                        type: 'select',
                        value: String(el.selectedIndex)
                    });
                }
                return JSON.stringify({
                    url: window.location.href,
                    scrollX: window.scrollX || window.pageXOffset || 0,
                    scrollY: window.scrollY || window.pageYOffset || 0,
                    fields: formFields
                });
            } catch(e) {
                return null;
            }
        })()
    ";

    public static async Task<TabCapturedState?> CaptureStateAsync(BrowserView? bv)
    {
        if (bv?.MainWebView?.CoreWebView2 == null) return null;
        try
        {
            string raw = await bv.MainWebView.CoreWebView2.ExecuteScriptAsync(SCRIPT_CAPTURE_STATE);
            if (string.IsNullOrWhiteSpace(raw) || raw == "null") return null;

            string json = raw;
            if (raw.StartsWith("\"") && raw.EndsWith("\""))
            {
                var unescaped = JsonSerializer.Deserialize<string>(raw);
                if (!string.IsNullOrWhiteSpace(unescaped)) json = unescaped;
            }

            var state = JsonSerializer.Deserialize<TabCapturedState>(json);
            if (state != null)
            {
                state.CapturedAtUtc = DateTime.UtcNow;
            }
            return state;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<bool> RestoreStateAsync(BrowserView? bv, TabCapturedState state)
    {
        if (bv?.MainWebView?.CoreWebView2 == null || state == null) return false;
        try
        {
            string stateJson = JsonSerializer.Serialize(state);
            string script = @"
                ((json) => {
                    try {
                        const state = typeof json === 'string' ? JSON.parse(json) : json;
                        if (!state) return false;

                        const apply = () => {
                            if (state.scrollX || state.scrollY) {
                                window.scrollTo(state.scrollX, state.scrollY);
                            }
                            if (state.fields && Array.isArray(state.fields)) {
                                for (const f of state.fields) {
                                    let el = null;
                                    if (f.selector) {
                                        try { el = document.querySelector(f.selector); } catch(e){}
                                    }
                                    if (!el && f.type === 'textarea') {
                                        const list = document.querySelectorAll('textarea');
                                        if (list.length > f.index) el = list[f.index];
                                    }
                                    if (!el && f.type === 'select') {
                                        const list = document.querySelectorAll('select');
                                        if (list.length > f.index) el = list[f.index];
                                    }
                                    if (!el) {
                                        const list = document.querySelectorAll('input:not([type=""hidden""]):not([type=""password""]):not([type=""submit""]):not([type=""button""]):not([type=""reset""])');
                                        if (list.length > f.index) el = list[f.index];
                                    }
                                    if (el) {
                                        if (f.type === 'checkbox' || f.type === 'radio') {
                                            el.checked = (f.value === 'true' || f.value === true);
                                        } else if (f.type === 'select') {
                                            const idx = parseInt(f.value, 10);
                                            if (!isNaN(idx) && idx >= 0 && idx < el.options.length) {
                                                el.selectedIndex = idx;
                                            }
                                        } else {
                                            el.value = f.value || '';
                                        }
                                        try {
                                            el.dispatchEvent(new Event('input', { bubbles: true }));
                                            el.dispatchEvent(new Event('change', { bubbles: true }));
                                        } catch(e){}
                                    }
                                }
                            }
                        };

                        apply();
                        setTimeout(apply, 100);
                        setTimeout(apply, 350);
                        return true;
                    } catch(e) {
                        return false;
                    }
                })(" + JsonSerializer.Serialize(stateJson) + @")
            ";

            await bv.MainWebView.CoreWebView2.ExecuteScriptAsync(script);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
