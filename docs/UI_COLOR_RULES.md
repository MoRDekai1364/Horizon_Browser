# UI colour rules (paste to the AI before any UI work)

1. Never hardcode colours (hex or named) for Foreground, Background or BorderBrush in XAML or C#. Use DynamicResource keys from Themes/*.xaml.
2. Text keys: Brush_Text (main), Brush_SoftText, Brush_MutedText (secondary), Brush_DisabledText, Brush_OnAccent (text on accent fills).
3. Do not set Foreground on a TextBlock unless it must differ from Brush_Text. Windows default to Brush_Text.
4. Any new brush key must be added to Horizon.xaml, Light.xaml and Redmond.xaml.
5. Do not write code that fixes text colour for wallpaper or blur. Services/ContrastGuard.cs corrects unreadable text automatically.
6. To exempt one subtree from the guard, add xmlns:svc="clr-namespace:Horizon.Stealth.Services" and svc:ContrastGuard.Mode="Off" on its root.
7. If a new screen has unreadable text, report the CONTRAST log lines instead of repainting by hand.