# Horizon Browser - Ideas Registry & Feature Backlog

Master inventory of proposed browser utilities, local automation tools, and research instruments.

| ID | Title | Category | Status | Primary Component |
|---|---|---|---|---|
| `citation_builder` | **Instant Citation Builder** | Academic & Research | 🟢 **Implemented** | `Views/CitationBuilderWindow.cs` |
| `doi_resolver` | **DOI / Paywall Resolver** | Academic & Research | 🟢 **Implemented** | `Views/DoiResolverWindow.cs` |
| `isbn_tracker` | **ISBN Tracker & Price Evaluator** | Academic & Research | 🟡 Catalog Registered | `Services/UtilitiesService.cs` |
| `abstract_summarizer` | **Abstract Summarizer Panel** | Academic & Research | 🟡 Catalog Registered | `Services/UtilitiesService.cs` |
| `knowledge_graph` | **Knowledge Graph Matrix** | Academic & Research | 🟡 Catalog Registered | `Services/UtilitiesService.cs` |
| `pdf_annotator` | **Inline PDF Annotator Layer** | Academic & Research | 🟡 Catalog Registered | `Services/UtilitiesService.cs` |
| `plagiarism_checker` | **Plagiarism Checker Engine** | Academic & Research | 🟡 Catalog Registered | `Services/UtilitiesService.cs` |
| `text_miner` | **Text Mining & Keyword Density Plotter** | Academic & Research | 🟢 **Implemented** | `Views/TextMinerWindow.cs` |
| `terminal_launcher` | **Terminal Command Launcher** | System & Network | 🟢 **Implemented** | `Views/TerminalLauncherWindow.cs` |
| `clipboard_bridge` | **Cross-Device Clipboard Bridge** | System & Network | 🟡 Catalog Registered | `Services/UtilitiesService.cs` |
| `local_drive_mount` | **Local Drive File Mount** | System & Network | 🟡 Catalog Registered | `Services/UtilitiesService.cs` |
| `sandbox_reset` | **Browser Sandbox Reset Switch** | System & Network | 🟢 **Implemented** | `Services/UtilitiesService.cs` |
| `display_matrix` | **Display Matrix Router** | System & Network | 🟡 Catalog Registered | `Services/UtilitiesService.cs` |
| `pip_aspect_lock` | **Picture-in-Picture Aspect Lock** | System & Network | 🟡 Catalog Registered | `Services/UtilitiesService.cs` |
| `shortcut_override` | **Global Shortcut Key Override** | System & Network | 🟡 Catalog Registered | `Services/UtilitiesService.cs` |
| `ssl_checker` | **SSL/TLS Certificate Checker** | System & Network | 🟢 **Implemented** | `Views/SslCertificateWindow.cs` |
| `websocket_inspector` | **Live WebSocket Stream Inspector** | System & Network | 🟡 Catalog Registered | `Services/UtilitiesService.cs` |
| `header_manipulator` | **HTTP Header Manipulator** | System & Network | 🟡 Catalog Registered | `Services/UtilitiesService.cs` |

---

## Architectural Notes
- All utilities implement the `UtilityItem` standard in `Services/UtilitiesService.cs`.
- Utilities support **Multi-Destination Routing**:
  1. Add to Favorites dock
  2. Add to Bookmarks folder
  3. Pin to Header right-click context menu
  4. Pin to Homepage Widget Board as live interactive card
- Flagship utilities feature dedicated WPF high-contrast acrylic dark glass modals with sub-millisecond execution times.
