// Firebase Authentication using REST API
// Configuration is loaded from config.js
// These values are public but secured through Firebase domain restrictions and OAuth redirect URI restrictions
// Get values from window.FIREBASE_CONFIG (set by config.js) or use fallback values
// Note: config.js declares these as var, so we can access them directly, but we use window.FIREBASE_CONFIG to be safe
const FIREBASE_API_KEY = window.FIREBASE_CONFIG?.apiKey || 'AIzaSyAj14oFzhIibZeqhut1d0pLXitkHOImSOU';
const FIREBASE_PROJECT_ID = window.FIREBASE_CONFIG?.projectId || window.FIREBASE_PROJECT_ID || 'save-it-later-dd29e';
// const CHROME_EXTENSION_CLIENT_ID = window.FIREBASE_CONFIG?.chromeExtensionClientId || '220169508577-lhjjp7i6od0ru8lc57tkp2ajkdaip8v3.apps.googleusercontent.com'; // Old Client ID
const CHROME_EXTENSION_CLIENT_ID = window.FIREBASE_CONFIG?.chromeExtensionClientId || '220169508577-h1puq2rom0f5v0b8e4qan2ch2cm16dti.apps.googleusercontent.com';
const WEB_CLIENT_ID = window.FIREBASE_CONFIG?.webClientId || '220169508577-tq5o72b8g4d2lp7qsst76nfco3smdnvk.apps.googleusercontent.com';

// Make FIREBASE_PROJECT_ID available globally for other scripts (if not already set)
if (!window.FIREBASE_PROJECT_ID) {
  window.FIREBASE_PROJECT_ID = FIREBASE_PROJECT_ID;
}

// Store auth state
let currentUser = null;
let idToken = null;
let refreshToken = null;
let tokenExpiryTime = null;

// Listen for auth state changes
const authStateListeners = [];

function onAuthStateChanged(callback) {
  authStateListeners.push(callback);
  if (currentUser) {
    callback(currentUser);
  }
  return () => {
    const index = authStateListeners.indexOf(callback);
    if (index > -1) authStateListeners.splice(index, 1);
  };
}

function notifyAuthStateListeners(user) {
  authStateListeners.forEach(callback => callback(user));
}

// Sign in with Google using OAuth popup
async function signInWithGoogle() {
  try {
    console.log('[AUTH] Starting sign in process...');
    
    // Check if chrome.identity is available
    if (!chrome.identity || !chrome.identity.getAuthToken) {
      console.error('[AUTH] chrome.identity API not available');
      throw new Error('Chrome identity API not available. Please check extension permissions.');
    }
    
    // Clear cached tokens to force account picker
    // This ensures the account selection window always appears
    console.log('[AUTH] Clearing cached tokens...');
    await new Promise((resolve) => {
      chrome.identity.getAuthToken({ interactive: false }, (cachedToken) => {
        if (chrome.runtime.lastError) {
          console.log('[AUTH] No cached token or error:', chrome.runtime.lastError.message);
          resolve();
          return;
        }
        
        if (cachedToken) {
          // Remove the cached token to force account selection
          chrome.identity.removeCachedAuthToken({ token: cachedToken }, () => {
            console.log('[AUTH] Cleared cached auth token to show account picker');
            resolve();
          });
        } else {
          resolve();
        }
      });
    });
    
    // Small delay to ensure cache is cleared
    await new Promise(resolve => setTimeout(resolve, 200));
    
    // Use chrome.identity.getAuthToken with interactive: true
    // After clearing cache, this will show account picker
    console.log('[AUTH] Requesting Google auth token (interactive)...');
    const googleAccessToken = await new Promise((resolve, reject) => {
      chrome.identity.getAuthToken(
        {
          interactive: true, // This shows account picker (after cache is cleared)
          scopes: [
            'openid',
            'https://www.googleapis.com/auth/userinfo.email',
            'https://www.googleapis.com/auth/userinfo.profile'
          ]
        },
        (token) => {
          if (chrome.runtime.lastError) {
            const error = chrome.runtime.lastError.message;
            console.error('[AUTH] getAuthToken error:', error);
            if (error.includes('canceled') || error.includes('OAuth2') || error.includes('OAuth')) {
              reject(new Error('Sign in cancelled. Please try again and select your Google account.'));
            } else if (error.includes('redirect_uri_mismatch')) {
              reject(new Error('OAuth configuration error. Please contact support.'));
            } else {
              reject(new Error(`Authentication failed: ${error}`));
            }
          } else if (!token) {
            reject(new Error('No token received from Google. Please try again.'));
          } else {
            console.log('[AUTH] Google access token received');
            resolve(token);
          }
        }
      );
    });
    
    // Get user info from Google
    const userInfoResponse = await fetch(
      `https://www.googleapis.com/oauth2/v2/userinfo?access_token=${googleAccessToken}`
    );
    
    if (!userInfoResponse.ok) {
      throw new Error('Failed to get user info from Google');
    }
    
    const userInfo = await userInfoResponse.json();
    
    // Exchange access token for Firebase ID token
    // Get redirect URL for Firebase token exchange
    let redirectUrl = chrome.identity.getRedirectURL();
    redirectUrl = redirectUrl.replace(/\/$/, ''); // Remove trailing slash
    
    // Use Firebase's auth handler to exchange the token
    const firebaseAuthUrl = `https://${FIREBASE_PROJECT_ID}.firebaseapp.com/__/auth/handler?` +
      `apiKey=${FIREBASE_API_KEY}&` +
      `appName=default&` +
      `authType=signInViaPopup&` +
      `providerId=google.com&` +
      `redirectUrl=${encodeURIComponent(redirectUrl)}&` +
      `v=10.7.1`;
    
    console.log('Exchanging Google token with Firebase...');
    
    // Try to use the access token directly with Firebase
    // First, try to exchange access token for ID token
    try {
      const firebaseResponse = await fetch(
        `https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key=${FIREBASE_API_KEY}`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            requestUri: redirectUrl,
            postBody: `access_token=${googleAccessToken}&providerId=google.com`,
            returnSecureToken: true,
            returnIdpCredential: true
          })
        }
      );
      
      if (firebaseResponse.ok) {
        const firebaseData = await firebaseResponse.json();
        
        currentUser = {
          uid: firebaseData.localId,
          email: firebaseData.email || userInfo.email,
          displayName: firebaseData.displayName || userInfo.name,
          photoURL: firebaseData.photoUrl || userInfo.picture
        };
        
        idToken = firebaseData.idToken;
        refreshToken = firebaseData.refreshToken || firebaseData.refresh_token;
        tokenExpiryTime = Date.now() + (firebaseData.expiresIn ? firebaseData.expiresIn * 1000 : 3600 * 1000);
        
        console.log('Sign in successful - refresh token stored:', !!refreshToken);
        
        await chrome.storage.local.set({ 
          currentUser, 
          idToken, 
          refreshToken, 
          tokenExpiryTime 
        });
        notifyAuthStateListeners(currentUser);
        return { success: true, user: currentUser };
      } else {
        const errorText = await firebaseResponse.text();
        console.warn('Firebase token exchange failed:', errorText);
        throw new Error('Failed to exchange token with Firebase');
      }
    } catch (exchangeError) {
      console.error('Token exchange error:', exchangeError);
      throw new Error('Failed to authenticate with Firebase. Please try again.');
    }
  } catch (error) {
    console.error('Sign in error:', error);
    throw error;
  }
}

// Sign out
async function signOut() {
  // Revoke all tokens
  chrome.identity.getAuthToken({ interactive: false }, (token) => {
    if (token && !chrome.runtime.lastError) {
      chrome.identity.removeCachedAuthToken({ token }, () => {});
    }
  });

  currentUser = null;
  idToken = null;
  refreshToken = null;
  tokenExpiryTime = null;
  await chrome.storage.local.remove(['currentUser', 'idToken', 'refreshToken', 'tokenExpiryTime']);
  notifyAuthStateListeners(null);
}

// Get current user
async function getCurrentUser() {
  if (currentUser) return currentUser;

  const stored = await chrome.storage.local.get(['currentUser', 'idToken', 'refreshToken', 'tokenExpiryTime']);
  if (stored.currentUser && stored.idToken) {
    currentUser = stored.currentUser;
    idToken = stored.idToken;
    refreshToken = stored.refreshToken;
    tokenExpiryTime = stored.tokenExpiryTime;
    return currentUser;
  }

  return null;
}

// Get ID token (with automatic refresh if expired)
async function getIdToken(forceRefresh = false) {
  // Check if token is expired or about to expire (within 5 minutes)
  const now = Date.now();
  const isExpired = !tokenExpiryTime || now >= (tokenExpiryTime - 5 * 60 * 1000);
  
  if (idToken && !isExpired && !forceRefresh) {
    return idToken;
  }

  // Try to get stored token first
  const stored = await chrome.storage.local.get(['idToken', 'refreshToken', 'tokenExpiryTime']);
  
  if (stored.idToken && stored.tokenExpiryTime) {
    idToken = stored.idToken;
    refreshToken = stored.refreshToken;
    tokenExpiryTime = stored.tokenExpiryTime;
    
    // Check if stored token is still valid
    if (now < (tokenExpiryTime - 5 * 60 * 1000) && !forceRefresh) {
      return idToken;
    }
  }

  // Token is expired or doesn't exist - need to refresh or re-authenticate
  if (refreshToken && !forceRefresh) {
    try {
      console.log('Attempting to refresh token...');
      // Try to refresh the token using Firebase's token refresh endpoint
      // Firebase expects form-encoded data, not JSON
      const refreshParams = new URLSearchParams({
        grant_type: 'refresh_token',
        refresh_token: refreshToken
      });
      
      const refreshResponse = await fetch(
        `https://securetoken.googleapis.com/v1/token?key=${FIREBASE_API_KEY}`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
          body: refreshParams.toString()
        }
      );

      if (refreshResponse.ok) {
        const refreshData = await refreshResponse.json();
        idToken = refreshData.id_token;
        refreshToken = refreshData.refresh_token || refreshToken; // Keep old refresh token if new one not provided
        // Firebase tokens expire in 1 hour (3600 seconds)
        tokenExpiryTime = now + (refreshData.expires_in ? refreshData.expires_in * 1000 : 3600 * 1000);
        
        await chrome.storage.local.set({ 
          idToken, 
          refreshToken, 
          tokenExpiryTime 
        });
        
        console.log('Token refreshed successfully');
        return idToken;
      } else {
        const errorText = await refreshResponse.text();
        console.error('Token refresh failed:', refreshResponse.status, errorText);
        // Clear invalid refresh token
        refreshToken = null;
        tokenExpiryTime = null;
        await chrome.storage.local.remove(['refreshToken', 'tokenExpiryTime']);
      }
    } catch (refreshError) {
      console.error('Token refresh error:', refreshError);
      // Clear invalid refresh token
      refreshToken = null;
      tokenExpiryTime = null;
      await chrome.storage.local.remove(['refreshToken', 'tokenExpiryTime']);
    }
  } else if (!refreshToken) {
    console.warn('No refresh token available. User needs to sign in again.');
  }

  // If refresh failed or no refresh token, return null
  // The caller should prompt user to sign in again
  console.warn('Cannot get valid token. User needs to sign in again.');
  return null;
}

// Initialize - restore auth state
async function init() {
  await getCurrentUser();
  if (currentUser) {
    notifyAuthStateListeners(currentUser);
  }
}

// Export
window.firebaseAuth = {
  signInWithGoogle,
  signOut,
  getCurrentUser,
  getIdToken,
  onAuthStateChanged,
  init
};

// Auto-initialize
init();
