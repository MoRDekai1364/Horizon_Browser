# Save Bookmark Flow - Step by Step

## 🔒 Security Status

### Are API Keys Safe?

**Short Answer**: The API keys are **still visible** in the extension bundle (this is unavoidable for client-side apps), but they are **protected** through Firebase's security mechanisms.

**Why this is safe:**
1. ✅ **Firebase API Key Restrictions**: The API key is restricted to specific domains/packages in Firebase Console
2. ✅ **OAuth Redirect URI Restrictions**: Client IDs only work with your extension's specific redirect URI
3. ✅ **Firestore Security Rules**: All database operations are protected by server-side rules
4. ✅ **Authentication Required**: Users must authenticate before any operations

**What we did:**
- ✅ Centralized all sensitive values in `config.js` (easier to manage)
- ✅ Added clear documentation about what's exposed
- ✅ Made it clear that security comes from restrictions, not hiding values

---

## 📋 Step-by-Step: Save Bookmark Flow

### When User Clicks "Save Bookmark" Button

#### **Step 1: Initial Validation** 
```
User clicks "Save Bookmark" button
  ↓
Check if user is authenticated
  ↓
Get current tab URL
  ↓
Validate URL format (must be http/https)
```

**Code Location**: `popup.js` line ~310-337

---

#### **Step 2: Extract Basic Info**
```
Extract domain from URL
  ↓
Use tab title as default title
  ↓
Initialize description and imageUrl as empty/null
```

**Code Location**: `popup.js` line ~327-339

---

#### **Step 3: Fetch Metadata (Title, Description, Image)**

**Primary: Vercel Endpoint**
```
Try: POST https://save-it-fetching.vercel.app/api/fetch-content
  ↓
Body: { url: "https://example.com" }
  ↓
Response: { title, description, image }
  ↓
If successful: Use fetched metadata
```

**Fallback: Cloud Function**
```
If Vercel fails:
  ↓
Try: GET https://fetchpreview-ztdink6mca-uc.a.run.app/fetchPreview?url=...
  ↓
Response: { success: true, title, description, image }
  ↓
If successful: Use fetched metadata
```

**If Both Fail:**
```
Use tab title (already extracted)
  ↓
Continue with empty description and imageUrl
  ↓
User can still save bookmark
```

**Code Location**: 
- `metadata-fetcher.js` - The utility function
- `popup.js` line ~341-364 - Calls the fetcher

**Visual Feedback**: Button text changes to "Fetching preview..." during this step

---

#### **Step 4: Prepare Bookmark Data**
```
Create bookmark object:
  {
    t: title,              // from metadata or tab title
    u: url,                // current tab URL
    ty: 'url',             // type
    dom: domain,           // extracted domain
    d: description,        // from metadata or null
    img: imageUrl,         // from metadata or null
    ar: 0,                 // isArchived
    fav: 0,                // isFavorite
    tags: '',              // tags
    rp: 0,                 // readingProgress
    tsr: 0                 // timeSpentReading
  }
```

**Code Location**: `popup.js` line ~368-381

---

#### **Step 5: Save to Firestore**

**5a. Check User Document Exists**
```
GET /users/{userId}
  ↓
If 404: Error "User document not found"
  ↓
If exists: Continue to premium check
```

**5b. Check Premium Status**
```
Read user document fields:
  - isPremium (boolean)
  - premiumExpirationDate (timestamp)
  - subscriptionCancelled (boolean)
  - subscriptionExpired (boolean)
  - subscriptionExpirationDate (timestamp)
  ↓
Calculate premium status:
  - isPremium == true OR
  - premiumExpirationDate > now OR
  - (subscriptionExpirationDate > now AND not cancelled AND not expired)
  ↓
If not premium: Error "Premium subscription required"
  ↓
If premium: Continue to save
```

**5c. Save Bookmark**
```
POST /bookmarks/{userId}/items/{bookmarkId}
  ↓
Body: Firestore document format with all bookmark fields
  ↓
Headers: Authorization: Bearer {idToken}
  ↓
Firestore Rules Check:
  - User must be authenticated
  - userId in path must match authenticated user
  - User document must exist
  - User must have premium status (checked by rules)
  ↓
If successful: Bookmark saved
  ↓
If permission denied: Error shown to user
```

**Code Location**: `firestore.js` - `saveBookmark()` function

**Visual Feedback**: Button text changes to "Saving..." during this step

---

#### **Step 6: Update UI**
```
Show success message: "Bookmark saved successfully!"
  ↓
If on bookmarks view: Refresh bookmarks list (force refresh)
  ↓
Reset button to "Save Bookmark"
  ↓
Clear status message after 3 seconds
```

**Code Location**: `popup.js` line ~386-398

---

## 🔄 Alternative Flow: Auto-Save from Context Menu

When user right-clicks and selects "Save to Save It Later":

1. **Check Auto-Save Setting**
   - If disabled: Open popup (same as manual save)
   - If enabled: Continue with auto-save

2. **Auto-Save Steps** (if enabled):
   - Check authentication
   - Check premium status
   - Fetch metadata (Vercel → Cloud Function fallback)
   - Save bookmark directly
   - Show desktop notification

**Code Location**: `background.js` - Context menu handler

---

## 🎯 Key Points

1. **Metadata Fetching**: Always tries Vercel first, falls back to Cloud Function
2. **Premium Check**: Happens both client-side (for UX) and server-side (Firestore rules)
3. **Error Handling**: Graceful fallbacks at each step
4. **User Feedback**: Visual indicators at each stage (button text changes)
5. **Caching**: Bookmarks are cached for 5 minutes to reduce API calls

---

## 🔍 Debugging Tips

If bookmark saving fails:

1. **Check Console Logs**:
   - Look for "✅ Fetched metadata from Vercel" or "Cloud Function"
   - Look for "Premium status check before save"
   - Look for any error messages

2. **Common Issues**:
   - **"User document not found"**: User needs to sign in on mobile app first
   - **"Premium subscription required"**: Premium status not synced from mobile app
   - **"Permission denied"**: Firestore rules blocking (check premium status)
   - **Metadata fetch fails**: Both endpoints down (rare, but bookmark still saves with tab title)

3. **Network Tab**:
   - Check if Vercel endpoint responds (200 OK)
   - Check if Cloud Function responds (200 OK)
   - Check if Firestore save succeeds (200 OK)

