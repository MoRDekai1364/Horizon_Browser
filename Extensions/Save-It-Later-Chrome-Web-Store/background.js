// Background service worker for Chrome extension
// Note: This is a service worker, so it doesn't have access to window object
// We need to use hardcoded values or import from config via importScripts
// For now, using hardcoded values (they're public anyway)

const FIREBASE_PROJECT_ID = 'save-it-later-dd29e';

// Categorization service for auto-tagging (inline version for service worker)
// Ported from lib/services/categorization_service.dart
const CategorizationService = {
  _platformKeywords: {
    'social': ['facebook.com', 'instagram.com', 'twitter.com', 'x.com', 'tiktok.com', 'linkedin.com', 'snapchat.com', 'pinterest.com', 'reddit.com'],
    'video': ['youtube.com', 'youtu.be', 'vimeo.com', 'twitch.tv', 'tiktok.com', 'instagram.com/tv', 'facebook.com/watch'],
    'news': ['cnn.com', 'bbc.com', 'reuters.com', 'nytimes.com', 'washingtonpost.com', 'theguardian.com', 'bloomberg.com', 'wsj.com', 'ft.com'],
    'shopping': ['amazon.com', 'ebay.com', 'etsy.com', 'shopify.com', 'alibaba.com', 'walmart.com', 'target.com', 'bestbuy.com'],
    'education': ['coursera.org', 'udemy.com', 'khanacademy.org', 'edx.org', 'mit.edu', 'stanford.edu', 'harvard.edu', 'youtube.com/education'],
    'development': ['github.com', 'stackoverflow.com', 'dev.to', 'medium.com/@', 'hashnode.com', 'freecodecamp.org', 'codepen.io'],
    'entertainment': ['netflix.com', 'hulu.com', 'disney.com', 'hbo.com'],
    'audio': ['spotify.com', 'open.spotify.com', 'music.apple.com', 'soundcloud.com', 'music.youtube.com', 'pandora.com', 'deezer.com', 'tidal.com', 'bandcamp.com', 'audiomack.com', 'mixcloud.com']
  },
  _enhancedContentKeywords: {
    'work': ['meeting', 'project', 'deadline', 'work', 'job', 'career', 'business', 'office', 'team', 'client', 'presentation', 'report', 'conference', 'interview', 'resume', 'cv', 'employment', 'professional'],
    'learning': ['tutorial', 'course', 'learn', 'study', 'education', 'training', 'how to', 'guide', 'lesson', 'class', 'academy', 'university', 'skill', 'knowledge', 'research', 'documentation', 'manual'],
    'shopping': ['buy', 'purchase', 'deal', 'sale', 'price', 'shop', 'store', 'discount', 'offer', 'product', 'item', 'cart', 'checkout', 'shipping', 'delivery', 'review', 'rating', 'compare'],
    'travel': ['trip', 'vacation', 'hotel', 'flight', 'travel', 'destination', 'booking', 'reservation', 'tourism', 'adventure', 'journey', 'airline', 'accommodation', 'itinerary', 'passport', 'visa'],
    'health': ['fitness', 'workout', 'diet', 'health', 'medical', 'exercise', 'nutrition', 'wellness', 'gym', 'yoga', 'running', 'training', 'doctor', 'hospital', 'medicine', 'therapy', 'mental health'],
    'finance': ['money', 'investment', 'budget', 'finance', 'banking', 'crypto', 'stock', 'trading', 'savings', 'loan', 'credit', 'insurance', 'retirement', 'tax', 'expense', 'income', 'wealth'],
    'tech': ['code', 'programming', 'software', 'tech', 'development', 'coding', 'app', 'website', 'database', 'api', 'algorithm', 'debug', 'framework', 'library', 'tool', 'system', 'computer'],
    'entertainment': ['movie', 'game', 'fun', 'entertainment', 'hobby', 'leisure', 'music', 'book', 'series', 'show', 'comedy', 'drama', 'sport', 'gaming', 'streaming', 'podcast', 'comic'],
    'personal': ['family', 'friend', 'personal', 'home', 'life', 'relationship', 'birthday', 'anniversary', 'wedding', 'baby', 'pet', 'hobby', 'interest', 'passion', 'dream', 'goal'],
    'news': ['news', 'article', 'breaking', 'update', 'report', 'story', 'politics', 'world', 'local', 'national', 'international', 'economy', 'sports', 'technology', 'science', 'weather']
  },
  _contentKeywords: {
    'idea': ['idea', 'concept', 'brainstorm', 'innovation', 'creative', 'inspiration', 'thought', 'suggestion', 'proposal', 'vision'],
    'task': ['todo', 'task', 'reminder', 'deadline', 'schedule', 'meeting', 'appointment', 'due', 'complete', 'finish', 'do this'],
    'note': ['note', 'memo', 'remember', 'important', 'reference', 'info', 'information', 'details', 'summary', 'recap'],
    'app': ['app', 'application', 'software', 'tool', 'download', 'install', 'mobile app', 'desktop app', 'web app', 'extension', 'ios app', 'android app', 'play store', 'app store', 'chrome extension'],
    'anime': ['anime', 'manga', 'otaku', 'japanese animation', 'anime series', 'anime episode', 'anime movie', 'crunchyroll', 'funimation', 'myanimelist', 'anilist', 'shonen', 'shoujo', 'seinen', 'josei', 'naruto', 'one piece', 'dragon ball', 'attack on titan', 'demon slayer', 'jujutsu kaisen', 'tokyo ghoul', 'death note', 'fullmetal alchemist'],
    'cartoon': ['cartoon', 'animation', 'animated', 'cartoon series', 'animated show', 'disney cartoon', 'cartoon network', 'nickelodeon', 'adult swim', 'rick and morty', 'south park', 'family guy', 'simpsons', 'futurama', 'adventure time', 'regular show', 'spongebob', 'avatar the last airbender'],
    'books': ['book', 'novel', 'ebook', 'kindle', 'reading', 'author', 'writer', 'literature', 'fiction', 'non-fiction', 'biography', 'memoir', 'poetry', 'poem', 'short story', 'chapter', 'page', 'library', 'goodreads', 'book review', 'bookstore', 'publisher'],
    'manga': ['manga', 'comic', 'graphic novel', 'manhwa', 'manhua', 'webtoon', 'manga chapter', 'manga volume', 'manga scan', 'manga reader', 'mangadex', 'mangakakalot', 'read manga', 'manga online'],
    'rated x': ['rated x', 'adult', 'nsfw', '18+', 'explicit', 'mature content', 'adult content', 'xxx', 'porn', 'pornography', 'hentai', 'ecchi', 'adult video', 'adult site', 'adult entertainment'],
    'mp4': ['mp4', 'video file', 'download video', 'video download', 'video format', '.mp4', 'video player', 'video streaming', 'video content', 'movie download', 'video clip', 'video file download']
  },
  _isValidUrl(url) {
    try {
      const uri = new URL(url);
      return uri.protocol === 'http:' || uri.protocol === 'https:';
    } catch (e) {
      return false;
    }
  },
  _containsKeywords(text, keywords) {
    return keywords.some(keyword => text.includes(keyword));
  },
  _getPlatformFromUrl(url) {
    if (!this._isValidUrl(url)) return null;
    try {
      const uri = new URL(url);
      const host = uri.host.toLowerCase();
      for (const [platform, keywords] of Object.entries(this._platformKeywords)) {
        if (keywords.some(keyword => host.includes(keyword))) {
          return platform;
        }
      }
    } catch (e) {
      return null;
    }
    return null;
  },
  _getCategoryFromUrl(url) {
    return this._getPlatformFromUrl(url);
  },
  analyzeUrlPattern(url) {
    if (!this._isValidUrl(url)) return 'other';
    try {
      const uri = new URL(url);
      const path = uri.path.toLowerCase();
      const host = uri.host.toLowerCase();
      if (path.includes('/video/') || path.includes('/watch') || path.includes('/v/') || host.includes('youtube') || host.includes('vimeo') || host.includes('tiktok')) return 'video';
      if (path.includes('/article/') || path.includes('/news/') || path.includes('/post/') || path.includes('/blog/') || path.includes('/story/')) return 'article';
      if (path.includes('/product/') || path.includes('/shop/') || path.includes('/buy/') || path.includes('/item/') || host.includes('amazon') || host.includes('ebay')) return 'shopping';
      if (path.includes('/tutorial/') || path.includes('/learn/') || path.includes('/course/') || path.includes('/lesson/') || path.includes('/guide/')) return 'learning';
      if (path.includes('/meeting/') || path.includes('/project/') || path.includes('/work/') || host.includes('linkedin') || host.includes('slack')) return 'work';
      if (path.includes('/movie/') || path.includes('/show/') || path.includes('/game/') || host.includes('netflix') || host.includes('hulu')) return 'entertainment';
      if (host.includes('crunchyroll') || host.includes('funimation') || host.includes('myanimelist') || host.includes('anilist') || host.includes('anime') || path.includes('/anime/')) return 'anime';
      if (host.includes('mangadex') || host.includes('mangakakalot') || host.includes('webtoon') || host.includes('manga') || path.includes('/manga/') || path.includes('/chapter/')) return 'manga';
      if (host.includes('goodreads') || host.includes('amazon.com/kindle') || host.includes('book') || path.includes('/book/') || path.includes('/novel/') || path.includes('/ebook/')) return 'books';
      if (url.toLowerCase().endsWith('.mp4') || url.toLowerCase().endsWith('.avi') || url.toLowerCase().endsWith('.mov') || url.toLowerCase().endsWith('.mkv') || path.includes('/video/') || path.includes('/download/')) return 'mp4';
      return 'other';
    } catch (e) {
      return 'other';
    }
  },
  getSuggestedTags({ url, title, description = null, content = null }) {
    const tags = [];
    const platform = this._getPlatformFromUrl(url);
    if (platform) tags.push(platform);
    const urlPattern = this.analyzeUrlPattern(url);
    if (urlPattern !== 'other') tags.push(urlPattern);
    const category = this._getCategoryFromUrl(url);
    if (category) tags.push(category);
    const textToAnalyze = `${title.toLowerCase()} ${description ? description.toLowerCase() : ''} ${content ? content.toLowerCase() : ''}`;
    for (const [tag, keywords] of Object.entries(this._contentKeywords)) {
      if (this._containsKeywords(textToAnalyze, keywords)) tags.push(tag);
    }
    for (const [tag, keywords] of Object.entries(this._enhancedContentKeywords)) {
      if (this._containsKeywords(textToAnalyze, keywords)) tags.push(tag);
    }
    return [...new Set(tags)];
  }
};

// Create context menu on install
chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({
    id: 'saveBookmark',
    title: 'Save to Save It Later',
    contexts: ['page', 'link'],
  });
});

// Validate URL
function isValidUrl(urlString) {
  if (!urlString || typeof urlString !== 'string') {
    return false;
  }
  
  try {
    const url = new URL(urlString);
    // Must have http or https protocol
    return url.protocol === 'http:' || url.protocol === 'https:';
  } catch (e) {
    return false;
  }
}

// Handle context menu click
chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  if (info.menuItemId === 'saveBookmark') {
    const url = info.linkUrl || info.pageUrl || tab.url;
    
    // Validate URL
    if (!isValidUrl(url)) {
      console.warn('Invalid URL from context menu:', url);
      chrome.action.openPopup();
      return;
    }
    
    // Just open the popup - no auto-save
    chrome.action.openPopup();
    chrome.storage.local.set({ pendingBookmark: url });
    return;
    
    // OLD AUTO-SAVE CODE (DISABLED) - kept for reference
    if (false) {
      // Auto-save: Save directly without opening popup
      try {
        // Get current user from storage (if available)
        const authResult = await chrome.storage.local.get(['currentUser', 'idToken']);
        const currentUser = authResult.currentUser;
        let idToken = authResult.idToken;
        
        if (!currentUser || !idToken) {
          // User not signed in - show notification
          chrome.notifications.create({
            type: 'basic',
            iconUrl: 'icons/icon48.png',
            title: '⚠️ Sign In Required',
            message: 'Please sign in to save bookmarks. Click to open extension.',
            priority: 2,
            requireInteraction: false
          });
          
          // Add warning badge
          chrome.action.setBadgeText({ text: '!' });
          chrome.action.setBadgeBackgroundColor({ color: '#FF9500' });
          
          setTimeout(() => {
            chrome.action.setBadgeText({ text: '' });
          }, 5000);
          
          // Open popup to sign in
          chrome.action.openPopup();
          chrome.storage.local.set({ pendingBookmark: url });
          return;
        }
        
        // Check if token is expired (rough check - 1 hour expiry)
        const tokenExpiryTime = (await chrome.storage.local.get(['tokenExpiryTime'])).tokenExpiryTime;
        if (tokenExpiryTime && Date.now() > tokenExpiryTime) {
          // Token expired - show notification
          chrome.notifications.create({
            type: 'basic',
            iconUrl: 'icons/icon48.png',
            title: '⚠️ Session Expired',
            message: 'Please sign in again to save bookmarks. Click to open extension.',
            priority: 2,
            requireInteraction: false
          });
          
          chrome.action.setBadgeText({ text: '!' });
          chrome.action.setBadgeBackgroundColor({ color: '#FF9500' });
          
          setTimeout(() => {
            chrome.action.setBadgeText({ text: '' });
          }, 5000);
          
          // Open popup to refresh
          chrome.action.openPopup();
          chrome.storage.local.set({ pendingBookmark: url });
          return;
        }
        
        // Check premium status before auto-saving
        try {
          const userDocUrl = `https://firestore.googleapis.com/v1/projects/${FIREBASE_PROJECT_ID}/databases/(default)/documents/users/${currentUser.uid}`;
          const userDocResponse = await fetch(userDocUrl, {
            method: 'GET',
            headers: {
              'Authorization': `Bearer ${idToken}`
            }
          });
          
          if (!userDocResponse.ok) {
            // User document doesn't exist or can't access - show notification
            chrome.notifications.create({
              type: 'basic',
              iconUrl: 'icons/icon48.png',
              title: '⚠️ Account Issue',
              message: 'Unable to verify account. Click to open extension.',
              priority: 2,
              requireInteraction: false
            });
            
            chrome.action.setBadgeText({ text: '!' });
            chrome.action.setBadgeBackgroundColor({ color: '#FF9500' });
            
            setTimeout(() => {
              chrome.action.setBadgeText({ text: '' });
            }, 5000);
            
            // Open popup
            chrome.action.openPopup();
            chrome.storage.local.set({ pendingBookmark: url });
            return;
          }
          
          const userData = await userDocResponse.json();
          const fields = userData.fields || {};
          const isPremium = fields.isPremium?.booleanValue === true;
          const expirationDate = fields.premiumExpirationDate?.timestampValue;
          const isPremiumByExpiration = expirationDate && new Date(expirationDate) > new Date();
          
          if (!isPremium && !isPremiumByExpiration) {
            // Not premium - show notification
            chrome.notifications.create({
              type: 'basic',
              iconUrl: 'icons/icon48.png',
              title: '⭐ Premium Required',
              message: 'Premium subscription required to save bookmarks. Click to open extension.',
              priority: 2,
              requireInteraction: false
            });
            
            chrome.action.setBadgeText({ text: '⭐' });
            chrome.action.setBadgeBackgroundColor({ color: '#FF9500' });
            
            setTimeout(() => {
              chrome.action.setBadgeText({ text: '' });
            }, 5000);
            
            // Open popup to show error
            chrome.action.openPopup();
            chrome.storage.local.set({ pendingBookmark: url });
            return;
          }
        } catch (premiumCheckError) {
          console.error('Premium check failed in auto-save:', premiumCheckError);
          
          // Show error notification
          chrome.notifications.create({
            type: 'basic',
            iconUrl: 'icons/icon48.png',
            title: '⚠️ Verification Error',
            message: 'Unable to verify premium status. Click to open extension.',
            priority: 2,
            requireInteraction: false
          });
          
          chrome.action.setBadgeText({ text: '!' });
          chrome.action.setBadgeBackgroundColor({ color: '#FF9500' });
          
          setTimeout(() => {
            chrome.action.setBadgeText({ text: '' });
          }, 5000);
          
          // On error, fall back to opening popup
          chrome.action.openPopup();
          chrome.storage.local.set({ pendingBookmark: url });
          return;
        }
        
        // Fetch metadata and save bookmark
        const title = tab.title || 'Untitled';
        let finalTitle = title;
        let description = '';
        let imageUrl = null;
        
        // Try to fetch metadata using Vercel endpoint (with Cloud Function fallback)
        try {
          // Try Vercel endpoint first
          const vercelEndpoint = 'https://save-it-fetching.vercel.app/api/fetch-content';
          const vercelResponse = await fetch(vercelEndpoint, {
            method: 'POST',
            headers: {
              'Accept': 'application/json',
              'Content-Type': 'application/json',
              'User-Agent': 'SaveItLater-Extension/1.0',
              'Origin': 'https://save-it-fetching.vercel.app',
              'Referer': 'https://save-it-fetching.vercel.app/',
            },
            body: JSON.stringify({ url: url }),
          });
          
          if (vercelResponse.ok) {
            const vercelData = await vercelResponse.json();
            if (vercelData.title && vercelData.title.trim()) {
              finalTitle = vercelData.title.trim();
            }
            if (vercelData.description && vercelData.description.trim()) {
              description = vercelData.description.trim();
            }
            if (vercelData.image && vercelData.image.trim()) {
              imageUrl = vercelData.image.trim();
            }
          }
        } catch (e) {
          // Vercel failed, try Cloud Function fallback
          try {
            const cloudEndpoint = `https://fetchpreview-ztdink6mca-uc.a.run.app/fetchPreview?url=${encodeURIComponent(url)}`;
            const cloudResponse = await fetch(cloudEndpoint, {
              method: 'GET',
              headers: {
                'Content-Type': 'application/json',
                'User-Agent': 'SaveItLater-Extension/1.0',
              },
            });
            
            if (cloudResponse.ok) {
              const cloudData = await cloudResponse.json();
              if (cloudData.success === true) {
                if (cloudData.title && cloudData.title.trim()) {
                  finalTitle = cloudData.title.trim();
                }
                if (cloudData.description && cloudData.description.trim()) {
                  description = cloudData.description.trim();
                }
                if (cloudData.image && cloudData.image.trim()) {
                  imageUrl = cloudData.image.trim();
                }
              }
            }
          } catch (cloudError) {
            // Both endpoints failed, continue with tab title
            console.warn('Metadata fetch failed, using tab title:', cloudError);
          }
        }
        
        const domain = new URL(url).host;
        
        // Generate auto tags
        let tags = [];
        try {
          tags = CategorizationService.getSuggestedTags({
            url: url,
            title: finalTitle,
            description: description || null
          });
          console.log('Generated auto tags:', tags);
        } catch (tagError) {
          console.warn('Tag generation failed:', tagError);
        }
        
        const bookmarkData = {
          t: finalTitle,
          u: url,
          ty: 'url',
          dom: domain,
          d: description || null,
          img: imageUrl || null,
          ar: 0,
          fav: 0,
          tags: tags.join(','), // Convert array to comma-separated string
          rp: 0,
          tsr: 0,
        };
        
        // Save bookmark using Firestore API
        const bookmarkId = Date.now().toString();
        const collectionUrl = `https://firestore.googleapis.com/v1/projects/${FIREBASE_PROJECT_ID}/databases/(default)/documents/bookmarks/${currentUser.uid}/items`;
        
        const firestoreDoc = {
          fields: {
            id: { nullValue: null },
            t: { stringValue: bookmarkData.t || 'Untitled' },
            u: { stringValue: bookmarkData.u || '' },
            ty: { stringValue: bookmarkData.ty || 'url' },
            d: bookmarkData.d ? { stringValue: bookmarkData.d } : { nullValue: null },
            img: bookmarkData.img ? { stringValue: bookmarkData.img } : { nullValue: null },
            dom: bookmarkData.dom ? { stringValue: bookmarkData.dom } : { nullValue: null },
            ar: { integerValue: bookmarkData.ar || '0' },
            fav: { integerValue: bookmarkData.fav || '0' },
            tags: { stringValue: bookmarkData.tags || '' },
            rp: { doubleValue: bookmarkData.rp || 0 },
            tsr: { integerValue: bookmarkData.tsr || '0' },
            // Save 'ca' and 'ua' as integers (milliseconds) to match mobile app format
            ca: { integerValue: Date.now().toString() },
            ua: { integerValue: Date.now().toString() },
            createdAt: { timestampValue: new Date().toISOString() },
            updatedAt: { timestampValue: new Date().toISOString() }
          }
        };
        
        const response = await fetch(`${collectionUrl}?documentId=${bookmarkId}`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${idToken}`
          },
          body: JSON.stringify(firestoreDoc)
        });
        
        if (response.ok) {
          // Show snackbar on the page (bottom right)
          try {
            await chrome.tabs.sendMessage(tab.id, {
              action: 'showSnackbar',
              message: 'Saved to Save It Later',
              type: 'success'
            });
          } catch (e) {
            // Content script might not be ready, fall back to notification
            console.log('Could not show snackbar, using notification instead');
          }
          
          // Also show notification as fallback
          chrome.notifications.create({
            type: 'basic',
            iconUrl: 'icons/icon48.png',
            title: '✅ Saved to Save It Later',
            message: `"${finalTitle}" has been saved successfully!`,
            priority: 2, // High priority
            requireInteraction: false
          });
          
          // Add badge to extension icon for visual feedback
          chrome.action.setBadgeText({ text: '✓' });
          chrome.action.setBadgeBackgroundColor({ color: '#00D4AA' });
          
          // Clear badge after 3 seconds
          setTimeout(() => {
            chrome.action.setBadgeText({ text: '' });
          }, 3000);
        } else {
          // Save failed - show error snackbar
          const errorText = await response.text();
          console.error('Auto-save failed:', errorText);
          
          // Show error snackbar on the page
          try {
            await chrome.tabs.sendMessage(tab.id, {
              action: 'showSnackbar',
              message: response.status === 403 
                ? 'Premium subscription required'
                : 'Failed to save bookmark',
              type: 'error'
            });
          } catch (e) {
            // Content script might not be ready, fall back to notification
            console.log('Could not show snackbar, using notification instead');
          }
          
          // Also show notification as fallback
          chrome.notifications.create({
            type: 'basic',
            iconUrl: 'icons/icon48.png',
            title: '❌ Save Failed',
            message: response.status === 403 
              ? 'Premium subscription required. Click to open extension.'
              : 'Failed to save bookmark. Click to open extension.',
            priority: 2,
            requireInteraction: false
          });
          
          // Add error badge
          chrome.action.setBadgeText({ text: '!' });
          chrome.action.setBadgeBackgroundColor({ color: '#FF3B30' });
          
          // Clear badge after 5 seconds
          setTimeout(() => {
            chrome.action.setBadgeText({ text: '' });
          }, 5000);
          
          // Open popup for manual save
          chrome.action.openPopup();
          chrome.storage.local.set({ pendingBookmark: url });
        }
      } catch (error) {
        console.error('Auto-save error:', error);
        
        // Show error notification
        chrome.notifications.create({
          type: 'basic',
          iconUrl: 'icons/icon48.png',
          title: '❌ Save Error',
          message: 'An error occurred while saving. Click to open extension.',
          priority: 2,
          requireInteraction: false
        });
        
        // Add error badge
        chrome.action.setBadgeText({ text: '!' });
        chrome.action.setBadgeBackgroundColor({ color: '#FF3B30' });
        
        // Clear badge after 5 seconds
        setTimeout(() => {
          chrome.action.setBadgeText({ text: '' });
        }, 5000);
        
        // On error, fall back to opening popup
        chrome.action.openPopup();
        chrome.storage.local.set({ pendingBookmark: url });
      }
    } else {
      // Manual mode: Open popup
      chrome.action.openPopup();
      chrome.storage.local.set({ pendingBookmark: url });
    }
  }
});

// Handle extension icon click (popup opens automatically via manifest)

// Listen for messages from content scripts or popup
chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
  if (request.action === 'saveBookmark') {
    // Handle bookmark save request
    sendResponse({ success: true });
  } else if (request.action === 'autoSaveSettingChanged') {
    // Auto-save setting changed, no response needed
    console.log('Auto-save setting changed:', request.enabled);
  } else if (request.action === 'saveBookmarkFromPopup') {
    // Handle manual save from popup - this ensures save continues even if popup closes
    (async () => {
      try {
        const { url, title, userId, idToken } = request;
        
        if (!url || !userId || !idToken) {
          sendResponse({ success: false, error: 'Missing required parameters' });
          return;
        }
        
        // Get current tab to ensure we have the latest info
        let tab;
        try {
          const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
          tab = tabs[0];
        } catch (error) {
          console.warn('Could not get current tab, using provided title:', error);
        }
        
        let finalTitle = title || (tab?.title) || 'Untitled';
        let description = '';
        let imageUrl = null;
        
        // Fetch metadata (title, description, image) using metadata fetcher
        try {
          // Try Vercel endpoint first
          const vercelEndpoint = 'https://save-it-fetching.vercel.app/api/fetch-content';
          const vercelResponse = await fetch(vercelEndpoint, {
            method: 'POST',
            headers: {
              'Accept': 'application/json',
              'Content-Type': 'application/json',
              'User-Agent': 'SaveItLater-Extension/1.0',
              'Origin': 'https://save-it-fetching.vercel.app',
              'Referer': 'https://save-it-fetching.vercel.app/',
            },
            body: JSON.stringify({ url: url }),
          });
          
          if (vercelResponse.ok) {
            const vercelData = await vercelResponse.json();
            if (vercelData.title && vercelData.title.trim()) {
              finalTitle = vercelData.title.trim();
            }
            if (vercelData.description && vercelData.description.trim()) {
              description = vercelData.description.trim();
            }
            if (vercelData.image && vercelData.image.trim()) {
              imageUrl = vercelData.image.trim();
            }
          }
        } catch (e) {
          // Vercel failed, try Cloud Function fallback
          try {
            const cloudEndpoint = `https://fetchpreview-ztdink6mca-uc.a.run.app/fetchPreview?url=${encodeURIComponent(url)}`;
            const cloudResponse = await fetch(cloudEndpoint, {
              method: 'GET',
              headers: {
                'Content-Type': 'application/json',
                'User-Agent': 'SaveItLater-Extension/1.0',
              },
            });
            
            if (cloudResponse.ok) {
              const cloudData = await cloudResponse.json();
              if (cloudData.success === true) {
                if (cloudData.title && cloudData.title.trim()) {
                  finalTitle = cloudData.title.trim();
                }
                if (cloudData.description && cloudData.description.trim()) {
                  description = cloudData.description.trim();
                }
                if (cloudData.image && cloudData.image.trim()) {
                  imageUrl = cloudData.image.trim();
                }
              }
            }
          } catch (cloudError) {
            // Both endpoints failed, continue with provided title
            console.warn('Metadata fetch failed, using provided title:', cloudError);
          }
        }
        
        const domain = new URL(url).host;
        
        // Generate auto tags
        let tags = [];
        try {
          tags = CategorizationService.getSuggestedTags({
            url: url,
            title: finalTitle,
            description: description || null
          });
          console.log('Generated auto tags:', tags);
        } catch (tagError) {
          console.warn('Tag generation failed:', tagError);
        }
        
        const bookmarkData = {
          t: finalTitle,
          u: url,
          ty: 'url',
          dom: domain,
          d: description || null,
          img: imageUrl || null,
          ar: 0,
          fav: 0,
          tags: tags.join(','), // Convert array to comma-separated string
          rp: 0,
          tsr: 0,
        };
        
        // Save bookmark using Firestore API
        const bookmarkId = Date.now().toString();
        const collectionUrl = `https://firestore.googleapis.com/v1/projects/${FIREBASE_PROJECT_ID}/databases/(default)/documents/bookmarks/${userId}/items`;
        
        const firestoreDoc = {
          fields: {
            id: { nullValue: null },
            t: { stringValue: bookmarkData.t || 'Untitled' },
            u: { stringValue: bookmarkData.u || '' },
            ty: { stringValue: bookmarkData.ty || 'url' },
            d: bookmarkData.d ? { stringValue: bookmarkData.d } : { nullValue: null },
            img: bookmarkData.img ? { stringValue: bookmarkData.img } : { nullValue: null },
            dom: bookmarkData.dom ? { stringValue: bookmarkData.dom } : { nullValue: null },
            ar: { integerValue: bookmarkData.ar || '0' },
            fav: { integerValue: bookmarkData.fav || '0' },
            tags: { stringValue: bookmarkData.tags || '' },
            rp: { doubleValue: bookmarkData.rp || 0 },
            tsr: { integerValue: bookmarkData.tsr || '0' },
            // Save 'ca' and 'ua' as integers (milliseconds) to match mobile app format
            ca: { integerValue: Date.now().toString() },
            ua: { integerValue: Date.now().toString() },
            createdAt: { timestampValue: new Date().toISOString() },
            updatedAt: { timestampValue: new Date().toISOString() }
          }
        };
        
        const response = await fetch(`${collectionUrl}?documentId=${bookmarkId}`, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${idToken}`
          },
          body: JSON.stringify(firestoreDoc)
        });
        
        if (response.ok) {
          // Show notification
          chrome.notifications.create({
            type: 'basic',
            iconUrl: 'icons/icon48.png',
            title: '✅ Saved to Save It Later',
            message: `"${finalTitle}" has been saved successfully!`,
            priority: 2, // High priority
            requireInteraction: false
          });
          
          // Add badge to extension icon for visual feedback
          chrome.action.setBadgeText({ text: '✓' });
          chrome.action.setBadgeBackgroundColor({ color: '#00D4AA' });
          
          // Clear badge after 3 seconds
          setTimeout(() => {
            chrome.action.setBadgeText({ text: '' });
          }, 3000);
          
          // Clear pending save
          await chrome.storage.local.remove(['pendingManualSave']);
          
          sendResponse({ success: true });
        } else {
          const errorText = await response.text();
          console.error('Firestore save error:', errorText);
          sendResponse({ 
            success: false, 
            error: response.status === 403 
              ? 'Premium subscription required' 
              : 'Failed to save bookmark' 
          });
        }
      } catch (error) {
        console.error('Error saving bookmark from popup:', error);
        sendResponse({ 
          success: false, 
          error: error.message || 'Failed to save bookmark' 
        });
      }
    })();
    
    return true; // Keep message channel open for async response
  }
  return true; // Keep message channel open for async response
});

