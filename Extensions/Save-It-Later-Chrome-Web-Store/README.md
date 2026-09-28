# Save It Later Chrome Extension

## 🔒 Configuration Security

### Are API Keys Safe?

**Short Answer**: **Yes, it's safe and standard practice** to expose Firebase API keys and OAuth Client IDs in client-side code. These values are **designed to be public**.

**Why this is safe:**
1. ✅ **Firebase API Key Restrictions**: The API key should be restricted to specific domains/packages in Firebase Console
2. ✅ **OAuth Redirect URI Restrictions**: Client IDs only work with your extension's specific redirect URI (`chrome-extension://[extension-id]`)
3. ✅ **Firestore Security Rules**: All database operations are protected by server-side rules (this is the real security)
4. ✅ **Firebase Authentication**: Users must authenticate before any operations
5. ✅ **No Secrets Exposed**: Client secrets are never in the code (only public client IDs)

**What we did:**
- ✅ Centralized all values in `config.js` (easier to manage)
- ✅ Added clear documentation about what's exposed
- ✅ Made it clear that security comes from restrictions and server-side rules

**See [SECURITY.md](./SECURITY.md) for detailed security information.**

### Configuration File

All sensitive configuration values are centralized in `config.js`:
- Firebase API Key
- Firebase Project ID
- OAuth Client IDs
- Metadata fetching endpoints

This makes it easier to:
- Update values in one place
- Understand what's being exposed
- Add environment-specific configs if needed

### Metadata Fetching

The extension uses a centralized metadata fetcher (`metadata-fetcher.js`) that:
1. **Primary**: Tries Vercel endpoint (`https://save-it-fetching.vercel.app/api/fetch-content`)
2. **Fallback**: Uses Cloud Function (`https://fetchpreview-ztdink6mca-uc.a.run.app/fetchPreview`)

This ensures reliable metadata fetching even if one endpoint is down.

**See [SAVE_FLOW.md](./SAVE_FLOW.md) for detailed step-by-step flow documentation.**

## File Structure

```
chrome-extension/
├── config.js              # Centralized configuration
├── metadata-fetcher.js    # Metadata fetching utility
├── auth.js                # Firebase authentication
├── firestore.js           # Firestore REST API client
├── popup.js               # Popup UI logic
├── bookmarks.js           # Full-screen bookmarks page logic
├── background.js          # Service worker (context menu, auto-save)
├── popup.html             # Popup UI
├── bookmarks.html         # Full-screen bookmarks page
├── popup.css              # Styles
└── manifest.json          # Extension manifest
```

## Script Loading Order

1. `config.js` - Configuration (must load first)
2. `metadata-fetcher.js` - Utilities
3. `auth.js` - Authentication
4. `firestore.js` - Database client
5. `popup.js` / `bookmarks.js` - UI logic
