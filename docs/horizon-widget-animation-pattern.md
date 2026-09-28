# Horizon Header Widget — Animation Pattern Guide

This document describes the animation system already used by the browser's
header widget box (`HeaderWidgetBorder` / `TxtWidget` in `MainWindow.xaml.cs`)
so it can be reused, unmodified in behavior, by other parts of the app — the
Weather popup included.

Everything below reflects code that already exists and compiles in
`MainWindow.xaml.cs`. This is a reference for reuse, not a proposal to change
the header widget itself.

---

## 1. What this pattern is for

The header widget box shows a single line of text (`TxtWidget`) whose content
changes as the active mode/data changes (Clock, Weather, Media, Battery,
etc.), and whose container (`HeaderWidgetBorder`) is a fixed-height pill that
needs to resize horizontally to fit that content. Three independent
animations cooperate to make that feel seamless:

1. **Width animation** — the pill smoothly grows/shrinks to a new target
   width instead of snapping.
2. **Content fade** — the text cross-fades out/in instead of popping when the
   underlying value changes.
3. **Marquee scroll** — when text is too wide for the pill and the pill isn't
   allowed to grow to fit it, the text scrolls horizontally in a loop instead
   of being clipped or ellipsized.

They are independent, composable primitives — a caller can use any one of
them without the others (e.g. the Weather popup can reuse the width
animation for panel slides without touching the marquee).

---

## 2. Width animation: `AnimateWidgetWidth`

```csharp
private void AnimateWidgetWidth(double targetWidth)
{
    if (HeaderWidgetBorder == null) return;
    if (Math.Abs(HeaderWidgetBorder.Width - targetWidth) < 1.0) return;
    var anim = new DoubleAnimation(targetWidth,
        new Duration(TimeSpan.FromMilliseconds(300)))
    {
        EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
    };
    HeaderWidgetBorder.BeginAnimation(FrameworkElement.WidthProperty, anim);
}
```

**Parameters that define the feel:**

| Property | Value |
|---|---|
| Duration | 300ms |
| Easing | `SineEase`, `EaseInOut` |
| Animated property | `FrameworkElement.WidthProperty` |
| No-op guard | skip entirely if `\|current - target\| < 1.0` px |

**Why the guard matters:** without it, every call re-triggers a fresh
`DoubleAnimation` even when the target is effectively unchanged (e.g. two
`RefreshWidgetDisplay()` calls in quick succession with the same text),
which restarts the animation curve and produces a visible stutter. The 1px
tolerance is deliberately loose — it's not a precision threshold, it's a
"don't bother re-animating" threshold.

**Reusable as-is** for any container's `Width`: caller just needs a target
width in pixels. Target width is always computed by the caller (see
`UpdateWeatherWidgetWidth`, `UpdateMediaWidgetWidth`), never by this method —
this function knows nothing about text measurement, mode, or content. Keep
that separation when reusing it: measurement/decision logic stays with the
caller, `AnimateWidgetWidth` (or its renamed equivalent) stays a pure
"animate to this width" primitive.

**How targets are computed today** (for reference, not required elsewhere):
`FormattedText` is measured against the current `TxtWidget` font/size, padded
(`+24`px, 12px each side), then clamped between `_widgetDefaultWidth` (146px)
and a max (320px). Any reuse elsewhere should define its own min/max clamp
appropriate to that context rather than reusing these constants.

---

## 3. Content fade: `FadeWidgetContent`

```csharp
private void FadeWidgetContent(string newText)
{
    if (_widgetFading) { TxtWidget.Text = newText; return; }
    _widgetFading = true;
    var fadeOut = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(300)));
    fadeOut.Completed += (s, e) =>
    {
        TxtWidget.Text = newText;
        var fadeIn = new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromMilliseconds(300)));
        fadeIn.Completed += (s2, e2) => _widgetFading = false;
        TxtWidget.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    };
    TxtWidget.BeginAnimation(UIElement.OpacityProperty, fadeOut);
}
```

**Sequencing — this is the important part to preserve on reuse:**

1. Fade `Opacity` 1.0 → 0.0 over 300ms.
2. **Only once that animation's `Completed` fires**, swap the underlying
   text/content (the swap is invisible — opacity is already 0).
3. Fade `Opacity` 0.0 → 1.0 over 300ms.
4. On that second animation's `Completed`, clear the in-progress flag.

Total visible transition: ~600ms (300 out + 300 in), with the actual content
swap hidden inside the invisible midpoint. No easing function is set on
either half (linear opacity ramp) — this is a deliberate contrast with the
width animation's `SineEase`; a fade doesn't need easing to read as smooth
the way a size change does.

**The `_widgetFading` re-entrancy guard:** if a new fade is requested while
one is already in flight, the pattern does **not** queue or restart the
animation — it just snaps the text directly (`TxtWidget.Text = newText`) and
leaves the in-flight fade to finish on its own. This prevents overlapping
`Completed` handlers from fighting over the text value or double-toggling
the flag. When reusing this pattern for rapid-fire updates (e.g. a live stat
row), keep this guard — the alternative (letting fades stack) produces
flicker, not smoothness.

**Reusable as-is** for any single `TextBlock`/`UIElement`'s content swap.
Needs one bool field per independent fade target (its own `_xFading`
equivalent) — do not share one flag across multiple simultaneously-fading
elements, or unrelated fades will block each other.

---

## 4. Marquee scroll: `StartWidgetMarquee` / `MarqueeTimer_Tick` / `StopWidgetMarquee`

```csharp
private void StartWidgetMarquee(string fullText)
{
    if (string.IsNullOrEmpty(fullText)) return;
    TxtWidget.Text = fullText;
    TxtWidget.HorizontalAlignment = HorizontalAlignment.Left;
    TxtWidget.Margin = new Thickness(6, 0, 0, 0);

    var ft = new System.Windows.Media.FormattedText(
        fullText, System.Globalization.CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight,
        new Typeface(TxtWidget.FontFamily, TxtWidget.FontStyle,
                     TxtWidget.FontWeight, TxtWidget.FontStretch),
        TxtWidget.FontSize, Brushes.White,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    _marqueeTextWidth = ft.Width;
    double containerW = _widgetDefaultWidth - 12;

    if (_marqueeTextWidth <= containerW)
    {
        TxtWidget.HorizontalAlignment = HorizontalAlignment.Center;
        TxtWidget.Margin = new Thickness(0);
        return;
    }

    TxtWidget.RenderTransform = new TranslateTransform(0, 0);
    _marqueeOffset    = -40;
    _isMarqueeRunning = true;

    if (_marqueeTimer == null)
    {
        _marqueeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _marqueeTimer.Tick += MarqueeTimer_Tick;
    }
    _marqueeTimer.Start();
}

private void MarqueeTimer_Tick(object? sender, EventArgs e)
{
    _marqueeOffset += 1.5;
    double travel = _marqueeTextWidth - (_widgetDefaultWidth - 12);
    if (_marqueeOffset > travel + 50) _marqueeOffset = -40;
    if (TxtWidget?.RenderTransform is TranslateTransform tt)
        tt.X = -Math.Max(0, _marqueeOffset);
}

private void StopWidgetMarquee()
{
    _marqueeTimer?.Stop();
    _isMarqueeRunning = false;
    _marqueeOffset    = 0;
    if (TxtWidget == null) return;
    TxtWidget.RenderTransform     = Transform.Identity;
    TxtWidget.HorizontalAlignment = HorizontalAlignment.Center;
    TxtWidget.Margin              = new Thickness(0);
}
```

**Mechanics:**

| Property | Value |
|---|---|
| Tick interval | 30ms (`DispatcherTimer`, not a `DoubleAnimation`) |
| Speed | 1.5px per tick (= 50px/sec) |
| Start offset | `-40` (a leading pause before text enters from the left) |
| Loop point | resets to `-40` once offset exceeds `travel + 50` (travel = text width − container width) — a trailing pause before it loops |
| Movement | `TranslateTransform.X = -max(0, offset)`, i.e. text is pushed left |

**Fit check comes first, always:** before starting any scroll, the text is
measured with `FormattedText` against the actual current font/size. If it
already fits the container, the marquee **does not start at all** — the
element is just centered normally. The scrolling path is only for the
overflow case. Don't skip this check when reusing the pattern; a marquee
that runs on text that already fits looks like a bug, not a feature.

**Why a timer instead of a `DoubleAnimation`:** the loop is open-ended
(unknown/variable text length, indefinite duration) and needs a manual reset
point mid-loop, which is awkward to express as a single WPF animation with a
fixed `Duration`. A ticking timer that mutates a transform property directly
is the simpler tool for an indefinitely-looping, self-resetting scroll.

**Cleanup:** `StopWidgetMarquee` fully reverses `StartWidgetMarquee`'s setup
— stops the timer, resets the offset, resets the transform to `Identity`,
and restores centered alignment with no margin. Always pair a `Start` call
path with a corresponding `Stop` call path (mode switches away from the
widget, popup closes, etc.) or the timer keeps ticking against a
now-irrelevant element.

**Reusable as-is** for any fixed-width single-line text element that
occasionally overflows. Needs its own timer instance, offset field, and
text-width field per independent marquee (don't share the module-level
`_marqueeTimer`/`_marqueeOffset`/`_marqueeTextWidth` fields across more than
one simultaneously-scrolling element).

---

## 5. How the three compose (orchestration reference)

`RefreshWidgetDisplay()` is the call site that decides, per update, which of
the three to invoke and in what order. The decision logic itself is specific
to the header widget's modes (Clock/Weather/Media/Battery) and not part of
the reusable pattern, but the **shape** of the decision is worth carrying
over to any new caller:

1. Decide whether the new content needs a fade (content changed) — if so,
   call the fade primitive; it internally guards against double-fades.
2. Decide the target width for the new content (measure, clamp) — call the
   width-animation primitive; it internally guards against no-op re-animation.
3. Decide whether the content should marquee instead of fade (fixed
   container, overflowing text, and a user setting allowing it) — call the
   marquee primitive instead of the fade for that case, not in addition to
   it.

Fade and marquee are mutually exclusive for the same element at the same
time (marqueeing text doesn't also cross-fade its swaps); width animation is
independent of both and can run alongside either.

---

## 6. Applying this to the Weather popup (Phase 18 scope — not implemented yet)

This section is guidance for later, not a description of current popup code.
The popup's existing pane-slide/resize code (`TranslateTransform` +
`DoubleAnimation` on `rightCol.Width` / `paneSlide.X`, described in
`horizon-widget-style-guide.md` §7) currently uses ad hoc durations/easing
that don't match this pattern. When Phase 18 replaces them:

- **Panel open/close slides** (left/right pane) map to the **width
  animation** shape: a `DoubleAnimation` to a target extent with
  `SineEase`/`EaseInOut` at 300ms, with the same "skip if already at target"
  guard — not the marquee's timer-based approach, since panel width has a
  single fixed target, not an open-ended scroll.
- **Window resizing between layout modes** (Compact ↔ Wide) maps to the same
  width-animation shape, targeting `win.Width`/`win.Height` instead of
  `HeaderWidgetBorder.Width`.
- **Tab content swaps**, if they should visually cross-fade rather than cut,
  map to the **fade** shape (300ms out, swap, 300ms in, re-entrancy guard) —
  currently tab switches in the popup just toggle `Visibility`, so adopting
  this would be a visible behavior change and should be confirmed with the
  user before Phase 18 patches it in.
- The popup has no marquee use case today (no fixed-width overflowing text
  element) — the marquee primitive is included here for completeness and for
  reuse by other widgets (per the style guide's Notes/Calculator/etc.
  extension list), not because the Weather popup needs it.

---

## 7. Structural rules (apply wherever this pattern is reused)

1. **Keep the three primitives independent.** A caller should be able to use
   width-animation without fade, fade without marquee, etc.
2. **One state field set per independent instance.** Don't share
   `_widgetFading`-, `_marqueeTimer`-, or `_isMarqueeRunning`-equivalent
   fields across more than one element that might animate concurrently.
3. **No easing on fades, `SineEase`/`EaseInOut` on width animations.** This
   split is intentional, not an oversight — preserve it.
4. **Always guard against redundant re-animation** (width: `<1px` delta
   skip; fade: in-flight snap-instead-of-restart).
5. **Always pair start/stop lifecycles** for the marquee (and for the
   `ThemeUpdated`-style subscriptions the style guide already requires) so a
   closed popup/hidden element doesn't leave a ticking timer behind.
6. **No code comments**, per project convention — this applies to any new
   code adopting the pattern, not to this document.
7. **Follow the delivery format**: phased plan → patches as (file path,
   new/existing, add/modify, exact Ctrl+F search text, exact replacement
   text) → status/handoff note at the end of each phase.
