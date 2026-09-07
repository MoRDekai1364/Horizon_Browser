// Configuration file for Chrome Extension
// NOTE: These values are PUBLIC and will be visible in the extension bundle.
// Security is enforced through:
// - Firebase API key restrictions (domain/package restrictions)
// - OAuth Client ID redirect URI restrictions
// - Firestore security rules

// Firebase Configuration
// Store values in an IIFE to avoid global scope pollution
// Only export to window.FIREBASE_CONFIG for other scripts to use
(function() {
  'use strict';
  
  const FIREBASE_API_KEY = 'AIzaSyAj14oFzhIibZeqhut1d0pLXitkHOImSOU';
  const FIREBASE_PROJECT_ID = 'save-it-later-dd29e';
  // Old Client ID (for reference/local testing): '220169508577-lhjjp7i6od0ru8lc57tkp2ajkdaip8v3.apps.googleusercontent.com'
  // const CHROME_EXTENSION_CLIENT_ID = '220169508577-lhjjp7i6od0ru8lc57tkp2ajkdaip8v3.apps.googleusercontent.com'; // Old Client ID
  const CHROME_EXTENSION_CLIENT_ID = '220169508577-h1puq2rom0f5v0b8e4qan2ch2cm16dti.apps.googleusercontent.com';
  const WEB_CLIENT_ID = '220169508577-tq5o72b8g4d2lp7qsst76nfco3smdnvk.apps.googleusercontent.com';
  const METADATA_ENDPOINTS = {
    vercel: 'https://save-it-fetching.vercel.app/api/fetch-content',
    cloudFunction: 'https://fetchpreview-ztdink6mca-uc.a.run.app/fetchPreview'
  };

  // Export for use in other scripts via window object
  if (typeof window !== 'undefined') {
    window.FIREBASE_CONFIG = {
      apiKey: FIREBASE_API_KEY,
      projectId: FIREBASE_PROJECT_ID,
      chromeExtensionClientId: CHROME_EXTENSION_CLIENT_ID,
      webClientId: WEB_CLIENT_ID
    };
    
    window.FIREBASE_PROJECT_ID = FIREBASE_PROJECT_ID;
    window.METADATA_ENDPOINTS = METADATA_ENDPOINTS;
  }
})();

