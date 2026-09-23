// DOM elements
const signedOutView = document.getElementById('signedOut');
const signedInView = document.getElementById('signedIn');
const loadingView = document.getElementById('loading');
const signInBtn = document.getElementById('signInBtn');
const signOutBtn = document.getElementById('signOutBtn');
const saveBtn = document.getElementById('saveBtn');
const userName = document.getElementById('userName');
const userAvatar = document.getElementById('userAvatar');
const premiumBadge = document.getElementById('premiumBadge');
const pageTitle = document.getElementById('pageTitle');
const pageUrl = document.getElementById('pageUrl');
const statusMessage = document.getElementById('statusMessage');

// Home screen elements
const homeTab = document.getElementById('homeTab');
const bookmarksTab = document.getElementById('bookmarksTab');
const homeView = document.getElementById('homeView');
const bookmarksView = document.getElementById('bookmarksView');
const bookmarksList = document.getElementById('bookmarksList');
const refreshBookmarksBtn = document.getElementById('refreshBookmarksBtn');
const openAppBtn = document.getElementById('openAppBtn');
const openLandingPageBtn = document.getElementById('openLandingPageBtn');
const openLandingPageBtnSignedOut = document.getElementById('openLandingPageBtnSignedOut');
const viewBookmarksBtn = document.getElementById('viewBookmarksBtn');
const openFullScreenBtn = document.getElementById('openFullScreenBtn');
const openFullScreenFromBookmarksBtn = document.getElementById('openFullScreenFromBookmarksBtn');
const bookmarkSearch = document.getElementById('bookmarkSearch');
const clearSearchBtn = document.getElementById('clearSearchBtn');
const bookmarkSort = document.getElementById('bookmarkSort');
const autoSaveToggle = document.getElementById('autoSaveToggle');

// Manual URL entry elements
const currentPageMode = document.getElementById('currentPageMode');
const manualUrlMode = document.getElementById('manualUrlMode');
const switchToManualBtn = document.getElementById('switchToManualBtn');
const switchToCurrentBtn = document.getElementById('switchToCurrentBtn');
const manualUrlInput = document.getElementById('manualUrlInput');
const saveManualUrlBtn = document.getElementById('saveManualUrlBtn');

let currentTab = null;
let isPremium = false;
let currentView = 'home';
let cachedBookmarks = null;
let cacheTimestamp = null;
const CACHE_DURATION = 5 * 60 * 1000; // 5 minutes
let allBookmarksData = []; // Store all bookmarks for filtering/sorting
let currentSearchQuery = '';
let currentSortOption = 'dateNewest';

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

// Initialize
async function init() {
  showView(loadingView);
  
  // Load cached bookmarks and sort option from storage
  try {
    const result = await chrome.storage.local.get(['cachedBookmarks', 'cacheTimestamp', 'sortOption']);
    if (result.cachedBookmarks && result.cacheTimestamp) {
      const cacheAge = Date.now() - result.cacheTimestamp;
      if (cacheAge < CACHE_DURATION) {
        cachedBookmarks = result.cachedBookmarks;
        cacheTimestamp = result.cacheTimestamp;
        console.log('Loaded cached bookmarks from storage (age:', Math.round(cacheAge / 1000), 'seconds)');
      }
    }
    // Load saved sort option
    if (result.sortOption) {
      currentSortOption = result.sortOption;
      // Sync dropdown with saved sort option
      if (bookmarkSort) {
        bookmarkSort.value = currentSortOption;
      }
    } else if (bookmarkSort) {
      // Initialize dropdown to match default
      bookmarkSort.value = currentSortOption;
    }
  } catch (error) {
    console.warn('Failed to load cache from storage:', error);
    // Still initialize dropdown
    if (bookmarkSort) {
      bookmarkSort.value = currentSortOption;
    }
  }
  
  // Load auto-save setting (default to true if not set)
  try {
    const result = await chrome.storage.local.get(['autoSaveEnabled']);
    if (autoSaveToggle) {
      // Default to true if not explicitly set to false
      autoSaveToggle.checked = result.autoSaveEnabled !== false;
      // If not set, save the default value
      if (result.autoSaveEnabled === undefined) {
        await chrome.storage.local.set({ autoSaveEnabled: true });
      }
    }
  } catch (error) {
    console.warn('Failed to load auto-save setting:', error);
  }
  
  // Get current tab
  try {
    const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
    currentTab = tabs[0];
    
    // Update page info
    if (currentTab) {
      pageTitle.textContent = currentTab.title || 'Untitled';
      pageUrl.textContent = currentTab.url || '';
    }
  } catch (error) {
    console.error('Error getting tab:', error);
  }
  
  // Wait for auth to be available
  let attempts = 0;
  while (!window.firebaseAuth && attempts < 50) {
    await new Promise(resolve => setTimeout(resolve, 50));
    attempts++;
  }
  
  if (!window.firebaseAuth) {
    console.error('Firebase auth not available');
    handleSignedOut();
    return;
  }
  
  // Check current auth state immediately
  // Note: We don't auto-login - user must explicitly sign in to choose account
  const currentUser = await window.firebaseAuth.getCurrentUser();
  if (currentUser) {
    await handleSignedIn(currentUser);
  } else {
    handleSignedOut();
  }
  
  // Listen for auth state changes
  window.firebaseAuth.onAuthStateChanged(async (user) => {
    if (user) {
      await handleSignedIn(user);
    } else {
      handleSignedOut();
      // Clear cache on sign out
      cachedBookmarks = null;
      cacheTimestamp = null;
      chrome.storage.local.remove(['cachedBookmarks', 'cacheTimestamp']);
    }
  });
}

// Show specific view
function showView(view) {
  signedOutView.classList.add('hidden');
  signedInView.classList.add('hidden');
  loadingView.classList.add('hidden');
  view.classList.remove('hidden');
}

// Handle signed in state
async function handleSignedIn(user) {
  showView(signedInView);
  
  userName.textContent = user.displayName || user.email;
  userAvatar.src = user.photoURL || '';
  userAvatar.style.display = user.photoURL ? 'block' : 'none';
  
  // Check premium status
  let idToken = await window.firebaseAuth.getIdToken();
  if (idToken) {
    try {
      isPremium = await window.firestoreAPI.checkPremiumStatus(user.uid, idToken);
    } catch (error) {
      console.error('Error checking premium status:', error);
      
      // If 401 error, try refreshing token and retry
      if (error.message && error.message.includes('401')) {
        console.log('Token expired, attempting to refresh...');
        try {
          idToken = await window.firebaseAuth.getIdToken(true); // Force refresh
          if (idToken) {
            isPremium = await window.firestoreAPI.checkPremiumStatus(user.uid, idToken);
          } else {
            console.warn('Token refresh failed, user needs to sign in again');
            isPremium = false;
            // Show message to user
            showStatus('Session expired. Please sign out and sign in again.', 'error');
          }
        } catch (refreshError) {
          console.error('Token refresh failed:', refreshError);
          isPremium = false;
        }
      } else {
        isPremium = false;
      }
    }
  }
  
  // Only show premium badge if we confirmed premium status
  if (isPremium) {
    premiumBadge.classList.remove('hidden');
  } else {
    premiumBadge.classList.add('hidden');
  }
  
  // Log premium status for debugging
  console.log('Premium status:', isPremium, 'User ID:', user.uid);
}

// Handle signed out state
function handleSignedOut() {
  showView(signedOutView);
}

// Sign in with Google
if (signInBtn) {
  signInBtn.addEventListener('click', async () => {
    try {
      console.log('Sign in button clicked');
      
      // Check if firebaseAuth is available
      if (!window.firebaseAuth) {
        console.error('firebaseAuth not available');
        showStatus('Authentication not initialized. Please reload the extension.', 'error');
        return;
      }
      
      if (!window.firebaseAuth.signInWithGoogle) {
        console.error('signInWithGoogle method not available');
        showStatus('Sign in method not available. Please reload the extension.', 'error');
        return;
      }
      
      signInBtn.disabled = true;
      signInBtn.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 21h-6a3 3 0 0 1-3-3v-1a3 3 0 0 1 3-3h6a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1z"></path><path d="M15 3H6a3 3 0 0 0-3 3v12a3 3 0 0 0 3 3h9"></path><path d="M10 12h8"></path><path d="M14 8l4 4-4 4"></path></svg>Signing in...';
      
      console.log('Calling signInWithGoogle...');
      const result = await window.firebaseAuth.signInWithGoogle();
      console.log('Sign in result:', result);
      
      if (result && result.success) {
        // Auth state change will handle the UI update
        console.log('Sign in successful');
      } else {
        throw new Error('Sign in failed: ' + (result?.error || 'Unknown error'));
      }
    } catch (error) {
      console.error('Sign in error:', error);
      console.error('Error details:', {
        message: error.message,
        stack: error.stack,
        firebaseAuth: !!window.firebaseAuth,
        signInMethod: !!window.firebaseAuth?.signInWithGoogle
      });
      showStatus(`Failed to sign in: ${error.message || 'Please try again.'}`, 'error');
      signInBtn.disabled = false;
      signInBtn.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 21h-6a3 3 0 0 1-3-3v-1a3 3 0 0 1 3-3h6a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1z"></path><path d="M15 3H6a3 3 0 0 0-3 3v12a3 3 0 0 0 3 3h9"></path><path d="M10 12h8"></path><path d="M14 8l4 4-4 4"></path></svg>Sign in with Google';
    }
  });
}

// Sign out
if (signOutBtn) {
  signOutBtn.addEventListener('click', async () => {
    try {
      await window.firebaseAuth.signOut();
      // Auth state change will handle the UI update
    } catch (error) {
      console.error('Sign out error:', error);
      showStatus('Failed to sign out.', 'error');
    }
  });
}

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

// Save bookmark
if (saveBtn) {
  saveBtn.addEventListener('click', async () => {
    if (!currentTab || !currentTab.url) {
      showStatus('Unable to get page information.', 'error');
      return;
    }
    
    // Validate URL
    const url = currentTab.url.trim();
    if (!isValidUrl(url)) {
      showStatus('Invalid URL. Please ensure the page has a valid http:// or https:// URL.', 'error');
      return;
    }
    
    const user = await window.firebaseAuth.getCurrentUser();
    if (!user) {
      showStatus('Please sign in first.', 'error');
      return;
    }
    
    // Re-check premium status before saving (in case it changed)
    let idToken = await window.firebaseAuth.getIdToken();
    if (!idToken) {
      showStatus('Not authenticated. Please sign in again.', 'error');
      return;
    }
    
    // Verify premium status again before attempting save
    try {
      let currentPremiumStatus = await window.firestoreAPI.checkPremiumStatus(user.uid, idToken);
      if (!currentPremiumStatus) {
        isPremium = false;
        showStatus('Premium subscription required. Please sign in to the mobile app to sync your premium status.', 'error');
        // Update UI
        if (premiumBadge) {
          premiumBadge.classList.add('hidden');
        }
        
        // Show detailed error in console
        console.error('Premium check failed. User document may not exist or premium status not set.');
        console.error('To fix:');
        console.error('1. Open the mobile app');
        console.error('2. Sign in with the same Google account');
        console.error('3. Ensure you have an active premium subscription');
        console.error('4. The app will sync premium status to Firestore');
        
        return;
      }
      isPremium = true;
      console.log('Premium status verified successfully');
    } catch (error) {
      console.error('Error verifying premium status:', error);
      
      // If 401 error, try refreshing token and retry once
      if (error.message && error.message.includes('401')) {
        console.log('Token expired, attempting to refresh...');
        try {
          idToken = await window.firebaseAuth.getIdToken(true); // Force refresh
          if (idToken) {
            const retryPremiumStatus = await window.firestoreAPI.checkPremiumStatus(user.uid, idToken);
            if (retryPremiumStatus) {
              isPremium = true;
              console.log('Premium status verified after token refresh');
            } else {
              showStatus('Premium subscription required. Please sign in to the mobile app to sync your premium status.', 'error');
              return;
            }
          } else {
            // Token refresh failed - user needs to sign in again
            showStatus('Authentication expired. Please sign out and sign in again to refresh your session.', 'error');
            console.warn('Token refresh failed - user needs to re-authenticate');
            return;
          }
        } catch (refreshError) {
          console.error('Token refresh failed:', refreshError);
          showStatus('Unable to verify premium status. Please sign in again.', 'error');
          return;
        }
      } else {
        showStatus('Unable to verify premium status. Please try again.', 'error');
        return;
      }
    }
    
    // Capture URL and title immediately (before any async operations)
    const capturedTitle = currentTab.title || 'Untitled';
    
    // Update button state
    if (saveBtn) {
      saveBtn.disabled = true;
      saveBtn.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path></svg>Saving...';
    }
    
    // Send save request to background script so it continues even if popup closes
    try {
      // Store auth info temporarily for background script
      await chrome.storage.local.set({
        pendingManualSave: {
          url: url,
          title: capturedTitle,
          userId: user.uid,
          idToken: idToken,
          timestamp: Date.now()
        }
      });
      
      // Request background script to handle the save
      chrome.runtime.sendMessage({
        action: 'saveBookmarkFromPopup',
        url: url,
        title: capturedTitle,
        userId: user.uid,
        idToken: idToken
      }, async (response) => {
        // Handle response (popup might be closed by now, so check if elements exist)
        if (chrome.runtime.lastError) {
          console.error('Error sending message to background:', chrome.runtime.lastError);
          // Fallback: try saving directly in popup
          try {
            let domain = '';
            try {
              const urlObj = new URL(url);
              domain = urlObj.host;
            } catch (e) {
              if (statusMessage) {
                showStatus('Invalid URL format.', 'error');
              }
              if (saveBtn) {
                saveBtn.disabled = false;
                saveBtn.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path></svg>Save Bookmark';
              }
              return;
            }
            
            let title = capturedTitle;
            let description = '';
            let imageUrl = null;
            
            // Fetch metadata
            try {
              if (window.metadataFetcher && window.metadataFetcher.fetchUrlMetadata) {
                const metadata = await window.metadataFetcher.fetchUrlMetadata(url);
                if (metadata.title && metadata.title.trim()) {
                  title = metadata.title.trim();
                }
                if (metadata.description && metadata.description.trim()) {
                  description = metadata.description.trim();
                }
                if (metadata.image && metadata.image.trim()) {
                  imageUrl = metadata.image.trim();
                }
              }
            } catch (metadataError) {
              console.warn('Metadata fetching failed, using tab title:', metadataError);
            }
            
            // Generate auto tags
            let tags = [];
            if (window.CategorizationService) {
              try {
                tags = window.CategorizationService.getSuggestedTags({
                  url: url,
                  title: title,
                  description: description || null
                });
                console.log('Generated auto tags:', tags);
              } catch (tagError) {
                console.warn('Tag generation failed:', tagError);
              }
            }
            
            const bookmarkData = {
              t: title,
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
            
            await window.firestoreAPI.saveBookmark(bookmarkData, user.uid, idToken);
            
            if (statusMessage) {
              showStatus('Bookmark saved successfully!', 'success');
            }
            
            if (currentView === 'bookmarks' && bookmarksList) {
              loadBookmarks(true);
            }
            
            if (saveBtn) {
              saveBtn.disabled = false;
              saveBtn.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path></svg>Save Bookmark';
            }
            
            setTimeout(() => {
              if (statusMessage) {
                statusMessage.classList.add('hidden');
              }
            }, 3000);
          } catch (fallbackError) {
            console.error('Fallback save error:', fallbackError);
            if (statusMessage) {
              showStatus('Failed to save bookmark. Please try again.', 'error');
            }
            if (saveBtn) {
              saveBtn.disabled = false;
              saveBtn.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path></svg>Save Bookmark';
            }
          }
          return;
        }
        
        if (response && response.success) {
          // Show success message if popup is still open
          if (statusMessage) {
            showStatus('Bookmark saved successfully!', 'success');
          }
          
          // Refresh bookmarks if on bookmarks view and popup is still open
          if (currentView === 'bookmarks' && bookmarksList) {
            loadBookmarks(true);
          }
          
          // Reset button if popup is still open
          if (saveBtn) {
            saveBtn.disabled = false;
            saveBtn.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path></svg>Save Bookmark';
          }
          
          // Clear status after 3 seconds
          setTimeout(() => {
            if (statusMessage) {
              statusMessage.classList.add('hidden');
            }
          }, 3000);
        } else if (response && response.error) {
          // Show error if popup is still open
          if (statusMessage) {
            if (response.error.includes('Premium') || response.error.includes('premium')) {
              showStatus('Premium subscription required. Please ensure your premium status is synced from the mobile app.', 'error');
            } else {
              showStatus(response.error, 'error');
            }
          }
          
          // Reset button if popup is still open
          if (saveBtn) {
            saveBtn.disabled = false;
            saveBtn.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path></svg>Save Bookmark';
          }
        }
      });
      
    } catch (error) {
      console.error('Save error:', error);
      
      if (statusMessage) {
        if (error.message.includes('PERMISSION_DENIED') || error.message.includes('permission-denied')) {
          showStatus('Premium subscription required. Please ensure your premium status is synced from the mobile app.', 'error');
        } else if (error.message.includes('Premium') || error.message.includes('User document')) {
          showStatus(error.message, 'error');
        } else {
          showStatus('Failed to save bookmark. Please try again.', 'error');
        }
      }
      
      if (saveBtn) {
        saveBtn.disabled = false;
        saveBtn.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path></svg>Save Bookmark';
      }
    }
  });
}

// Switch to manual URL entry mode
if (switchToManualBtn) {
  switchToManualBtn.addEventListener('click', () => {
    if (currentPageMode) currentPageMode.classList.add('hidden');
    if (manualUrlMode) manualUrlMode.classList.remove('hidden');
    if (manualUrlInput) {
      manualUrlInput.focus();
      manualUrlInput.select();
    }
  });
}

// Switch back to current page mode
if (switchToCurrentBtn) {
  switchToCurrentBtn.addEventListener('click', () => {
    if (currentPageMode) currentPageMode.classList.remove('hidden');
    if (manualUrlMode) manualUrlMode.classList.add('hidden');
    if (manualUrlInput) manualUrlInput.value = '';
  });
}

// Save manual URL
if (saveManualUrlBtn && manualUrlInput) {
  // Allow Enter key to save
  manualUrlInput.addEventListener('keypress', (e) => {
    if (e.key === 'Enter') {
      saveManualUrlBtn.click();
    }
  });
  
  saveManualUrlBtn.addEventListener('click', async () => {
    const url = manualUrlInput.value.trim();
    
    if (!url) {
      showStatus('Please enter a URL.', 'error');
      manualUrlInput.focus();
      return;
    }
    
    // Validate URL - add https:// if no protocol
    let finalUrl = url;
    if (!url.startsWith('http://') && !url.startsWith('https://')) {
      finalUrl = 'https://' + url;
    }
    
    if (!isValidUrl(finalUrl)) {
      showStatus('Invalid URL. Please enter a valid URL (e.g., example.com or https://example.com).', 'error');
      manualUrlInput.focus();
      return;
    }
    
    const user = await window.firebaseAuth.getCurrentUser();
    if (!user) {
      showStatus('Please sign in first.', 'error');
      return;
    }
    
    let idToken = await window.firebaseAuth.getIdToken();
    if (!idToken) {
      showStatus('Not authenticated. Please sign in again.', 'error');
      return;
    }
    
    // Verify premium status
    try {
      let currentPremiumStatus = await window.firestoreAPI.checkPremiumStatus(user.uid, idToken);
      if (!currentPremiumStatus) {
        isPremium = false;
        showStatus('Premium subscription required. Please sign in to the mobile app to sync your premium status.', 'error');
        if (premiumBadge) {
          premiumBadge.classList.add('hidden');
        }
        return;
      }
      isPremium = true;
    } catch (error) {
      console.error('Error verifying premium status:', error);
      if (error.message && error.message.includes('401')) {
        try {
          idToken = await window.firebaseAuth.getIdToken(true);
          if (idToken) {
            const retryPremiumStatus = await window.firestoreAPI.checkPremiumStatus(user.uid, idToken);
            if (!retryPremiumStatus) {
              showStatus('Premium subscription required. Please sign in to the mobile app to sync your premium status.', 'error');
              return;
            }
            isPremium = true;
          } else {
            showStatus('Authentication expired. Please sign out and sign in again.', 'error');
            return;
          }
        } catch (refreshError) {
          showStatus('Unable to verify premium status. Please sign in again.', 'error');
          return;
        }
      } else {
        showStatus('Unable to verify premium status. Please try again.', 'error');
        return;
      }
    }
    
    // Capture title immediately (default to domain)
    let capturedTitle = 'Untitled';
    try {
      const urlObj = new URL(finalUrl);
      capturedTitle = urlObj.host; // Default title to domain
    } catch (e) {
      showStatus('Invalid URL format.', 'error');
      manualUrlInput.focus();
      return;
    }
    
    // Update button state
    if (saveManualUrlBtn) {
      saveManualUrlBtn.disabled = true;
      saveManualUrlBtn.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path></svg>Saving...';
    }
    
    // Send save request to background script so it continues even if popup closes
    try {
      // Store auth info temporarily for background script
      await chrome.storage.local.set({
        pendingManualSave: {
          url: finalUrl,
          title: capturedTitle,
          userId: user.uid,
          idToken: idToken,
          timestamp: Date.now()
        }
      });
      
      // Request background script to handle the save
      chrome.runtime.sendMessage({
        action: 'saveBookmarkFromPopup',
        url: finalUrl,
        title: capturedTitle,
        userId: user.uid,
        idToken: idToken
      }, async (response) => {
        // Handle response (popup might be closed by now, so check if elements exist)
        if (chrome.runtime.lastError) {
          console.error('Error sending message to background:', chrome.runtime.lastError);
          // Fallback: try saving directly in popup
          try {
            let domain = '';
            try {
              const urlObj = new URL(finalUrl);
              domain = urlObj.host;
            } catch (e) {
              if (statusMessage) {
                showStatus('Invalid URL format.', 'error');
              }
              if (saveManualUrlBtn) {
                saveManualUrlBtn.disabled = false;
                saveManualUrlBtn.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path></svg>Save URL';
              }
              return;
            }
            
            let title = capturedTitle;
            let description = '';
            let imageUrl = null;
            
            // Fetch metadata
            try {
              if (window.metadataFetcher && window.metadataFetcher.fetchUrlMetadata) {
                const metadata = await window.metadataFetcher.fetchUrlMetadata(finalUrl);
                if (metadata.title && metadata.title.trim()) {
                  title = metadata.title.trim();
                }
                if (metadata.description && metadata.description.trim()) {
                  description = metadata.description.trim();
                }
                if (metadata.image && metadata.image.trim()) {
                  imageUrl = metadata.image.trim();
                }
              }
            } catch (metadataError) {
              console.warn('Metadata fetching failed, using default title:', metadataError);
            }
            
            // Generate auto tags
            let tags = [];
            if (window.CategorizationService) {
              try {
                tags = window.CategorizationService.getSuggestedTags({
                  url: finalUrl,
                  title: title,
                  description: description || null
                });
                console.log('Generated auto tags:', tags);
              } catch (tagError) {
                console.warn('Tag generation failed:', tagError);
              }
            }
            
            const bookmarkData = {
              t: title,
              u: finalUrl,
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
            
            await window.firestoreAPI.saveBookmark(bookmarkData, user.uid, idToken);
            
            if (statusMessage) {
              showStatus('Bookmark saved successfully!', 'success');
            }
            
            // Clear input and switch back to current page mode
            if (manualUrlInput) manualUrlInput.value = '';
            if (currentPageMode) currentPageMode.classList.remove('hidden');
            if (manualUrlMode) manualUrlMode.classList.add('hidden');
            
            // Refresh bookmarks if on bookmarks view
            if (currentView === 'bookmarks' && bookmarksList) {
              loadBookmarks(true);
            }
            
            if (saveManualUrlBtn) {
              saveManualUrlBtn.disabled = false;
              saveManualUrlBtn.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path></svg>Save URL';
            }
            
            setTimeout(() => {
              if (statusMessage) {
                statusMessage.classList.add('hidden');
              }
            }, 3000);
          } catch (fallbackError) {
            console.error('Fallback save error:', fallbackError);
            if (statusMessage) {
              showStatus('Failed to save bookmark. Please try again.', 'error');
            }
            if (saveManualUrlBtn) {
              saveManualUrlBtn.disabled = false;
              saveManualUrlBtn.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path></svg>Save URL';
            }
          }
          return;
        }
        
        if (response && response.success) {
          // Show success message if popup is still open
          if (statusMessage) {
            showStatus('Bookmark saved successfully!', 'success');
          }
          
          // Clear input and switch back to current page mode
          if (manualUrlInput) manualUrlInput.value = '';
          if (currentPageMode) currentPageMode.classList.remove('hidden');
          if (manualUrlMode) manualUrlMode.classList.add('hidden');
          
          // Refresh bookmarks if on bookmarks view and popup is still open
          if (currentView === 'bookmarks' && bookmarksList) {
            loadBookmarks(true);
          }
          
          // Reset button if popup is still open
          if (saveManualUrlBtn) {
            saveManualUrlBtn.disabled = false;
            saveManualUrlBtn.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path></svg>Save URL';
          }
          
          // Clear status after 3 seconds
          setTimeout(() => {
            if (statusMessage) {
              statusMessage.classList.add('hidden');
            }
          }, 3000);
        } else if (response && response.error) {
          // Show error if popup is still open
          if (statusMessage) {
            if (response.error.includes('Premium') || response.error.includes('premium')) {
              showStatus('Premium subscription required. Please ensure your premium status is synced from the mobile app.', 'error');
            } else {
              showStatus(response.error, 'error');
            }
          }
          
          // Reset button if popup is still open
          if (saveManualUrlBtn) {
            saveManualUrlBtn.disabled = false;
            saveManualUrlBtn.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path></svg>Save URL';
          }
        }
      });
      
    } catch (error) {
      console.error('Save manual URL error:', error);
      
      if (statusMessage) {
        if (error.message.includes('PERMISSION_DENIED') || error.message.includes('permission-denied')) {
          showStatus('Premium subscription required. Please ensure your premium status is synced from the mobile app.', 'error');
        } else if (error.message.includes('Premium') || error.message.includes('User document')) {
          showStatus(error.message, 'error');
        } else {
          showStatus('Failed to save bookmark. Please try again.', 'error');
        }
      }
      
      if (saveManualUrlBtn) {
        saveManualUrlBtn.disabled = false;
        saveManualUrlBtn.innerHTML = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path></svg>Save URL';
      }
    }
  });
}

// Show status message
function showStatus(message, type = 'info') {
  if (statusMessage) {
    statusMessage.textContent = message;
    statusMessage.className = `status-message ${type}`;
    statusMessage.classList.remove('hidden');
  }
}

// Switch between views
function switchView(view) {
  currentView = view;
  
  // Update tabs
  if (homeTab) {
    homeTab.classList.toggle('active', view === 'home');
  }
  if (bookmarksTab) {
    bookmarksTab.classList.toggle('active', view === 'bookmarks');
  }
  
  // Update views
  if (homeView) {
    homeView.classList.toggle('hidden', view !== 'home');
  }
  if (bookmarksView) {
    bookmarksView.classList.toggle('hidden', view !== 'bookmarks');
  }
  
  // Load bookmarks if switching to bookmarks view (use cache if available)
  if (view === 'bookmarks') {
    loadBookmarks(false); // Don't force refresh, use cache if available
  }
}

// Load bookmarks from Firestore
async function loadBookmarks(forceRefresh = false) {
  const user = await window.firebaseAuth.getCurrentUser();
  if (!user) {
    return;
  }
  
  // Check cache first (unless force refresh)
  // NOTE: We don't use cache if it might contain deleted bookmarks
  // Always fetch fresh data to ensure deleted bookmarks are filtered out
  if (!forceRefresh && cachedBookmarks && cacheTimestamp) {
    const cacheAge = Date.now() - cacheTimestamp;
    // Reduce cache duration to 30 seconds to ensure deletions are reflected quickly
    const SHORT_CACHE_DURATION = 30 * 1000; // 30 seconds
    if (cacheAge < SHORT_CACHE_DURATION) {
      console.log('Using cached bookmarks (age:', Math.round(cacheAge / 1000), 'seconds)');
      renderBookmarks(cachedBookmarks);
      return;
    }
  }
  
  const idToken = await window.firebaseAuth.getIdToken();
  if (!idToken) {
    return;
  }
  
  try {
    // Show loading state
    bookmarksList.innerHTML = '<div class="empty-state"><div class="spinner"></div><p>Loading bookmarks...</p></div>';
    
    // Get FIREBASE_PROJECT_ID from auth.js
    const FIREBASE_PROJECT_ID = window.FIREBASE_PROJECT_ID || 'save-it-later-dd29e';
    
    // Load bookmarks from TWO sources (like web app):
    // 1. Direct Firestore bookmarks (bookmarks/{userId}/items)
    // 2. Backup documents (backups/{userId}/user_backups)
    
    let allBookmarks = [];
    const bookmarksMap = new Map(); // Use URL as key to deduplicate
    
    // ============================================
    // 1. Load direct Firestore bookmarks
    // ============================================
    const parentPath = `projects/${FIREBASE_PROJECT_ID}/databases/(default)/documents/bookmarks/${user.uid}`;
    
    let allDocuments = [];
    let pageToken = null;
    let pageCount = 0;
    const maxPages = 50;
    
    do {
      let url = `https://firestore.googleapis.com/v1/${parentPath}/items?pageSize=1000`;
      if (pageToken) {
        url += `&pageToken=${encodeURIComponent(pageToken)}`;
      }
      
      console.log(`Fetching Firestore bookmarks page ${pageCount + 1}...`);
      
      const response = await fetch(url, {
        method: 'GET',
        headers: {
          'Authorization': `Bearer ${idToken}`
        }
      });
      
      if (!response.ok) {
        const errorText = await response.text();
        console.error('Firestore error:', errorText);
        // Don't throw - continue to load from backups
        break;
      }
      
      const data = await response.json();
      const documents = data.documents || [];
      allDocuments = allDocuments.concat(documents);
      
      pageToken = data.nextPageToken || null;
      pageCount++;
      
      if (pageToken && pageCount < maxPages) {
        bookmarksList.innerHTML = `<div class="empty-state"><div class="spinner"></div><p>Loading Firestore bookmarks... (${allDocuments.length} loaded)</p></div>`;
      }
    } while (pageToken && pageCount < maxPages);
    
    console.log(`✅ Loaded ${allDocuments.length} Firestore bookmarks in ${pageCount} page(s)`);
    
    // Track URLs that exist in Firestore (for filtering backup items)
    // Normalize URLs for comparison (lowercase, remove trailing slash, etc.)
    const firestoreBookmarksUrls = new Set();
    const normalizeUrl = (url) => {
      if (!url) return '';
      try {
        // Normalize URL: lowercase, remove trailing slash, remove fragment
        const urlObj = new URL(url);
        urlObj.hash = ''; // Remove fragment
        let normalized = urlObj.toString().toLowerCase();
        // Remove trailing slash
        if (normalized.endsWith('/')) {
          normalized = normalized.slice(0, -1);
        }
        return normalized;
      } catch (e) {
        // If URL parsing fails, just lowercase and trim
        return url.toLowerCase().trim().replace(/\/$/, '');
      }
    };
    
    // Parse Firestore bookmarks
    for (const doc of allDocuments) {
      const fields = doc.fields || {};
      const url = fields.u?.stringValue || '';
      if (!url) continue;
      
      // Track normalized URL as existing in Firestore
      const normalizedUrl = normalizeUrl(url);
      firestoreBookmarksUrls.add(normalizedUrl);
      
      // Parse createdAt - PRIORITIZE 'ca' (integer milliseconds) from mobile app
      // Mobile app saves 'ca' as integer (milliseconds), Chrome extension saves 'createdAt' as timestamp
      // We want the ORIGINAL date, so check 'ca' FIRST
      let createdAt = null;
      
      // PRIORITY 1: 'ca' as integer (milliseconds) - this is what mobile app uses and preserves original date
      if (fields.ca?.integerValue !== undefined) {
        const ms = parseInt(fields.ca.integerValue);
        if (!isNaN(ms) && ms > 0) {
          createdAt = new Date(ms).toISOString();
        }
      } 
      // PRIORITY 2: 'ca' as string (milliseconds) - fallback format
      else if (fields.ca?.stringValue) {
        const ms = parseInt(fields.ca.stringValue);
        if (!isNaN(ms) && ms > 0) {
          createdAt = new Date(ms).toISOString();
        }
      }
      // PRIORITY 3: 'createdAt' as integer (milliseconds) - if somehow saved as integer
      else if (fields.createdAt?.integerValue !== undefined) {
        const ms = parseInt(fields.createdAt.integerValue);
        if (!isNaN(ms) && ms > 0) {
          createdAt = new Date(ms).toISOString();
        }
      }
      // PRIORITY 4: 'ca' as timestamp (ISO string) - Chrome extension format
      else if (fields.ca?.timestampValue) {
        createdAt = fields.ca.timestampValue; // ISO string from Timestamp
      }
      // PRIORITY 5: 'createdAt' as timestamp (ISO string) - Chrome extension format
      else if (fields.createdAt?.timestampValue) {
        createdAt = fields.createdAt.timestampValue; // ISO string from Timestamp
      }
      
      // Parse updatedAt - PRIORITIZE 'ua' (integer milliseconds) from mobile app
      let updatedAt = null;
      
      // PRIORITY 1: 'ua' as integer (milliseconds) - this is what mobile app uses
      if (fields.ua?.integerValue !== undefined) {
        const ms = parseInt(fields.ua.integerValue);
        if (!isNaN(ms) && ms > 0) {
          updatedAt = new Date(ms).toISOString();
        }
      }
      // PRIORITY 2: 'ua' as string (milliseconds) - fallback format
      else if (fields.ua?.stringValue) {
        const ms = parseInt(fields.ua.stringValue);
        if (!isNaN(ms) && ms > 0) {
          updatedAt = new Date(ms).toISOString();
        }
      }
      // PRIORITY 3: 'updatedAt' as integer (milliseconds)
      else if (fields.updatedAt?.integerValue !== undefined) {
        const ms = parseInt(fields.updatedAt.integerValue);
        if (!isNaN(ms) && ms > 0) {
          updatedAt = new Date(ms).toISOString();
        }
      }
      // PRIORITY 4: 'ua' as timestamp (ISO string) - Chrome extension format
      else if (fields.ua?.timestampValue) {
        updatedAt = fields.ua.timestampValue;
      }
      // PRIORITY 5: 'updatedAt' as timestamp (ISO string) - Chrome extension format
      else if (fields.updatedAt?.timestampValue) {
        updatedAt = fields.updatedAt.timestampValue;
      }
      
      // Fallback to createdAt if updatedAt is missing
      if (!updatedAt && createdAt) {
        updatedAt = createdAt;
      }
      
      // Log date parsing for debugging
      if (!createdAt) {
        console.warn('[BOOKMARKS] Missing createdAt for bookmark:', {
          url: url,
          hasCreatedAt: !!fields.createdAt,
          hasCa: !!fields.ca,
          createdAtType: fields.createdAt ? Object.keys(fields.createdAt)[0] : null,
          caType: fields.ca ? Object.keys(fields.ca)[0] : null,
          fields: Object.keys(fields)
        });
      } else {
        // Log successful date parsing (for debugging date issues)
        console.log('[BOOKMARKS] Parsed createdAt:', {
          url: url.substring(0, 50),
          createdAt: createdAt,
          source: fields.createdAt?.timestampValue ? 'createdAt.timestampValue' :
                  fields.ca?.timestampValue ? 'ca.timestampValue' :
                  fields.ca?.integerValue ? 'ca.integerValue' :
                  fields.createdAt?.integerValue ? 'createdAt.integerValue' : 'unknown'
        });
      }
      
      // Parse tags (comma-separated string)
      const tagsStr = fields.tags?.stringValue || '';
      const tags = tagsStr ? tagsStr.split(',').map(tag => tag.trim()).filter(tag => tag.length > 0) : [];
      
      const bookmark = {
        id: doc.name.split('/').pop(),
        title: fields.t?.stringValue || 'Untitled',
        url: url,
        domain: fields.dom?.stringValue || '',
        description: fields.d?.stringValue || null,
        imageUrl: fields.img?.stringValue || fields.imageUrl?.stringValue || null,
        tags: tags,
        createdAt: createdAt,
        updatedAt: updatedAt
      };
      
      // Add to map (URL as key, most recent wins)
      if (!bookmarksMap.has(url) || 
          (bookmark.updatedAt && bookmarksMap.get(url).updatedAt && 
           new Date(bookmark.updatedAt) > new Date(bookmarksMap.get(url).updatedAt))) {
        bookmarksMap.set(url, bookmark);
      }
    }
    
    // ============================================
    // 2. Load bookmarks from backups
    // ============================================
    bookmarksList.innerHTML = `<div class="empty-state"><div class="spinner"></div><p>Loading from backups... (${bookmarksMap.size} from Firestore)</p></div>`;
    
    const backupsPath = `projects/${FIREBASE_PROJECT_ID}/databases/(default)/documents/backups/${user.uid}/user_backups`;
    
    // Get all backups
    let allBackups = [];
    pageToken = null;
    pageCount = 0;
    
    do {
      // Note: Firestore REST API doesn't support orderBy in listDocuments
      // We'll sort client-side instead
      let url = `https://firestore.googleapis.com/v1/${backupsPath}?pageSize=1000`;
      if (pageToken) {
        url += `&pageToken=${encodeURIComponent(pageToken)}`;
      }
      
      console.log(`Fetching backups page ${pageCount + 1}...`);
      
      const response = await fetch(url, {
        method: 'GET',
        headers: {
          'Authorization': `Bearer ${idToken}`
        }
      });
      
      if (!response.ok) {
        console.warn('Failed to load backups:', response.status);
        break;
      }
      
      const data = await response.json();
      const backups = data.documents || [];
      allBackups = allBackups.concat(backups);
      
      pageToken = data.nextPageToken || null;
      pageCount++;
      
      if (pageToken && pageCount < maxPages) {
        bookmarksList.innerHTML = `<div class="empty-state"><div class="spinner"></div><p>Loading backups... (${allBackups.length} backups, ${bookmarksMap.size} bookmarks)</p></div>`;
      }
    } while (pageToken && pageCount < maxPages);
    
    console.log(`✅ Loaded ${allBackups.length} backup(s)`);
    
    // Sort backups by createdAt descending (most recent first)
    allBackups.sort((a, b) => {
      const aCreated = a.fields?.createdAt?.timestampValue || a.fields?.ca?.timestampValue || '';
      const bCreated = b.fields?.createdAt?.timestampValue || b.fields?.ca?.timestampValue || '';
      if (!aCreated) return 1;
      if (!bCreated) return -1;
      return new Date(bCreated) - new Date(aCreated);
    });
    
    // Parse items from each backup
    for (const backupDoc of allBackups) {
      const backupFields = backupDoc.fields || {};
      const isChunked = backupFields.isChunked?.booleanValue === true || backupFields.ch?.booleanValue === true;
      
      let items = [];
      
      if (isChunked) {
        // Load chunks
        const backupId = backupDoc.name.split('/').pop();
        const chunksPath = `${backupsPath}/${backupId}/chunks`;
        
        let allChunks = [];
        pageToken = null;
        let chunkPageCount = 0;
        
        do {
          // Note: Firestore REST API doesn't support orderBy in listDocuments
          // We'll sort chunks client-side by 'ci' field
          let url = `https://firestore.googleapis.com/v1/${chunksPath}?pageSize=1000`;
          if (pageToken) {
            url += `&pageToken=${encodeURIComponent(pageToken)}`;
          }
          
          const response = await fetch(url, {
            method: 'GET',
            headers: {
              'Authorization': `Bearer ${idToken}`
            }
          });
          
          if (!response.ok) break;
          
          const data = await response.json();
          const chunks = data.documents || [];
          allChunks = allChunks.concat(chunks);
          
          pageToken = data.nextPageToken || null;
          chunkPageCount++;
        } while (pageToken && chunkPageCount < maxPages);
        
        // Sort chunks by 'ci' (chunk index) field
        allChunks.sort((a, b) => {
          const aIndex = a.fields?.ci?.integerValue || a.fields?.chunkIndex?.integerValue || 0;
          const bIndex = b.fields?.ci?.integerValue || b.fields?.chunkIndex?.integerValue || 0;
          return parseInt(aIndex) - parseInt(bIndex);
        });
        
        // Extract items from chunks
        for (const chunkDoc of allChunks) {
          const chunkFields = chunkDoc.fields || {};
          const chunkItems = chunkFields.i?.arrayValue?.values || [];
          items = items.concat(chunkItems.map(v => parseFirestoreValue(v)));
        }
      } else {
        // Single document backup
        const dataField = backupFields.data?.mapValue?.fields || {};
        const itemsField = dataField.i?.arrayValue?.values || [];
        items = itemsField.map(v => parseFirestoreValue(v));
      }
      
      // Parse items and add to map
      for (const item of items) {
        if (!item || typeof item !== 'object') continue;
        
        const url = item.u || item.url || '';
        if (!url) continue;
        
        // Show ALL backup bookmarks for better UX
        // This provides maximum data recovery and predictable behavior
        // Users can manually delete bookmarks they don't want to see
        // Note: Firestore bookmarks take precedence (loaded first), so if a bookmark
        // exists in both Firestore and backups, the Firestore version is used
        const normalizedUrl = normalizeUrl(url);
        
        // Parse createdAt - can be number (milliseconds), ISO string, or Firestore timestamp
        // IMPORTANT: Don't use Date.now() as fallback - preserve original dates or use null
        let createdAt = item.ca !== undefined ? item.ca : (item.createdAt !== undefined ? item.createdAt : null);
        let updatedAt = item.ua !== undefined ? item.ua : (item.updatedAt !== undefined ? item.updatedAt : null);
        
        // Normalize dates to ISO strings for consistent sorting
        // Only normalize if we have a valid date value
        if (createdAt !== null && createdAt !== undefined) {
          if (typeof createdAt === 'number' && createdAt > 0) {
            // Only convert if it's a valid timestamp (greater than 0)
            createdAt = new Date(createdAt).toISOString();
          } else if (typeof createdAt === 'string') {
            if (createdAt.includes('T')) {
              // Already ISO format
              createdAt = createdAt;
            } else {
              // Try to parse it
              const parsed = new Date(createdAt);
              if (!isNaN(parsed.getTime()) && parsed.getTime() > 0) {
                createdAt = parsed.toISOString();
              } else {
                // Invalid date string, set to null
                createdAt = null;
              }
            }
          } else {
            // Invalid type, set to null
            createdAt = null;
          }
        } else {
          createdAt = null;
        }
        
        // Use createdAt as fallback for updatedAt only if updatedAt is missing
        if (updatedAt === null || updatedAt === undefined) {
          updatedAt = createdAt;
        } else {
          if (typeof updatedAt === 'number' && updatedAt > 0) {
            updatedAt = new Date(updatedAt).toISOString();
          } else if (typeof updatedAt === 'string') {
            if (updatedAt.includes('T')) {
              updatedAt = updatedAt;
            } else {
              const parsed = new Date(updatedAt);
              if (!isNaN(parsed.getTime()) && parsed.getTime() > 0) {
                updatedAt = parsed.toISOString();
              } else {
                updatedAt = createdAt; // Fallback to createdAt if invalid
              }
            }
          } else {
            updatedAt = createdAt; // Fallback to createdAt if invalid
          }
        }
        
        // Final fallback: if both are null, we can't determine the date, so leave it null
        // This will be handled in the display logic
        
        // Parse tags (comma-separated string or array)
        let tags = [];
        if (item.tags) {
          if (typeof item.tags === 'string') {
            tags = item.tags.split(',').map(tag => tag.trim()).filter(tag => tag.length > 0);
          } else if (Array.isArray(item.tags)) {
            tags = item.tags.filter(tag => tag && tag.trim().length > 0);
          }
        }
        
        const bookmark = {
          id: null, // Backup items don't have document IDs
          title: item.t || item.title || 'Untitled',
          url: url,
          domain: item.dom || item.domain || '',
          description: item.d || item.description || null,
          imageUrl: item.img || item.imageUrl || null,
          tags: tags,
          createdAt: createdAt,
          updatedAt: updatedAt
        };
        
        // Add to map (URL as key, most recent wins)
        if (!bookmarksMap.has(url) || 
            (bookmark.updatedAt && bookmarksMap.get(url).updatedAt && 
             new Date(bookmark.updatedAt) > new Date(bookmarksMap.get(url).updatedAt))) {
          bookmarksMap.set(url, bookmark);
        }
      }
    }
    
    // Convert map to array
    allBookmarks = Array.from(bookmarksMap.values());
    
    console.log(`✅ Total bookmarks loaded: ${allBookmarks.length} (${allDocuments.length} from Firestore, ${allBookmarks.length - allDocuments.length} from backups)`);
    
    // Helper function to parse Firestore values
    function parseFirestoreValue(value) {
      if (!value) return null;
      if (value.stringValue !== undefined) return value.stringValue;
      if (value.integerValue !== undefined) return parseInt(value.integerValue);
      if (value.doubleValue !== undefined) return parseFloat(value.doubleValue);
      if (value.booleanValue !== undefined) return value.booleanValue;
      if (value.timestampValue !== undefined) return value.timestampValue;
      if (value.mapValue?.fields) {
        const result = {};
        for (const [key, val] of Object.entries(value.mapValue.fields)) {
          result[key] = parseFirestoreValue(val);
        }
        return result;
      }
      if (value.arrayValue?.values) {
        return value.arrayValue.values.map(v => parseFirestoreValue(v));
      }
      return null;
    }
    
    // Don't pre-sort here - let filterAndSortBookmarks handle sorting based on currentSortOption
    // This ensures the sort dropdown and actual sorting stay in sync
    const bookmarks = allBookmarks;
    
    if (bookmarks.length === 0) {
      bookmarksList.innerHTML = `
        <div class="empty-state">
          <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" opacity="0.3">
            <path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path>
          </svg>
          <p>No bookmarks yet</p>
          <p class="empty-subtitle">Save pages to see them here</p>
        </div>
      `;
      return;
    }
    
    // Cache the bookmarks
    cachedBookmarks = bookmarks;
    cacheTimestamp = Date.now();
    
    // Save to chrome.storage for persistence across popup opens
    try {
      await chrome.storage.local.set({
        cachedBookmarks: bookmarks,
        cacheTimestamp: cacheTimestamp
      });
    } catch (error) {
      console.warn('Failed to save cache to storage:', error);
    }
    
    // Render bookmarks (will be sorted by filterAndSortBookmarks)
    renderBookmarks(bookmarks);
    
  } catch (error) {
    console.error('Error loading bookmarks:', error);
    bookmarksList.innerHTML = `
      <div class="empty-state">
        <p>Failed to load bookmarks</p>
        <p class="empty-subtitle">${error.message}</p>
      </div>
    `;
  }
}

// Filter and sort bookmarks
function filterAndSortBookmarks(bookmarks) {
  let filtered = [...bookmarks];
  
  // Apply search filter
  if (currentSearchQuery.trim()) {
    const query = currentSearchQuery.toLowerCase().trim();
    filtered = filtered.filter(bookmark => {
      const title = (bookmark.title || '').toLowerCase();
      const url = (bookmark.url || '').toLowerCase();
      const domain = (bookmark.domain || '').toLowerCase();
      return title.includes(query) || url.includes(query) || domain.includes(query);
    });
  }
  
  // Apply sort
  filtered.sort((a, b) => {
    switch (currentSortOption) {
      case 'dateNewest':
        // Use createdAt to match mobile app behavior
        // Parse dates more robustly - handle ISO strings, timestamps, and invalid dates
        let aDate = 0;
        let bDate = 0;
        
        if (a.createdAt) {
          // Try parsing as Date
          const parsed = new Date(a.createdAt);
          if (!isNaN(parsed.getTime())) {
            aDate = parsed.getTime();
          } else {
            // If parsing fails, try as number (milliseconds)
            const num = typeof a.createdAt === 'number' ? a.createdAt : parseInt(a.createdAt);
            if (!isNaN(num) && num > 0) {
              aDate = num;
            }
          }
        }
        
        if (b.createdAt) {
          // Try parsing as Date
          const parsed = new Date(b.createdAt);
          if (!isNaN(parsed.getTime())) {
            bDate = parsed.getTime();
          } else {
            // If parsing fails, try as number (milliseconds)
            const num = typeof b.createdAt === 'number' ? b.createdAt : parseInt(b.createdAt);
            if (!isNaN(num) && num > 0) {
              bDate = num;
            }
          }
        }
        
        // If dates are equal or both 0, maintain original order
        if (aDate === bDate) return 0;
        
        return bDate - aDate; // Descending (newest first)
      case 'dateOldest':
        // Use createdAt to match mobile app behavior
        // Parse dates more robustly
        let aDateOld = 0;
        let bDateOld = 0;
        
        if (a.createdAt) {
          const parsed = new Date(a.createdAt);
          if (!isNaN(parsed.getTime())) {
            aDateOld = parsed.getTime();
          } else {
            const num = typeof a.createdAt === 'number' ? a.createdAt : parseInt(a.createdAt);
            if (!isNaN(num) && num > 0) {
              aDateOld = num;
            }
          }
        }
        
        if (b.createdAt) {
          const parsed = new Date(b.createdAt);
          if (!isNaN(parsed.getTime())) {
            bDateOld = parsed.getTime();
          } else {
            const num = typeof b.createdAt === 'number' ? b.createdAt : parseInt(b.createdAt);
            if (!isNaN(num) && num > 0) {
              bDateOld = num;
            }
          }
        }
        
        // If dates are equal or both 0, maintain original order
        if (aDateOld === bDateOld) return 0;
        
        return aDateOld - bDateOld; // Ascending (oldest first)
      case 'titleAZ':
        return (a.title || '').localeCompare(b.title || '');
      case 'titleZA':
        return (b.title || '').localeCompare(a.title || '');
      case 'urlAZ':
        return (a.url || '').localeCompare(b.url || '');
      case 'urlZA':
        return (b.url || '').localeCompare(a.url || '');
      default:
        return 0;
    }
  });
  
  return filtered;
}

// Render bookmarks list
function renderBookmarks(bookmarks) {
  // Store all bookmarks for filtering/sorting
  allBookmarksData = bookmarks;
  
  // Apply filters and sorting
  const filteredBookmarks = filterAndSortBookmarks(bookmarks);
  
  // Render bookmarks (show count in header)
  const bookmarksHeader = document.querySelector('.bookmarks-header .section-title');
  if (bookmarksHeader) {
    const totalCount = bookmarks.length;
    const filteredCount = filteredBookmarks.length;
    if (currentSearchQuery.trim() && filteredCount !== totalCount) {
      bookmarksHeader.textContent = `My Bookmarks (${filteredCount}/${totalCount})`;
    } else {
      bookmarksHeader.textContent = `My Bookmarks (${totalCount})`;
    }
  }
  
  if (filteredBookmarks.length === 0) {
    if (currentSearchQuery.trim()) {
      bookmarksList.innerHTML = `
        <div class="empty-state">
          <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" opacity="0.3">
            <circle cx="11" cy="11" r="8"></circle>
            <path d="m21 21-4.35-4.35"></path>
          </svg>
          <p>No bookmarks found</p>
          <p class="empty-subtitle">Try a different search term</p>
        </div>
      `;
    } else {
      bookmarksList.innerHTML = `
        <div class="empty-state">
          <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" opacity="0.3">
            <path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path>
          </svg>
          <p>No bookmarks yet</p>
          <p class="empty-subtitle">Save pages to see them here</p>
        </div>
      `;
    }
    return;
  }
  
  // Render all bookmarks with images
  bookmarksList.innerHTML = filteredBookmarks.map(bookmark => {
    const imageHtml = bookmark.imageUrl 
      ? `<img src="${escapeHtml(bookmark.imageUrl)}" alt="${escapeHtml(bookmark.title)}" class="bookmark-item-image">
         <svg class="bookmark-item-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="display: none;">
           <path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path>
         </svg>`
      : `<svg class="bookmark-item-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
           <path d="M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z"></path>
         </svg>`;
    
    return `
      <div class="bookmark-item" data-url="${escapeHtml(bookmark.url)}" data-id="${escapeHtml(bookmark.id || '')}">
        <div class="bookmark-item-image-wrapper">
          ${imageHtml}
        </div>
        <div class="bookmark-item-content">
          <p class="bookmark-item-title">${escapeHtml(bookmark.title)}</p>
          <p class="bookmark-item-url">${escapeHtml(bookmark.url)}</p>
          ${bookmark.description && bookmark.description.trim() ? `<p class="bookmark-item-description">${escapeHtml(bookmark.description)}</p>` : ''}
          ${bookmark.tags && bookmark.tags.length > 0 ? `<div class="bookmark-item-tags">${bookmark.tags.map(tag => `<span class="bookmark-tag">${escapeHtml(tag)}</span>`).join('')}</div>` : ''}
          ${bookmark.createdAt ? `<p class="bookmark-item-date">${escapeHtml(formatBookmarkDate(bookmark.createdAt))}</p>` : ''}
        </div>
        <div class="bookmark-item-actions">
          <button class="bookmark-action-btn bookmark-edit-btn" data-id="${escapeHtml(bookmark.id || '')}" title="Edit">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"></path>
              <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"></path>
            </svg>
          </button>
          <button class="bookmark-action-btn bookmark-delete-btn" data-id="${escapeHtml(bookmark.id || '')}" title="Delete">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polyline points="3 6 5 6 21 6"></polyline>
              <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
            </svg>
          </button>
        </div>
      </div>
    `;
  }).join('');
  
  // Add click handlers for opening bookmarks
  document.querySelectorAll('.bookmark-item').forEach(item => {
    const content = item.querySelector('.bookmark-item-content');
    if (content) {
      content.addEventListener('click', () => {
        const url = item.getAttribute('data-url');
        if (url) {
          chrome.tabs.create({ url });
        }
      });
    }
    
    // Prevent click propagation on image wrapper and actions
    const imageWrapper = item.querySelector('.bookmark-item-image-wrapper');
    if (imageWrapper) {
      imageWrapper.addEventListener('click', (e) => {
        e.stopPropagation();
      });
    }
    
    const actions = item.querySelector('.bookmark-item-actions');
    if (actions) {
      actions.addEventListener('click', (e) => {
        e.stopPropagation();
      });
    }
    
    // Handle image errors
    const img = item.querySelector('.bookmark-item-image');
    if (img) {
      img.addEventListener('error', function() {
        this.style.display = 'none';
        const icon = this.nextElementSibling;
        if (icon) {
          icon.style.display = 'block';
        }
      });
    }
  });
  
  // Add edit button handlers
  document.querySelectorAll('.bookmark-edit-btn').forEach(btn => {
    btn.addEventListener('click', async (e) => {
      e.stopPropagation();
      const bookmarkId = btn.getAttribute('data-id');
      const bookmark = allBookmarksData.find(b => b.id === bookmarkId);
      if (bookmark) {
        await showEditBookmarkDialog(bookmark);
      }
    });
  });
  
  // Add delete button handlers
  document.querySelectorAll('.bookmark-delete-btn').forEach(btn => {
    btn.addEventListener('click', async (e) => {
      e.stopPropagation();
      const bookmarkId = btn.getAttribute('data-id');
      const bookmark = allBookmarksData.find(b => b.id === bookmarkId);
      if (bookmark) {
        await deleteBookmark(bookmark);
      }
    });
  });
}

// Escape HTML to prevent XSS
function escapeHtml(text) {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

// Format date and time for display
function formatBookmarkDate(dateString) {
  if (!dateString) return '';
  
  try {
    const date = new Date(dateString);
    if (isNaN(date.getTime())) return '';
    
    // Format: "Jan 15, 2024 • 3:45 PM"
    const dateOptions = { 
      year: 'numeric', 
      month: 'short', 
      day: 'numeric'
    };
    
    const timeOptions = {
      hour: 'numeric',
      minute: '2-digit',
      hour12: true
    };
    
    const dateStr = date.toLocaleDateString('en-US', dateOptions);
    const timeStr = date.toLocaleTimeString('en-US', timeOptions);
    
    return `${dateStr} • ${timeStr}`;
  } catch (e) {
    return '';
  }
}

// Event listeners for navigation
if (homeTab) {
  homeTab.addEventListener('click', () => switchView('home'));
}
if (bookmarksTab) {
  bookmarksTab.addEventListener('click', () => switchView('bookmarks'));
}
if (refreshBookmarksBtn) {
  refreshBookmarksBtn.addEventListener('click', () => loadBookmarks(true)); // Force refresh
}
if (viewBookmarksBtn) {
  viewBookmarksBtn.addEventListener('click', () => switchView('bookmarks'));
}

// Search functionality
if (bookmarkSearch) {
  bookmarkSearch.addEventListener('input', (e) => {
    currentSearchQuery = e.target.value;
    if (clearSearchBtn) {
      clearSearchBtn.classList.toggle('hidden', !currentSearchQuery.trim());
    }
    if (allBookmarksData.length > 0) {
      renderBookmarks(allBookmarksData);
    }
  });
}

if (clearSearchBtn) {
  clearSearchBtn.addEventListener('click', () => {
    if (bookmarkSearch) {
      bookmarkSearch.value = '';
    }
    currentSearchQuery = '';
    clearSearchBtn.classList.add('hidden');
    if (allBookmarksData.length > 0) {
      renderBookmarks(allBookmarksData);
    }
  });
}

// Sort functionality
if (bookmarkSort) {
  bookmarkSort.addEventListener('change', (e) => {
    currentSortOption = e.target.value;
    // Save sort option to storage
    try {
      chrome.storage.local.set({ sortOption: currentSortOption });
    } catch (error) {
      console.warn('Failed to save sort option:', error);
    }
    if (allBookmarksData.length > 0) {
      renderBookmarks(allBookmarksData);
    }
  });
}

// Open app button
if (openAppBtn) {
  openAppBtn.addEventListener('click', () => {
    chrome.tabs.create({ url: 'https://save-it-later-dd29e.web.app' });
  });
}

// Open landing page button (from quick actions)
if (openLandingPageBtn) {
  openLandingPageBtn.addEventListener('click', () => {
    chrome.tabs.create({ url: 'https://save-it-later.vercel.app/' });
  });
}

// Open landing page button (from signed-out view)
if (openLandingPageBtnSignedOut) {
  openLandingPageBtnSignedOut.addEventListener('click', () => {
    chrome.tabs.create({ url: 'https://save-it-later.vercel.app/' });
  });
}

// Open full-screen button (from home)
if (openFullScreenBtn) {
  openFullScreenBtn.addEventListener('click', () => {
    chrome.tabs.create({ url: chrome.runtime.getURL('bookmarks.html') });
  });
}

// Open full-screen button (from bookmarks view)
if (openFullScreenFromBookmarksBtn) {
  openFullScreenFromBookmarksBtn.addEventListener('click', () => {
    chrome.tabs.create({ url: chrome.runtime.getURL('bookmarks.html') });
  });
}

// Auto-save toggle
if (autoSaveToggle) {
  autoSaveToggle.addEventListener('change', async (e) => {
    const enabled = e.target.checked;
    try {
      await chrome.storage.local.set({ autoSaveEnabled: enabled });
      // Notify background script of the change
      chrome.runtime.sendMessage({ action: 'autoSaveSettingChanged', enabled: enabled });
      showStatus(enabled ? 'Auto-save enabled' : 'Auto-save disabled', 'success');
      setTimeout(() => {
        if (statusMessage) {
          statusMessage.classList.add('hidden');
        }
      }, 2000);
    } catch (error) {
      console.error('Failed to save auto-save setting:', error);
      showStatus('Failed to save setting', 'error');
    }
  });
}

// Show edit bookmark dialog
async function showEditBookmarkDialog(bookmark) {
  const user = await window.firebaseAuth.getCurrentUser();
  if (!user) {
    showStatus('Please sign in first.', 'error');
    return;
  }
  
  // Create dialog HTML
  const dialog = document.createElement('div');
  dialog.className = 'edit-dialog-overlay';
  dialog.innerHTML = `
    <div class="edit-dialog">
      <div class="edit-dialog-header">
        <h3>Edit Bookmark</h3>
        <button class="edit-dialog-close" title="Close">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <line x1="18" y1="6" x2="6" y2="18"></line>
            <line x1="6" y1="6" x2="18" y2="18"></line>
          </svg>
        </button>
      </div>
      <div class="edit-dialog-content">
        <div class="edit-field">
          <label>Title</label>
          <input type="text" id="editBookmarkTitle" value="${escapeHtml(bookmark.title || '')}" placeholder="Bookmark title">
        </div>
        <div class="edit-field">
          <label>URL</label>
          <input type="text" id="editBookmarkUrl" value="${escapeHtml(bookmark.url || '')}" placeholder="https://example.com">
        </div>
        <div class="edit-field">
          <label>Description (optional)</label>
          <textarea id="editBookmarkDescription" placeholder="Description">${escapeHtml(bookmark.description || '')}</textarea>
        </div>
      </div>
      <div class="edit-dialog-actions">
        <button class="btn btn-secondary" id="editBookmarkCancel">Cancel</button>
        <button class="btn btn-primary" id="editBookmarkSave">Save</button>
      </div>
    </div>
  `;
  
  document.body.appendChild(dialog);
  
  // Close handlers
  const closeDialog = () => {
    if (dialog && dialog.parentNode) {
      document.body.removeChild(dialog);
    }
  };
  
  const closeBtn = dialog.querySelector('.edit-dialog-close');
  if (closeBtn) {
    closeBtn.addEventListener('click', closeDialog);
  }
  
  const cancelBtn = dialog.querySelector('#editBookmarkCancel');
  if (cancelBtn) {
    cancelBtn.addEventListener('click', closeDialog);
  }
  
  const overlay = dialog.querySelector('.edit-dialog-overlay');
  if (overlay) {
    overlay.addEventListener('click', (e) => {
      if (e.target === dialog) closeDialog();
    });
  }
  
  // Save handler
  const dialogSaveBtn = dialog.querySelector('#editBookmarkSave');
  if (dialogSaveBtn) {
    dialogSaveBtn.addEventListener('click', async () => {
      const titleInput = dialog.querySelector('#editBookmarkTitle');
      const urlInput = dialog.querySelector('#editBookmarkUrl');
      const descriptionInput = dialog.querySelector('#editBookmarkDescription');
      
      if (!titleInput || !urlInput) {
        showStatus('Form fields not found.', 'error');
        return;
      }
      
      const title = titleInput.value.trim();
      const url = urlInput.value.trim();
      const description = descriptionInput ? descriptionInput.value.trim() : '';
      
      // Validate
      if (!title) {
        showStatus('Title is required.', 'error');
        return;
      }
      
      if (!url || !isValidUrl(url)) {
        showStatus('Please enter a valid http:// or https:// URL.', 'error');
        return;
      }
      
      try {
        dialogSaveBtn.disabled = true;
        dialogSaveBtn.textContent = 'Saving...';
        
        const idToken = await window.firebaseAuth.getIdToken();
        if (!idToken) {
          showStatus('Not authenticated. Please sign in again.', 'error');
          closeDialog();
          return;
        }
        
        let domain = '';
        try {
          const urlObj = new URL(url);
          domain = urlObj.host;
        } catch (e) {
          showStatus('Invalid URL format.', 'error');
          if (dialogSaveBtn) {
            dialogSaveBtn.disabled = false;
            dialogSaveBtn.textContent = 'Save';
          }
          return;
        }
        
        // Generate auto tags for updated bookmark
        let tags = [];
        if (window.CategorizationService) {
          try {
            tags = window.CategorizationService.getSuggestedTags({
              url: url,
              title: title,
              description: description || null
            });
            console.log('Generated auto tags for update:', tags);
          } catch (tagError) {
            console.warn('Tag generation failed:', tagError);
          }
        }
        
        // Update bookmark
        const bookmarkData = {
          t: title,
          u: url,
          ty: 'url',
          dom: domain,
          d: description || null,
          img: bookmark.imageUrl || null,
          ar: 0,
          fav: 0,
          tags: tags.join(','), // Convert array to comma-separated string
          rp: 0,
          tsr: 0,
        };
        
        await window.firestoreAPI.updateBookmark(bookmark.id, bookmarkData, user.uid, idToken);
        
        closeDialog();
        showStatus('Bookmark updated successfully!', 'success');
        
        // Refresh bookmarks
        if (currentView === 'bookmarks') {
          loadBookmarks(true);
        }
        
        setTimeout(() => {
          if (statusMessage) {
            statusMessage.classList.add('hidden');
          }
        }, 3000);
        
      } catch (error) {
        console.error('Update error:', error);
        showStatus('Failed to update bookmark. Please try again.', 'error');
        if (dialogSaveBtn) {
          dialogSaveBtn.disabled = false;
          dialogSaveBtn.textContent = 'Save';
        }
      }
    });
  }
}

// Delete bookmark
async function deleteBookmark(bookmark) {
  if (!confirm(`Are you sure you want to delete "${bookmark.title}"?`)) {
    return;
  }
  
  const user = await window.firebaseAuth.getCurrentUser();
  if (!user) {
    showStatus('Please sign in first.', 'error');
    return;
  }
  
  try {
    const idToken = await window.firebaseAuth.getIdToken();
    if (!idToken) {
      showStatus('Not authenticated. Please sign in again.', 'error');
      return;
    }
    
    await window.firestoreAPI.deleteBookmark(bookmark.id, user.uid, idToken);
    
    // Clear cache to ensure deleted bookmark doesn't reappear
    cachedBookmarks = null;
    cacheTimestamp = null;
    try {
      await chrome.storage.local.remove(['cachedBookmarks', 'cacheTimestamp']);
    } catch (error) {
      console.warn('Failed to clear cache:', error);
    }
    
    showStatus('Bookmark deleted successfully!', 'success');
    
    // Refresh bookmarks (force refresh to get updated data)
    if (currentView === 'bookmarks') {
      loadBookmarks(true);
    }
    
    setTimeout(() => {
      statusMessage.classList.add('hidden');
    }, 3000);
    
  } catch (error) {
    console.error('Delete error:', error);
    showStatus('Failed to delete bookmark. Please try again.', 'error');
  }
}

// Initialize on load
init();
