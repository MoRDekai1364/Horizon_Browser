<div align="center">

# 🌌 Horizon Browser

**The Next-Generation Stealth, Speed & Productivity Web Browser for Windows**

*Engineered with .NET 9 / WPF, Microsoft WebView2 Chromium, and a high-performance Native Rust Core.*

<br/>

[![Version](https://img.shields.io/badge/Version-2.7.6%20Beta-7928CA?style=for-the-badge&logo=rocket&logoColor=white)](docs/version.txt)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-0078D4?style=for-the-badge&logo=windows11&logoColor=white)](#installation--quick-start)
[![Framework](https://img.shields.io/badge/.NET-8.0%20%2F%209.0%20%7C%20C%23%2013-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](#architecture--engine-overview)
[![Web Engine](https://img.shields.io/badge/Engine-WebView2%20%2F%20Chromium-00ADB5?style=for-the-badge&logo=googlechrome&logoColor=white)](#architecture--engine-overview)
[![Rust Core](https://img.shields.io/badge/Core-Native%20Rust%20cdylib-DEA584?style=for-the-badge&logo=rust&logoColor=white)](#architecture--engine-overview)
[![UI Design](https://img.shields.io/badge/UI-Fluent%20Acrylic%20%26%20Mica-222831?style=for-the-badge&logo=windows&logoColor=white)](#-key-features)
[![Stealth](https://img.shields.io/badge/Aegis-Shield%20Active-00C853?style=for-the-badge&logo=shield&logoColor=white)](#-aegis-security--multi-account-containers)

<br/>

[✨ Key Features](#-key-features) •
[🔒 Aegis Privacy](#-aegis-security--multi-account-containers) •
[🧩 Fullscreen Widget Board](#-fullscreen-widget-board) •
[⚡ Flux Sidebar](#-sliding-flux-sidebar--workspace-hub) •
[⌨️ Shortcuts](#️-keyboard-shortcuts) •
[🚀 Quick Start](#-installation--quick-start) •
[💬 Feedback](#-feedback--community)

<br/>

<a href="docs/screenshots/01-homepage-hero.png">
  <img src="docs/screenshots/01-homepage-hero.png" alt="Horizon Browser Homepage Hero" width="100%" style="border-radius: 12px; box-shadow: 0 10px 30px rgba(0,0,0,0.4);" />
</a>

*Horizon Browser 2.7.6: Hardware-accelerated Acrylic glass homepage with optical-center search anchoring, dynamic weather, and bottom control island.*

</div>

---

## 💡 About Horizon Browser

**Horizon Browser** redefines desktop browsing on Windows. Unlike standard Chromium repackagings, Horizon blends a native hardware-accelerated **WPF .NET Fluent Acrylic/Mica shell** with a sub-millisecond **Native Rust core engine (`horizon_core.dll`)** and Microsoft WebView2.

Horizon was designed from the ground up for power users who demand:
- 🛡️ **Total Identity & Session Isolation** — Multiple logins to the same service side-by-side using containerized profile sandboxes.
- 💨 **Zero Latency & Memory Governance** — Intelligent 5-tier tab resource governor (*Active → Warm → Dormant → Suspended → Cryo*) that sheds background memory pressure.
- 🎨 **Artisanal Aesthetics** — Real-time acrylic blur glass sampling, 9 customizable visual launcher worlds, spring-physics UI animations, and a top-notch Dynamic Island.
- 🔏 **Stealth & Anti-Fingerprinting** — Cloudflare and anti-bot mitigation, AdGuard tracker blocking, YouTube SponsorBlock bypass, and TikTok guest-wall elimination.

---

## ✨ Key Features

### 🌌 1. Fluent Acrylic Glass Homepage & Dynamic Island
The homepage presents a floating, real-time blurred glass canvas. The search bar is optically locked to the midpoint of the screen, staying immutably anchored across searches, favorites, and widget switching.

- **Persistent Search Row**: Search bar remains frozen at the optical center while content islands smoothly expand downward.
- **Dynamic Island Top-Notch**: A draggable, flush status capsule anchored to the screen ceiling that reveals active media, tab state, and quick shortcuts, with automatic click-outside collapse.
- **Dynamic Weather & Clock**: Real-time atmospheric conditions, pressure, humidity, and clock rendering with sub-pixel typography.
- **Bottom Island Dock**: Centralized pill hosting VPN bypass toggles, system battery health, live wallpaper switchers, and quick settings.

<div align="center">
  <a href="docs/screenshots/06-active-browsing-dynamic-island.png">
    <img src="docs/screenshots/06-active-browsing-dynamic-island.png" alt="Active Browsing and Dynamic Island" width="100%" style="border-radius: 10px; margin-top: 8px;" />
  </a>
  <p><em>Active browsing view showing header tabs, seamless WebView2 rendering, and the integrated Dynamic Island status capsule.</em></p>
</div>

---

### 🔒 Aegis Security & Multi-Account Containers
Tired of opening incognito windows or secondary browsers just to log into multiple accounts? Horizon features deep **`CoreWebView2Profile` Container Tab Isolation**.

- **Multi-Account Container Tabs**: Right-click any tab to isolate it into **Work**, **Personal**, **Finance**, **Shopping**, or a **Custom** named container. Each container maintains strictly isolated cookies, localStorage, indexedDB, and HTTP cache.
- **Simultaneous Multi-Session**: Run multiple accounts on Google, GitHub, Claude, Discord, or AWS side-by-side without interference.
- **High-Contrast Container Badging**: Visual container pills with distinct color accents render directly on tabs.
- **Built-in Shields**: Integrated AdGuard tracker blocking, YouTube SponsorBlock bypass, TikTok guest modal unblocking, and referrer header stripping.

<div align="center">
  <a href="docs/screenshots/04-aegis-security-containers.png">
    <img src="docs/screenshots/04-aegis-security-containers.png" alt="Aegis Security & Container Tab Suite" width="100%" style="border-radius: 10px; margin-top: 8px;" />
  </a>
  <p><em>Aegis Security & Privacy Suite: Multi-account container isolation, anti-fingerprinting shields, and platform bypasses.</em></p>
</div>

---

### 🧩 Fullscreen Widget Board
Switch to **Widgets** mode to open the fullscreen Acrylic glass command center. Horizon incorporates an interactive 4-column matrix containing 15 dynamic productivity and diagnostic cards:

| Widget | Capabilities |
|---|---|
| 🕒 **World Clock & Time** | Multiple timezones, UTC offsets, and digital/analog display. |
| 🌦️ **Atmospheric Weather** | Real-time temperature, humidity, atmospheric pressure, and forecast. |
| ⚡ **Aegis RAM Governor** | Live browser process-tree memory auditing powered by the native Rust core. |
| 📝 **Quick Notes** | Instant scratchpad with auto-save, word counter, and character counter. |
| 📐 **Unit & Currency Converter** | Live length, weight, temperature, and currency conversion. |
| 🧮 **Scientific Calculator** | Interactive keypad with instant mathematical evaluation. |
| 🔋 **Battery & Power Status** | Win32 power telemetry, battery percentage, charging health, and power state. |
| 📅 **Calendar & Event Planner** | Full agenda, date interval calculator, age calculator, and event tracking. |

<div align="center">
  <a href="docs/screenshots/02-widget-board.png">
    <img src="docs/screenshots/02-widget-board.png" alt="Fullscreen Acrylic Widget Board" width="100%" style="border-radius: 10px; margin-top: 8px;" />
  </a>
  <p><em>Fullscreen Windows 11 Widget Board: 15 interactive cards rendered over dynamic acrylic glass.</em></p>
</div>

<div align="center">
  <table>
    <tr>
      <td align="center" width="50%">
        <img src="docs/screenshots/widget-converter.png" alt="Interactive Converter Widget" width="380" style="border-radius: 8px;" />
        <br/><em>Interactive Unit & Currency Converter</em>
      </td>
      <td align="center" width="50%">
        <img src="docs/screenshots/widget-notes.png" alt="Auto-saving Quick Notes Widget" width="380" style="border-radius: 8px;" />
        <br/><em>Auto-saving Scratchpad & Word Counter</em>
      </td>
    </tr>
  </table>
</div>

---

### 🔖 Interactive Acrylic Bookmarks & Favorites Island
Say goodbye to cluttered bookmark bars. Horizon groups your bookmarks and favorites into an organic **puzzle-block matrix**:

- **Domain-Grouped Bookmarks**: Bookmarks automatically organize by web domain into sleek glass cards.
- **Puzzle-Block Packing**: Cards dynamically scale and pack together based on item count with zero wasted space.
- **5 Folder Presentation Styles**: Choose between *Header Only*, *First Row*, *Stacked Preview*, *Mini Folder*, and *Full List*.
- **Spring Popup Physics**: Silky-smooth opening and closing transitions powered by asymptotic spring curves (`diff * 0.22`).

<div align="center">
  <a href="docs/screenshots/03-favorites-bookmarks.png">
    <img src="docs/screenshots/03-favorites-bookmarks.png" alt="Bookmarks and Favorites Acrylic Island" width="100%" style="border-radius: 10px; margin-top: 8px;" />
  </a>
  <p><em>Favorites & Bookmarks Island: Puzzle-packed domain blocks with dynamic acrylic blur.</em></p>
</div>

---

### ⚡ Sliding Flux Sidebar & Workspace Hub
The **Flux Sidebar** is an extensible slide-out workspace hub that adapts to your workflow:

- **Spring-Decelerated Slide**: Ported asymptotic physics formula running on a 16ms render loop delivers 60 FPS opening and zero-snap reversal.
- **Categorized Tab Management**: Group tabs into color-coded categories, panes, and workspaces.
- **Integrated File Operations**: View recent downloads with native cut, copy, paste, and open operations directly from the sidebar.
- **Homepage Overlay Mode**: Floats over the homepage with unmirrored acrylic glass without pushing or disturbing centered widgets, automatically transitioning to push mode on web pages.

<div align="center">
  <a href="docs/screenshots/05-flux-sidebar.png">
    <img src="docs/screenshots/05-flux-sidebar.png" alt="Sliding Flux Sidebar" width="100%" style="border-radius: 10px; margin-top: 8px;" />
  </a>
  <p><em>Sliding Flux Sidebar: Tab categories, navigation panes, and quick-access utility tools.</em></p>
</div>

---

### 📅 Calendar & Productivity Suite
Horizon includes a built-in calendar and date-math suite accessible directly from the homepage or widget board:

- Full monthly calendar view with event tagging.
- **Date Calculator**: Calculate exact duration between two dates in days, weeks, and months.
- **Age Calculator**: Precise date-of-birth age and milestone tracker.

<div align="center">
  <a href="docs/screenshots/07-calendar-productivity.png">
    <img src="docs/screenshots/07-calendar-productivity.png" alt="Calendar and Date Calculator Suite" width="100%" style="border-radius: 10px; margin-top: 8px;" />
  </a>
  <p><em>Integrated Calendar, Date Math, and Event Planning Suite.</em></p>
</div>

---

### 🎨 Homepage Customizer & Multi-World Themes
Personalize every facet of your browsing environment:

- **9 Visual Launcher Worlds**:
  - 🌌 *Horizon Flow* (Default Fluent Glass)
  - ⚡ *CyberTerminal HUD*
  - 🔲 *Metro Matrix*
  - 🦀 *Rust Neo-Canvas*
  - 📺 *Retro CRT Cyberdeck*
  - 📰 *Editorial Paper*
  - 🚀 *Starship LCARS*
  - 🌸 *Zen Garden*
  - 💾 *Win95 RetroDesk*
- **Glass & Tint Controls**: Fine-tune glass opacity, acrylic noise intensity, border glow, and background wallpaper blurring.

<div align="center">
  <a href="docs/screenshots/08-homepage-customizer.png">
    <img src="docs/screenshots/08-homepage-customizer.png" alt="Homepage Customizer" width="100%" style="border-radius: 10px; margin-top: 8px;" />
  </a>
  <p><em>Homepage Settings: Instant switching between 9 visual worlds and glass styling options.</em></p>
</div>

---

## 🏛️ Architecture & Engine Overview

Horizon Browser leverages a hybrid three-tier architecture:

```
┌─────────────────────────────────────────────────────────────────┐
│                    HORIZON BROWSER ARCHITECTURE                 │
├─────────────────────────────────────────────────────────────────┤
│  PRESENTATION LAYER (C# 13 / .NET 9 WPF)                        │
│  • MainWindow Shell & Dynamic Island Capsule                     │
│  • HomePageView with Fluent Acrylic Glass & 9 Visual Worlds     │
│  • Flux Sidebar with Asymptotic Spring Physics                  │
│  • Fullscreen 4x4 Acrylic Widget Board Matrix                   │
├─────────────────────────────────────────────────────────────────┤
│  NATIVE ACCELERATION CORE (Rust cdylib: horizon_core.dll)       │
│  • Sub-millisecond Process-Tree Memory Auditing                 │
│  • SIMD Wallpaper Luminance & Contrast Sampling                 │
│  • Zero-Allocation Sub-Pixel Dock & Grid Coordinate Solver      │
├─────────────────────────────────────────────────────────────────┤
│  WEB PLATFORM ENGINE (Microsoft WebView2 Chromium)              │
│  • CoreWebView2Profile Multi-Account Container Sandboxing       │
│  • Aegis Tab Governor (Active → Warm → Dormant → Cryo)          │
│  • Chrome DevTools Protocol (CDP) Capture Pipeline              │
│  • Stealth Anti-Fingerprinting & Client Hints Sanitation        │
└─────────────────────────────────────────────────────────────────┘
```

### Aegis Resource Manager Tab Governor
To maintain lightning-fast responsiveness and avoid Chromium RAM bloat, Horizon's tab governor automatically transitions background tabs through 5 distinct lifecycle tiers:
1. **Active**: Full rendering, high priority, audio/video playback preserved.
2. **Warm**: Throttled DOM timers and background JavaScript.
3. **Dormant**: State cached, memory footprint trimmed.
4. **Suspended**: Serialized DOM tree; execution paused until tab re-selection.
5. **Cryo**: Complete memory release with instant state-restoration token.

---

## ⌨️ Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| <kbd>Ctrl</kbd> + <kbd>T</kbd> | Open new tab |
| <kbd>Ctrl</kbd> + <kbd>W</kbd> | Close active tab |
| <kbd>Ctrl</kbd> + <kbd>Tab</kbd> | Open topmost visual **TabSwitcher** with live CDP previews |
| <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>Tab</kbd> | Cycle backward through tabs |
| <kbd>Ctrl</kbd> + <kbd>1</kbd> .. <kbd>9</kbd> | Jump directly to tab 1 through 9 |
| <kbd>Ctrl</kbd> + <kbd>H</kbd> | Open browsing history |
| <kbd>Ctrl</kbd> + <kbd>J</kbd> | Open downloads drawer in Flux Sidebar |
| <kbd>Ctrl</kbd> + <kbd>B</kbd> | Toggle Flux Sidebar |
| <kbd>Ctrl</kbd> + <kbd>L</kbd> / <kbd>Alt</kbd> + <kbd>D</kbd> | Focus URL / Search address bar |
| <kbd>F11</kbd> | Toggle cinematic glass curtain Fullscreen mode |
| <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>I</kbd> | Inspect element / Chromium DevTools |
| <kbd>Ctrl</kbd> + <kbd>+</kbd> / <kbd>-</kbd> / <kbd>0</kbd> | Zoom in, zoom out, reset zoom |

---

## 🚀 Installation & Quick Start

> [!NOTE]
> Horizon Browser runs on **Windows 10 / 11 (64-bit)** with the [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (pre-installed on Windows 11).

### Option 1: Official Release Package (Recommended)
1. Download the latest **`Horizon_Browser_official_release.zip`** from Releases.
2. Extract the archive to your preferred directory (e.g. `C:\Program Files\Horizon` or `~/AppData/Local/Horizon`).
3. Run **`Horizon.exe`** (or use the included desktop shortcut).

### Option 2: Build From Source
Ensure you have the [.NET 9.0 SDK](https://dotnet.microsoft.com/download) and [Visual Studio 2022](https://visualstudio.microsoft.com/) / modern Windows C++ build tools installed.

```powershell
# 1. Clone repository
git clone https://github.com/dmeta/horizon-browser.git
cd horizon-browser

# 2. Build Release configuration
dotnet build -c Release "Horizon.Browser.csproj"

# 3. Launch Horizon Browser
& "bin\Release\Horizon.Browser.exe"
```

---

## 🗺️ Roadmap & Upcoming Milestones

- [x] Multi-Account Container Tabs & Profile Isolation (`CoreWebView2Profile`)
- [x] Fullscreen 15-Card Acrylic Glass Widget Board
- [x] Sliding Flux Sidebar with Asymptotic Spring Physics
- [x] 9-World Multi-Theme Homepage Launchers
- [x] Native Rust acceleration core (`horizon_core.dll`)
- [x] TikTok login wall and YouTube SponsorBlock bypass
- [ ] Automated Chrome Web Store extension installer flow
- [ ] End-to-end encrypted tab and session synchronization
- [ ] Dedicated MSIX / InnoSetup packaging installer
- [ ] Split-view dual-tab browsing layout

---

## 💬 Feedback & Community

We are actively shaping Horizon Browser based on user feedback.

- 📝 **Submit Ideas & Bug Reports:** [Horizon Feedback Portal](https://feedbackcol-nszemvwz.manus.space/)
- 🐞 **GitHub Issues:** [Open an Issue](https://github.com/dmeta/horizon-browser/issues)

---

<div align="center">
  <sub>Built with ❤️ for Windows power users. Horizon Browser is licensed under custom distribution terms.</sub>
</div>
