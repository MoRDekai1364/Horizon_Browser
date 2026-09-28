using System;

namespace Horizon.Stealth.Services;

public enum ExtensionSource
{
    Bundled,      // Ships inside the app; cannot be uninstalled, only disabled
    ChromeStore,
    EdgeStore,
    FirefoxStore,
    Manual        // Dropped into folder by the user
}


public class ExtensionRecord
{
    public string          Id          { get; set; } = string.Empty;  // folder name / store id
    public string          Name        { get; set; } = string.Empty;
    public string          Description { get; set; } = string.Empty;
    public string          Version     { get; set; } = string.Empty;
    public string          Icon        { get; set; } = "🧩";
    public ExtensionSource Source      { get; set; } = ExtensionSource.Manual;
    public bool            Enabled     { get; set; } = true;
    public DateTime        InstalledAt { get; set; } = DateTime.Now;

    // Folder name under %LocalAppData%\Horizon_Browser\Extensions\
    public string FolderName => Id;
}