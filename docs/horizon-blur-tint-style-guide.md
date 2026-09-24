# Horizon Blur & Tint System — Style Guide

This document describes the blurred-wallpaper "glass" backdrop and the
adjustable tint layer used across Horizon Browser (homepage widgets, the
weather popup, and any future header/sidebar/widget). It is written to be
portable: everything here is plain WPF (`System.Windows.Media.Effects`,
`Border`, `SolidColorBrush`) with no Horizon-specific dependency except where
explicitly noted, so it can be copied 1:1 into another WPF project.

---

## 1. Design intent

Two effects, layered together, and kept strictly separate:

- **Blur** — a Gaussian-blurred copy of the wallpaper/background sits behind
  a widget, so the widget reads as translucent glass over whatever is behind
  it. This is a `BlurEffect`, always at full, fixed strength. It is never
  scaled, faded, or made user-adjustable — that's what makes it "the blur
  effect."
- **Tint** — a flat, semi-transparent color layered on top of (or as the
  background alpha of) the widget. This is what makes the glass read as
  "dark glass" or "colored glass" rather than a plain magnifier. Unlike the
  blur, the tint's *opacity* is meant to be user-adjustable: lower tint
  opacity means more of the blurred backdrop shows through; higher tint
  opacity means a more solid-looking widget.

Keeping these two concepts separate is the single most important rule in
this system. A slider that changes "how much tint" must only ever touch
alpha bytes on tint/background colors — never the `BlurEffect.Radius`,
never an element's own `Opacity`, and never anything that would cause the
blur layer itself to become invisible.

---

## 2. The blur layer

Every blurred backdrop in this app uses the same fixed recipe:

```csharp
var wallLayer = new Border
{
    RenderTransformOrigin = new Point(0.5, 0.5),
    RenderTransform = new ScaleTransform(1.08, 1.08),
    Effect = new System.Windows.Media.Effects.BlurEffect
    {
        Radius = 32,
        KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
        RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
    }
};
```

Notes:

- `Radius = 32` is the standard strength used throughout Horizon. Smaller
  inline widgets (menus, context menus) use `Radius = 16` instead — pick one
  of these two, don't introduce a third value without a reason.
- The slight upscale (`ScaleTransform(1.08, 1.08)`, sometimes `1.15`) avoids
  a soft/blurred edge being visible at the border of the element, since blur
  samples outside the element's own bounds.
- `RenderingBias.Performance` matters — without it, large blurred areas can
  visibly tank frame rate on lower-end GPUs.
- This layer's `Background` is set to whatever should be blurred — usually
  an `ImageBrush` of the current wallpaper, or a `VisualBrush` of a live
  video element. It is **never** given a `SolidColorBrush` — a blurred solid
  color is a wasted GPU pass, since blurring a flat color produces the same
  flat color.

This layer's own `Opacity` can be tied to a "how visible is the wallpaper
backdrop" setting (Horizon uses `SettingsService.Current.BackgroundOpacity`
for this) — that is a different knob from tint, and is allowed to be
adjustable, because it controls how much of the *wallpaper* shows through,
not how blurred it is.

---

## 3. The tint layer

A tint is just a `Border` (or the widget's own background) with a
semi-transparent `SolidColorBrush`, painted **on top of** the blur layer:

```csharp
var tintLayer = new Border();
tintLayer.Background = new SolidColorBrush(Color.FromArgb(0x1C, pill.R, pill.G, pill.B));
```

The alpha byte (`0x1C` above) is the only thing a "tint strength" control
should ever touch. Everything else about the color — its hue/RGB — can come
from wherever the widget's theme comes from (a wallpaper-derived accent
color, a fixed brand color, etc.); that's a separate, orthogonal decision
from how transparent the tint is.

### 3.1 Centralizing the alpha math

Rather than scattering `* fraction` math at every call site, put it in one
small helper so every widget's tint reads from the same setting and the
same clamp logic:

```csharp
public static class TintService
{
    public static byte Apply(byte baseAlpha)
    {
        double fraction = Math.Clamp(SettingsService.Current.HomeTintStrength / 100.0, 0.0, 1.0);
        return (byte)Math.Round(baseAlpha * fraction);
    }

    public static Color Apply(Color baseColor)
    {
        return Color.FromArgb(Apply(baseColor.A), baseColor.R, baseColor.G, baseColor.B);
    }
}
```

Every tinted background in the app is then written as:

```csharp
tintLayer.Background = new SolidColorBrush(Color.FromArgb(TintService.Apply((byte)0x1C), pill.R, pill.G, pill.B));
```

or, for a widget whose *entire* background alpha represents its tint (no
separate overlay `Border`):

```csharp
Color pillBg = Color.FromArgb(TintService.Apply(baseAlpha), hueColor.R, hueColor.G, hueColor.B);
```

This is the extension point: any new widget that wants the same
user-adjustable tint just calls `TintService.Apply(...)` on its own base
alpha value. No other wiring is required — it automatically follows
whatever the user has set, because it reads `SettingsService.Current` at
paint time.

### 3.2 Base alpha values in use

| Element | Base alpha (100% tint) | Where |
|---|---|---|
| Search bar / pill background | `0x8C` (dark wallpaper) / `0x99` (light wallpaper) | `HomePageView.xaml.cs`, `ApplyAdaptiveColors` |
| Search bar surface (incl. white variant on light wallpaper) | `0xD9` | `HomePageView.xaml.cs`, `ApplyAdaptiveColors` |
| Weather popup glass tint | `0x1C` | `MainWindow.xaml.cs`, `BuildWidgetBackdrop` |

When adding a new widget, pick a base alpha in this same rough range
(`0x1C`–`0xD9` depending on how "solid" the element should look at 100%
tint) rather than inventing an unrelated scale.

---

## 4. Glass eligibility (which elements get a blur backdrop at all)

Horizon uses an opt-in registration system (`HomeGlassService.cs`,
`HomeGlassInlineLayer`) so that only elements with a genuinely
semi-transparent background get the blur treatment — a fully opaque or
fully invisible background gets nothing, since blurring behind either would
be wasted work or a visible glitch:

```csharp
public static bool IsGlassEligible(FrameworkElement el)
{
    if (HomeGlass.GetExclude(el)) return false;
    if (el.ActualWidth < MinGlassSize || el.ActualHeight < MinGlassSize) return false;
    Brush? bg = el switch
    {
        Border b => b.Background,
        Panel p => p.Background,
        Control c => c.Background,
        _ => null
    };
    return bg is SolidColorBrush scb && scb.Color.A > 0 && scb.Color.A < 255;
}
```

**This is the one place a tint-strength control must be careful never to
push a value to exactly `0` or `255`** — either extreme silently drops the
element from the glass system. `TintService.Apply` naturally avoids `255`
(it only ever scales down), but a slider UI should still floor its minimum
at something like `1` rather than allowing a literal `0`, if the element in
question is also glass-eligible and should keep at least a sliver of tint
visible. Widgets that use a dedicated `tintLayer` `Border` (like the weather
popup) rather than relying on their own background's transparency are not
subject to this rule, since that check only inspects `Background`, not
child layers.

---

## 5. Settings wiring

One `double` setting drives every widget's tint:

```csharp
public double HomeTintStrength { get; set; } = 30.0;
```

- Range: `0`–`100`, read as a percentage of each element's base alpha.
- Exposed in two places, both writing the same property so they stay in
  sync: a live-updating slider in the homepage's own settings window, and a
  save-on-close slider in the browser's global Settings → Appearance tab.
- No widget-specific settings properties should be added for tint — every
  widget reads this one shared value via `TintService`. If a future widget
  genuinely needs an independent tint (rare), give it its own named
  property and its own slider, but don't default to that — the point of
  `TintService` is one dial for the whole app.

---

## 6. Porting checklist (for another project)

To replicate this 1:1 in a different WPF codebase:

1. Copy the `BlurEffect` recipe from §2 verbatim (`Radius`, `KernelType`,
   `RenderingBias`, the upscale transform). Don't make radius adjustable.
2. Add a `TintService` (or equivalent name) with the two `Apply` overloads
   from §3.1, backed by whatever settings/config system the target project
   uses in place of `SettingsService.Current`.
3. Route every semi-transparent widget background/tint layer's alpha byte
   through `TintService.Apply` instead of a literal hex value.
4. If the target project also wants selective glass registration (§4),
   port `IsGlassEligible`'s alpha-bounds check (`> 0 && < 255`) along with
   whatever minimum-size/exclude checks make sense there; otherwise this
   step is optional — plenty of simple blur backdrops (like the weather
   popup's) don't use a registration system at all and just always render.
5. Add one shared "tint strength" setting (0–100, percentage) and expose it
   wherever that project's settings UI lives. Do not create a separate
   setting per widget.
6. Verify: dragging tint to `0` should leave the blur fully intact and
   visible with (almost) no color cast; dragging to `100` should look
   identical to the original, pre-tint-system hard-coded values in §3.2.
