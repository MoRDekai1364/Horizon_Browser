# Icons Directory

Extension icons are already in place:

- ✅ `icon16.png` (16x16 pixels)
- ✅ `icon32.png` (32x32 pixels)
- ✅ `icon48.png` (48x48 pixels)
- ✅ `icon128.png` (128x128 pixels)

Icons were copied from `assets/IconKitchen-Output/web/` and resized to the required Chrome extension sizes.

If you need to regenerate these icons:
1. Use the icons from `assets/IconKitchen-Output/web/icon-192.png` and `icon-512.png`
2. Resize them using:
   ```bash
   sips -z 16 16 icon-192.png --out icon16.png
   sips -z 32 32 icon-192.png --out icon32.png
   sips -z 48 48 icon-192.png --out icon48.png
   sips -z 128 128 icon-512.png --out icon128.png
   ```

