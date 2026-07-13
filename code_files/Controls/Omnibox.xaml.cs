using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Horizon.Stealth.Services;
using Horizon.Stealth.Views;

namespace Horizon.Stealth.Controls;

public partial class Omnibox : UserControl
{
    public event EventHandler<string>? NavigateRequested;

    public Omnibox()
    {
        InitializeComponent();
    }

    public void SetText(string text)
    {
        UrlInput.Text = text;
    }

    /// <summary>
    /// Converts raw omnibox input into a navigable URL.
    /// Rules: absolute URL → pass through; domain-like (no spaces, has dot) → prepend https://;
    /// everything else → build a search URL using the configured engine.
    /// </summary>
    public static string BuildNavigationUrl(string raw)
    {
        raw = raw.Trim();
        if (string.IsNullOrEmpty(raw)) return SettingsService.Current.HomePage;

        // Already an absolute URL
        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https" ||
             uri.Scheme == "about" || uri.Scheme == "data" ||
             uri.Scheme == "chrome-extension" || uri.Scheme == "file"))
            return raw;

        // Looks like a bare domain / localhost (no spaces, contains a dot or is localhost)
        if (!raw.Contains(' ') && (raw.Contains('.') || raw.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)))
            return "https://" + raw;

        // Search query
        string template = SettingsService.Current.SearchEngineUrl;
        if (string.IsNullOrWhiteSpace(template))
            template = "https://alohafind.com/search/?q={query}";

        return template.Replace("{query}", Uri.EscapeDataString(raw));
    }

    private void UrlInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateRequested?.Invoke(this, BuildNavigationUrl(UrlInput.Text));
        }
    }
}