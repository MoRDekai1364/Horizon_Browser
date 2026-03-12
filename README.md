# Horizon Browser

A custom WPF/WebView2 browser focused on stealth, speed, and a clean UI.

> **Status:** Functional — active development & debugging in progress.

**Feedback & contact:** https://feedbackcol-nszemvwz.manus.space/

---

<img width="480" height="270" alt="image" src="https://github.com/user-attachments/assets/f9dac41e-128a-4ce7-97c6-3c05f66a37d5" />
<img width="480" height="270" alt="image" src="https://github.com/user-attachments/assets/24f5ff7f-3401-48f0-8f4f-ee7477bc90b6" />
<img width="480" height="270" alt="image" src="https://github.com/user-attachments/assets/19e61c5b-52fe-4944-be49-96eb1b334320" />
<img width="480" height="270" alt="image" src="https://github.com/user-attachments/assets/60ef0e6b-1755-4e07-b5e2-cd70907bcd06" />

---

## Installation

> ⚠️ **Temporarily disabling your antivirus is highly recommended before installation.**

### Recommended — Full release zip
1. Download **`Horizon_Browser_official_release.zip`**
2. Unarchive to a location of your choice
3. Launch via the **`Horizon_Browser`** shortcut

### Alternative — Executable only
1. Download **only** the release files containing **`.exe` and `.pak`**
2. Make sure they are in the same folder
3. Run the **`Horizon.START`** shortcut
   - If the shortcut doesn't work, run directly: `bin/release/Horizon.Stealth.exe`

> ⚠️ The alternative method includes a setup file that may be flagged by some antivirus software.

---

## Updating

Before updating to a new version, run **`Update_manager.bat`** and follow the on-screen instructions.

---

## Known Issues

> *The number of ⛔ symbols indicates how soon an issue will be fixed.*

| | Issue |
|---|---|
| ⛔ | Secondary installation method may be blocked by antivirus software |
| ⛔ | Bot detection triggered on some websites (e.g. Claude) due to the custom browser fingerprint |
| ⛔⛔⛔ | Per-tab language UI switches correctly, but does not change the actual keyboard layout |
| ⛔⛔⛔ | Sleeping tabs display `about:blank` instead of a "Sleeping" placeholder title |

---

## Planned Features

> *The number of ⭐ symbols indicates priority.*

| | Feature |
|---|---|
| ⭐⭐⭐ | Move items between Favourites and Bookmarks via right-click context menus in both tabs |
| ⭐⭐ | Automated Chrome Web Store extension install flow |
| ⭐⭐ | Dedicated installer |
| ⭐ | Vastly improved customisability |

---

## Recently Added

- Sleeping tabs
- Drag & drop support for sidebar and tabs
- Sidebar categories for tab management
- Tab titles show domain only by default, with full title revealed on hover — reversible in settings
- Right-click forward/backward media buttons to skip tracks *(note: backward → previous track not yet working)*
- Copy, preview, cut & paste file operations in the downloads sidebar
- Shift-click / Ctrl-click tab multi-selection
- Improved cookie blocking
