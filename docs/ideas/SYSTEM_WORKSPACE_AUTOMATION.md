# System Integration & Workspace Automation

This document logs architectural ideas and implementation specifications for system integration, multi-display controls, and network diagnostics in Horizon Browser.

---

## 1. Local System Controls

### 1.1 Terminal Command Launcher
- **Description**: An embedded quick terminal panel passing commands and scripts directly to local PowerShell or CMD without switching window context.
- **Features**:
  - Interactive CLI runner with standard output and error streaming.
  - Preset quick developer diagnostics (`git status`, `ping 1.1.1.1`, `ipconfig /all`, `netstat -ano`, `systeminfo`).
  - Command history navigation (`Up` / `Down` arrows).
  - Clean syntax-highlighted dark glass console output.

### 1.2 Cross-Device Clipboard Bridge
- **Description**: A local, zero-cloud encrypted peer-to-peer network clipboard synchronizer sharing text, links, and screenshots across phone, tablet, and desktop browser instances.
- **Protocol**: Local UDP beacon discovery with AES-GCM encrypted TCP sockets over local Wi-Fi.

### 1.3 Local Drive File Mount
- **Description**: A sidebar hub allowing users to browse, edit, and drag internal drive files straight into web applications without redundant download copies.
- **Security**: Sandboxed directory picker with explicit user permissions and read/write audit logging.

### 1.4 Browser Sandbox Reset Switch
- **Description**: An emergency wipe switch that purges all active session tokens, temporary cookies, cache storage, and resets the browser environment to a completely pristine state.
- **Actions**:
  - Evicts all cached DOM storage, cookies, and local database entries.
  - Reinitializes the active WebView2 environment with clean profile defaults.

---

## 2. Window & Multi-Display Architectures

### 2.1 Display Matrix Router
- **Description**: A visual control grid allowing users to send specific active web tabs onto secondary or tertiary external monitors with exact pixel placement.
- **Win32 API**: `EnumDisplayMonitors`, `GetMonitorInfo`, `SetWindowPos`.
- **Modes**: Snap to Screen 1 (Left/Right/Full), Screen 2 (Left/Right/Full), or Multi-Monitor Tile.

### 2.2 Picture-in-Picture Aspect Lock
- **Description**: Floating web viewport panel with rigid aspect ratio enforcement (16:9, 4:3, 1:1, 21:9) ensuring video and streaming feeds remain perfectly proportioned without letterboxing.

### 2.3 Global Shortcut Key Override
- **Description**: A settings dashboard to intercept and reassign hardcoded system keys to browser macros, hotkeys, and automated tab sequences.
- **Win32 API**: `RegisterHotKey` / `UnregisterHotKey`.

---

## 3. Network, API, & Server Maintenance

### 3.1 SSL/TLS Certificate Checker
- **Description**: A deep analytics panel displaying cryptographic paths, certificate chains, issuer details, validity duration, days until expiration, and cipher suites for the active website.
- **Inspection Details**:
  - Certificate Subject & Organization
  - Issuer (e.g. Let's Encrypt, DigiCert, Cloudflare)
  - Valid From & Expiration Date with countdown meter
  - Subject Alternative Names (SANs)
  - Public Key Algorithm & Key Length (e.g. RSA 2048, ECC P-256)
  - Signature Algorithm (e.g. SHA256withRSA)
  - Negotiated TLS Protocol (TLS 1.3 / TLS 1.2)
  - Negotiated Cipher Suite (e.g. `TLS_AES_256_GCM_SHA384`)

### 3.2 Live WebSocket Stream Inspector
- **Description**: A real-time data terminal capturing incoming and outgoing WebSocket frames (`ws://` and `wss://`) to diagnose live chat apps, stock tickers, and server push events.
- **CDP Protocol**: `Network.webSocketCreated`, `Network.webSocketFrameReceived`, `Network.webSocketFrameSent`.

### 3.3 HTTP Header Manipulator
- **Description**: A control deck enabling users to surgically inject, delete, or override request and response headers (such as `Cookie`, `User-Agent`, `Cache-Control`, `Authorization`, or custom `X-*` headers) on a per-domain basis.
- **Implementation**: CoreWebView2 `WebResourceRequested` filter engine.
