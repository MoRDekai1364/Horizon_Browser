using System;
using System.Text.Json.Serialization;

namespace Horizon.Stealth.Services;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShortcutActionType
{
    Url,
    Script,
    BrowserCommand
}

public class CustomShortcut
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "\u26A1";
    public string Category { get; set; } = "Custom";
    public ShortcutActionType ActionType { get; set; } = ShortcutActionType.Url;
    public string Payload { get; set; } = "";
    public string KeyGesture { get; set; } = "";
    public string Keywords { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public bool IsBuiltIn { get; set; } = false;
}
