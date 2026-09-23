# Horizon Widget Visual Refresh — Style Guide

This document describes the visual language and code patterns used to refresh the
Weather widget popup in Horizon Browser (WPF, C#, `MainWindow.xaml.cs`). Use it to
apply the same look and structure to other header widgets (Notes, Calculator,
Converter, Calendar, Media, etc.) while adapting the *content* to each widget's
own purpose.

Everything below reflects code that already exists and compiles in
`MainWindow.xaml.cs`. When adapting a widget, reuse the shared helpers directly —
do not reinvent them. Only the content of each tab/section should change.

---

## 1. Design intent

- The old widget popups were separate `Window`s with `WindowStyle.ToolWindow`,
  a flat dark-navy background (`#0e1a2e` or similar), and a fixed size.
- The new popups are **frameless, resizable windows** with a custom title bar,
  a background derived from the **user's current homepage wallpaper** (blurred,
  dimmed, tinted), and colors that adapt to that wallpaper instead of being
  hard-coded navy/blue.
- The effect: every widget popup feels like part of the same themed system,
  and that theme follows whatever wallpaper the user has set on the homepage.

---

## 2. Shared theme source: `WeatherBridge`-style pattern

Despite the name, `WeatherBridge.cs` is a general **theme bridge**, not
weather-specific. It exposes:

```csharp
public static System.Windows.Media.Imaging.BitmapSource? ThemeWallpaper { get; }
public static bool       HasTheme             { get; }
public static bool       ThemeDarkWallpaper   { get; }
public static bool       ThemeAdaptive        { get; }
public static MediaColor ThemeAverage         { get; }
public static MediaColor ThemePill            { get; }
public static MediaColor ThemeText            { get; }
public static MediaColor ThemeAccent          { get; }
public static MediaColor ThemeAccentSecondary { get; }
public static MediaColor ThemeAccentSubtle    { get; }
public static event Action? ThemeUpdated;
```

This is published from `HomePageView.xaml.cs` (`ApplyAdaptiveColors` /
`PublishWeatherTheme` / `EnsureWeatherThemeAsync`) whenever the wallpaper or its
sampled average color changes.

**For a new widget popup, do not create a second bridge.** Reuse
`WeatherBridge`'s published values directly. If you want a name that isn't
weather-specific, this is a good opportunity to rename `WeatherBridge` to
something like `AppThemeBridge` and update the one call site in
`HomePageView.xaml.cs` plus all popups — but that's optional and can be done
in a later pass; it's cosmetic, not functional.

---

## 3. Color tokens (derive, don't hard-code)

Every popup should define its own small set of *local* static helpers that
derive from `WeatherBridge`'s published colors, the same way the weather
popup does in `MainWindow.xaml.cs`:

```csharp
private static Color WxBlend(Color a, Color b, double t) =>
    Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

private static Color WxPillOpaque =>
    Color.FromRgb(WeatherBridge.ThemePill.R, WeatherBridge.ThemePill.G, WeatherBridge.ThemePill.B);

private static Color WxPanel        => WxBlend(WeatherBridge.ThemeAverage, Colors.Black, 0.82);
private static Color WxMuted        => WxBlend(WeatherBridge.ThemeAccentSubtle, WxPanel, 0.35);
private static Color WxButtonBg     => WxBlend(WxPillOpaque, WxPanel, 0.55);
private static Color WxButtonBorder => WxBlend(WxPillOpaque, Colors.White, 0.12);
private static Color WxSurface      => Color.FromArgb(0x58, 0x00, 0x00, 0x00);
private static Color WxBorder       => Color.FromArgb(0x48, WeatherBridge.ThemeAccent.R, WeatherBridge.ThemeAccent.G, WeatherBridge.ThemeAccent.B);
```

**Reusable token meanings** (apply these consistently in any new widget):

| Token | Meaning | Typical use |
|---|---|---|
| `WxPanel` | Very dark, wallpaper-tinted base | Window background, behind the blurred wallpaper layer |
| `WxSurface` | Semi-transparent black overlay (`#58000000`) | Card/row backgrounds, buttons at rest |
| `WxBorder` | Semi-transparent accent-tinted border (`#48` + accent RGB) | Borders on cards, buttons, dividers |
| `WxMuted` | Dimmed accent color blended toward panel | Secondary/label text, muted icons |
| `WxButtonBg` / `WxButtonBorder` | Pill-tinted button fill/border | Primary action buttons (Refresh-equivalent) |
| `ThemeAccent` | Bright wallpaper-derived hue | Active tab text, primary highlight text, current-selection border |
| `ThemeAccentSecondary` | Mid-brightness wallpaper hue | Secondary accents (e.g. gradient bottom stop) |
| `ThemeAccentSubtle` | Soft wallpaper hue | De-emphasized labels, calendar day icons |
| `ThemePill` | The homepage's search-pill color | Selection highlights, "today" markers, active states |

**Never hard-code colors like `Color.FromRgb(0x1e, 0x2e, 0x44)` in new widget
code.** Every color should trace back to one of the tokens above, so the
whole app re-themes together when the wallpaper changes.

Hover/rest pairs use plain alpha-black/white overlays rather theme colors, so
they work regardless of hue:
```csharp
var restBrush  = new SolidColorBrush(Color.FromArgb(0x48, 0x00, 0x00, 0x00)); // or 0x28 for alternating rows
var hoverBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
```

---

## 4. Backdrop (blurred, tinted wallpaper)

Every popup gets the same backdrop builder pattern. This exact method can be
copied per-widget (or better, factored into a shared helper once multiple
widgets use it):

```csharp
private (Grid Root, Action Detach) BuildWeatherBackdrop()
{
    var root = new Grid { ClipToBounds = true, IsHitTestVisible = false };
    var baseLayer = new Border();
    var wallLayer = new Border
    {
        RenderTransformOrigin = new Point(0.5, 0.5),
        RenderTransform = new ScaleTransform(1.15, 1.15),
        Effect = new System.Windows.Media.Effects.BlurEffect
        {
            Radius = 32,
            KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
            RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
        }
    };
    var tintLayer = new Border();
    var shadeLayer = new Border
    {
        Background = new LinearGradientBrush(Color.FromArgb(0x60, 0, 0, 0), Color.FromArgb(0xA0, 0, 0, 0), 90)
    };
    root.Children.Add(baseLayer);
    root.Children.Add(wallLayer);
    root.Children.Add(tintLayer);
    root.Children.Add(shadeLayer);

    void Apply()
    {
        baseLayer.Background = new SolidColorBrush(WxPanel);
        var wp = WeatherBridge.ThemeWallpaper;
        if (wp != null)
        {
            wallLayer.Background = new ImageBrush(wp) { Stretch = Stretch.UniformToFill };
            wallLayer.Opacity = Math.Clamp(0.55 * SettingsService.Current.BackgroundOpacity, 0.0, 1.0);
        }
        else
        {
            wallLayer.Background = null;
        }
        var pill = WeatherBridge.ThemePill;
        tintLayer.Background = new SolidColorBrush(Color.FromArgb(0x1C, pill.R, pill.G, pill.B));
    }

    Apply();
    Action handler = () => Dispatcher.BeginInvoke(new Action(Apply));
    WeatherBridge.ThemeUpdated += handler;
    return (root, () => WeatherBridge.ThemeUpdated -= handler);
}
```

Usage inside the popup constructor:
```csharp
var bgGrid = new Grid { ClipToBounds = true };
var backdrop = BuildWeatherBackdrop();
bgGrid.Children.Add(backdrop.Root);
_ = HomePageView.EnsureWeatherThemeAsync(); // fallback if homepage hasn't loaded yet
...
win.Closed += (_, _) => backdrop.Detach();  // always unsubscribe on close
```

**Layer order matters:** solid base → blurred wallpaper (55% opacity ×
`BackgroundOpacity`) → thin pill-color tint (`0x1C` alpha) → dark diagonal
shade for readability. Do not skip the shade layer — it's what keeps text
legible on bright wallpapers.

---

## 5. Window shell / title bar

Every refreshed popup uses the same frameless-window + `WindowChrome`
pattern instead of `WindowStyle.ToolWindow`:

```csharp
win.SizeToContent = SizeToContent.Manual;
win.WindowStyle = WindowStyle.None;
win.ResizeMode = ResizeMode.CanResize;
win.ShowInTaskbar = true;
win.Background = new SolidColorBrush(WxPanel);
System.Windows.Shell.WindowChrome.SetWindowChrome(win, new System.Windows.Shell.WindowChrome
{
    CaptionHeight = 34,
    ResizeBorderThickness = new Thickness(6),
    GlassFrameThickness = new Thickness(0),
    CornerRadius = new CornerRadius(0),
    UseAeroCaptionButtons = false
});
```

Title bar: a 34px `Grid`, semi-transparent black (`#50000000`), with the
widget's title on the left and custom caption buttons (Segoe MDL2 Assets
glyphs) on the right: minimize (`\uE921`), maximize/restore (`\uE922`/`\uE923`),
close (`\uE8BB`, red hover). Any widget-specific toggle buttons (like the
weather pane's `\uE76B`/`\uE76C` chevron) go to the *left* of the standard
three, inside `captionButtons`. Use the `MakeCaptionButton` local-function
pattern from the weather popup — copy it per widget.

**Each caption button must call**
`System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(box, true)`
or clicks won't register (the whole caption area is normally drag-only).

---

## 6. Compact/Wide layout (optional, use if the widget has enough content to benefit)

For widgets with meaningfully different "small" vs "large" content density
(like Weather's Now tab), reuse this pattern:

- A `SettingsService` string property, `"Auto"` / `"Compact"` / `"Wide"`,
  defaulting to `"Auto"`.
- Auto-detection via window width with hysteresis (enter Wide at ~900px,
  exit at ~840px) to avoid flicker while resizing.
- A `ScaleTransform` (~1.12×) applied via `LayoutTransform` on the tab
  content container in Wide mode.
- A title-bar button that cycles the three modes and persists the choice.
- A matching set of entries in the widget's settings right-click menu
  (see `LstWidgetOrder_RightClick` in `MainWindow.xaml.cs` for the existing
  pattern — add a widget-specific `SettingsService` field there rather than
  reusing `WeatherLayoutMode`).

Skip this entirely for simple widgets (Clock, Notes) where there's nothing
meaningfully different between compact and wide — just let the content
reflow naturally instead.

---

## 7. Typical content patterns

These are the recurring visual patterns used inside the Weather popup's
tabs. Reuse whichever fit the new widget's content:

**Monospace stat rows** (`Now` tab pattern) — icon, muted label, value,
optional tooltip, hover highlight:
```csharp
var mono = new FontFamily("Consolas");
// icon: 13px, Segoe UI Emoji, WeatherBridge.ThemeAccentSubtle
// label: 12px, Consolas, WxMuted
// value: 12px, Consolas, White (or a semantic color like temp/UV)
// row: Border, Padding(4,2,4,2), CornerRadius(3), transparent → 0x1AFFFFFF on hover
// optional ToolTip on the row for extra detail
```

**Themed list rows** (`7d`/`16d` tab pattern) — alternating-alpha background,
`WxBorder` border, `CornerRadius(4)`, hover swap between rest/hover brushes,
click opens a detail view.

**Colored badges** (UV badge pattern) — small `CornerRadius(4)` `Border`,
solid semantic-severity color, dark text (`#1A1A1A`) for contrast against the
bright badge fill:
```csharp
private static Color WxUvColor(double uv) =>
    uv < 3  ? Color.FromRgb(0x8B, 0xC3, 0x4A) :
    uv < 6  ? Color.FromRgb(0xF5, 0xC5, 0x18) :
    uv < 8  ? Color.FromRgb(0xF5, 0x8A, 0x1F) :
    uv < 11 ? Color.FromRgb(0xE5, 0x4B, 0x4B) :
              Color.FromRgb(0xB3, 0x6B, 0xFF);
```
Reuse this exact severity ramp (green → yellow → orange → red → violet) for
any other "5-level severity" data another widget might show (e.g. battery
health, signal strength, load).

**Curve/graph** (temperature curve pattern) — `Canvas`-based line chart,
theme-accent stroke, gradient area fill fading to transparent, axis labels
in `WxMuted`, `WxBorder` baseline/border, a highlighted "now" marker dot
when applicable. Wrap in a `Viewbox` with `Stretch.Uniform` so it scales
cleanly with the layout mode.

**Slide-out side pane** (Overcast pane pattern) — a second column in a
window-level `Grid` (not per-tab), `TranslateTransform` + `DoubleAnimation`
slide-in, shared across all tabs of the popup rather than duplicated per tab.
Use this if a widget benefits from an optional detail view that shouldn't
always take up space (e.g. Notes could slide out a full-note editor,
Converter could slide out a history list).

**Embedded calendar** — themed `UniformGrid` day cells, today highlighted
via `WxBlend(WxPillOpaque, WxPanel, 0.35)`, weekend days in `ThemeAccent`,
per-cell icon + secondary line, `sp.Tag`-based load-token guard to prevent
stale async responses from stacking when the user clicks month-nav quickly.

---

## 8. Structural rules (apply to every widget)

1. **No hard-coded hex colors.** Everything traces to a `Wx*` token or
   `WeatherBridge.Theme*`.
2. **No code comments**, per project convention.
3. **One popup window per widget**, frameless + `WindowChrome`, not
   `WindowStyle.ToolWindow`.
4. **Subscribe to `WeatherBridge.ThemeUpdated` and always unsubscribe on
   `win.Closed`** — leaking the subscription keeps the closed window alive
   and slowly leaks memory as popups are reopened.
5. **`EnsureWeatherThemeAsync()`-equivalent fallback** — call it when opening
   the popup so the very first Weather popup opened during a session (before
   the homepage has painted a wallpaper) still gets a theme instead of a
   flat default.
6. **Async loads must be token-guarded** (like `sp.Tag` in the calendar) or
   report success/failure back to the caller (like `LoadForecastTab`
   returning `bool`) so rapid clicking can't leave stale data on screen or
   permanently mark a tab as "loaded" when its fetch actually failed.
7. **Retry affordance on any network-backed tab** — a themed
   `AccentButton("↻  Retry", WxButtonBg, WxButtonBorder, 110)` that re-runs
   the failed load.
8. **Follow the delivery format**: phased plan → patches as (file path,
   new/existing, add/modify, exact Ctrl+F search text, exact replacement
   text) → status/handoff note at the end of each phase, so work can
   resume from a fresh account if needed. Always re-derive search text
   from the user's most recently uploaded current file rather than from
   memory of earlier patches, since earlier phases change line content.

---

## 9. What to send the other AI, per widget

For each widget being refreshed, give it:
1. This document in full.
2. The widget's current implementation (its `Open*Popup()`/`Open*Window()`
   method and any helper methods it calls) from the current
   `MainWindow.xaml.cs` or wherever it lives — pasted or uploaded, not
   summarized, since exact current text is what future patches will need
   to match against.
3. A short description of what should change *visually* (same ask as the
   Weather refresh: frameless window, wallpaper backdrop, theme tokens,
   compact/wide if relevant) and what should change *functionally*, if
   anything — keep these separate, since the goal is usually a visual
   refresh with the existing behavior intact.
4. Confirmation that the same delivery format (phased plan first, then
   patches in the four-field format) applies.
