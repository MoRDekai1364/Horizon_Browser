// Firestore REST API client
// FIREBASE_PROJECT_ID is available from window.FIREBASE_PROJECT_ID (set by config.js or auth.js)
// Use a function to get it to avoid redeclaration errors
function getFirebaseProjectId() {
  return window.FIREBASE_PROJECT_ID || 'save-it-later-dd29e';
}

async function saveBookmark(bookmarkData, userId, idToken) {
  const bookmarkId = Date.now().toString();
  
  // First, ensure user document exists with premium status
  // This is critical for Firestore rules to work correctly
  // The Firestore rules check: userDoc.exists && (userDoc.data.isPremium == true || premiumExpirationDate > request.time)
  const userDocUrl = `https://firestore.googleapis.com/v1/projects/${getFirebaseProjectId()}/databases/(default)/documents/users/${userId}`;
  
  try {
    const userDocCheck = await fetch(userDocUrl, {
      method: 'GET',
      headers: {
        'Authorization': `Bearer ${idToken}`
      }
    });
    
    // If user document doesn't exist, Firestore rules will deny the write
    // We can't create it here - it must be created by the mobile app
    if (userDocCheck.status === 404) {
      console.error('User document does not exist in Firestore.');
      console.error('Firestore rules require the user document to exist with premium status.');
      console.error('To fix:');
      console.error('1. Open the mobile app');
      console.error('2. Sign in with the same Google account');
      console.error('3. The app will create the user document with premium status');
      throw new Error('User document not found in Firestore. Please sign in to the mobile app first to create your user document.');
    }
    
    // Verify user document has premium status
    if (userDocCheck.ok) {
      const userData = await userDocCheck.json();
      const fields = userData.fields || {};
      
      // Check all premium indicators
      const isPremiumField = fields.isPremium?.booleanValue === true;
      const expirationDate = fields.premiumExpirationDate?.timestampValue;
      const isPremiumByExpiration = expirationDate && new Date(expirationDate) > new Date();
      const subscriptionCancelled = fields.subscriptionCancelled?.booleanValue === true;
      const subscriptionExpired = fields.subscriptionExpired?.booleanValue === true;
      const subscriptionExpirationDate = fields.subscriptionExpirationDate?.timestampValue;
      const isSubscriptionActive = subscriptionExpirationDate && new Date(subscriptionExpirationDate) > new Date();
      
      const isPremium = isPremiumField || 
                       isPremiumByExpiration || 
                       (isSubscriptionActive && !subscriptionCancelled && !subscriptionExpired);
      
      console.log('Premium status check before save:', {
        isPremiumField,
        isPremiumByExpiration,
        subscriptionCancelled,
        subscriptionExpired,
        isSubscriptionActive,
        finalIsPremium: isPremium,
        allFields: Object.keys(fields),
        isPremiumRaw: fields.isPremium,
        premiumExpirationDateRaw: fields.premiumExpirationDate
      });
      
      if (!isPremium) {
        throw new Error('Premium subscription required. Your account does not have premium status.');
      }
      
      // Double-check: Firestore rules check userDoc.data.isPremium == true
      // Make sure the field is actually a boolean true, not just truthy
      const isPremiumBoolean = fields.isPremium?.booleanValue === true;
      
      // Log detailed information for debugging
      console.log('Detailed premium check:', {
        isPremiumField: fields.isPremium,
        isPremiumBooleanValue: fields.isPremium?.booleanValue,
        isPremiumBoolean: isPremiumBoolean,
        premiumExpirationDate: fields.premiumExpirationDate,
        isPremiumByExpiration: isPremiumByExpiration,
        allPremiumFields: {
          isPremium: fields.isPremium,
          premiumExpirationDate: fields.premiumExpirationDate,
          subscriptionCancelled: fields.subscriptionCancelled,
          subscriptionExpired: fields.subscriptionExpired
        }
      });
      
      if (!isPremiumBoolean && !isPremiumByExpiration) {
        console.error('ERROR: isPremium field is not boolean true. Firestore rules WILL deny the write.');
        console.error('isPremium field value:', JSON.stringify(fields.isPremium, null, 2));
        console.error('Firestore rules require: userDoc.data.isPremium == true (exact boolean true)');
        console.error('Current value does not match. Mobile app needs to update the user document.');
        throw new Error('Premium status not set correctly in Firestore. Please open the mobile app and sign in to sync your premium status.');
      }
      
      // If we have premium by expiration date, that's also valid for Firestore rules
      if (isPremiumByExpiration) {
        console.log('Premium status valid via expiration date:', expirationDate);
      } else {
        console.log('Premium status valid via isPremium boolean field');
      }
    } else {
      console.error('User document check failed:', userDocCheck.status, userDocCheck.statusText);
      throw new Error('User document not found. Please ensure you have premium subscription and try again.');
    }
  } catch (error) {
    // If it's already an error we threw, re-throw it
    if (error.message.includes('Premium') || error.message.includes('User document') || error.message.includes('not set correctly')) {
      throw error;
    }
    console.warn('User document check failed:', error);
    // Continue anyway - Firestore rules will handle the check
  }
  
  // Ensure the parent document exists
  const parentDocUrl = `https://firestore.googleapis.com/v1/projects/${getFirebaseProjectId()}/databases/(default)/documents/bookmarks/${userId}`;
  
  try {
    const parentCheck = await fetch(parentDocUrl, {
      method: 'GET',
      headers: {
        'Authorization': `Bearer ${idToken}`
      }
    });
    
    // If parent doesn't exist (404), create it
    if (parentCheck.status === 404) {
      await fetch(parentDocUrl, {
        method: 'PATCH',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${idToken}`
        },
        body: JSON.stringify({
          fields: {
            createdAt: { timestampValue: new Date().toISOString() },
            updatedAt: { timestampValue: new Date().toISOString() }
          }
        })
      });
    }
  } catch (error) {
    console.warn('Parent document check failed:', error);
  }
  
  // Final check: Re-read user document right before saving to ensure Firestore rules see the same thing
  // This helps catch any timing/caching issues
  // Also "touch" the user document to ensure Firestore rules see the latest version
  try {
    const finalUserDocCheck = await fetch(userDocUrl, {
      method: 'GET',
      headers: {
        'Authorization': `Bearer ${idToken}`
      }
    });
    
    if (finalUserDocCheck.ok) {
      const finalUserData = await finalUserDocCheck.json();
      const finalFields = finalUserData.fields || {};
      const finalIsPremium = finalFields.isPremium?.booleanValue === true;
      const finalExpirationDate = finalFields.premiumExpirationDate?.timestampValue;
      const finalIsPremiumByExpiration = finalExpirationDate && new Date(finalExpirationDate) > new Date();
      
      console.log('Final premium check right before save:', {
        isPremium: finalIsPremium,
        isPremiumByExpiration: finalIsPremiumByExpiration,
        expirationDate: finalExpirationDate,
        willPassFirestoreRules: finalIsPremium || finalIsPremiumByExpiration,
        fullUserDocument: finalUserData
      });
      
      if (!finalIsPremium && !finalIsPremiumByExpiration) {
        console.error('CRITICAL: Final check failed - Firestore rules will deny this write!');
        console.error('User document at save time:', JSON.stringify(finalUserData, null, 2));
        throw new Error('Premium status check failed right before save. Please ensure your premium status is synced from the mobile app.');
      }
      
      // "Touch" the user document to ensure Firestore rules see the latest version
      // This helps with potential caching issues in Firestore rules get()
      // CRITICAL: Must include isPremium and premiumExpirationDate to preserve them!
      // PATCH without these fields will remove them, causing Firestore rules to fail
      try {
        // Get current user data to include ALL required fields
        const currentEmail = finalFields.email?.stringValue || '';
        const currentDisplayName = finalFields.displayName?.stringValue || '';
        const currentIsPremium = finalFields.isPremium?.booleanValue === true; // Explicitly check for true
        const currentPremiumExpirationDate = finalFields.premiumExpirationDate?.timestampValue || null;
        
        // Check if document was recently updated (within last 5 seconds)
        // If isPremium is true and document is fresh, we might not need to touch
        const lastUpdated = finalUserData.updateTime;
        const lastUpdatedTime = lastUpdated ? new Date(lastUpdated) : null;
        const secondsSinceUpdate = lastUpdatedTime ? (new Date() - lastUpdatedTime) / 1000 : Infinity;
        const isDocumentFresh = secondsSinceUpdate < 5;
        
        console.log('Touch operation - preserving premium status:', {
          currentIsPremium,
          currentPremiumExpirationDate,
          isPremiumFieldExists: !!finalFields.isPremium,
          isPremiumRaw: finalFields.isPremium,
          lastUpdated: lastUpdated,
          secondsSinceUpdate: secondsSinceUpdate,
          isDocumentFresh: isDocumentFresh,
          shouldSkipTouch: currentIsPremium && isDocumentFresh
        });
        
        // If isPremium is true and document was recently updated, we can skip the touch
        // This avoids unnecessary operations and potential race conditions
        // Firestore rules get() has eventual consistency, so we need to wait a bit
        if (currentIsPremium && isDocumentFresh) {
          console.log('⏭️ Skipping touch - isPremium is true and document is fresh (updated', secondsSinceUpdate.toFixed(1), 'seconds ago)');
          console.log('⏳ Waiting for Firestore rules cache to refresh (eventual consistency)...');
          // Wait longer to ensure Firestore rules get() sees the latest version
          // Firestore rules get() can have up to a few seconds of eventual consistency
          await new Promise(resolve => setTimeout(resolve, 2000)); // 2 seconds for rules cache
          console.log('✅ Waited for Firestore rules cache refresh');
        } else if (currentIsPremium && !isDocumentFresh) {
          // isPremium is true but document is stale - we should still skip touch
          // but wait longer to ensure rules see the latest version
          console.log('⏭️ Skipping touch - isPremium is true but document is stale (updated', secondsSinceUpdate.toFixed(1), 'seconds ago)');
          console.log('⏳ Waiting for Firestore rules cache to refresh...');
          await new Promise(resolve => setTimeout(resolve, 2000));
          console.log('✅ Waited for Firestore rules cache refresh');
        } else {
        
        // Build fields object with ALL critical fields
        const touchFields = {
          email: currentEmail ? { stringValue: currentEmail } : { nullValue: null },
          displayName: currentDisplayName ? { stringValue: currentDisplayName } : { nullValue: null },
          lastActiveAt: { timestampValue: new Date().toISOString() },
          lastUpdated: { timestampValue: new Date().toISOString() }
        };
        
        // CRITICAL: Always include isPremium if it exists in the document
        // If isPremium is true, we MUST preserve it, otherwise Firestore rules will fail
        if (finalFields.isPremium !== undefined) {
          touchFields.isPremium = { booleanValue: currentIsPremium };
          console.log('✅ Including isPremium in touch:', currentIsPremium);
        } else {
          console.warn('⚠️ isPremium field does not exist in document - cannot preserve it');
        }
        
        // Include premiumExpirationDate if it exists
        if (currentPremiumExpirationDate) {
          touchFields.premiumExpirationDate = { timestampValue: currentPremiumExpirationDate };
          console.log('✅ Including premiumExpirationDate in touch');
        }
        
        const touchResponse = await fetch(userDocUrl, {
          method: 'PATCH',
          headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${idToken}`
          },
          body: JSON.stringify({
            fields: touchFields
          })
        });
        
        if (touchResponse.ok) {
          console.log('✅ Touched user document to refresh Firestore rules cache');
          console.log('⏳ Waiting for Firestore rules cache to refresh (eventual consistency)...');
          // Longer delay to ensure the update fully propagates to Firestore rules
          // Firestore rules get() can have up to a few seconds of eventual consistency
          await new Promise(resolve => setTimeout(resolve, 2500)); // 2.5 seconds for rules cache
          console.log('✅ Waited for Firestore rules cache refresh');
          
          // Verify the touch worked by reading the document again
          const verifyTouch = await fetch(userDocUrl, {
            method: 'GET',
            headers: {
              'Authorization': `Bearer ${idToken}`
            }
          });
          
          if (verifyTouch.ok) {
            const verifyData = await verifyTouch.json();
            const verifiedIsPremium = verifyData.fields?.isPremium?.booleanValue === true;
            const verifiedExpirationDate = verifyData.fields?.premiumExpirationDate?.timestampValue;
            
            console.log('✅ Verified user document touch:', {
              lastUpdated: verifyData.fields?.lastUpdated?.timestampValue,
              isPremium: verifiedIsPremium,
              premiumExpirationDate: verifiedExpirationDate ? new Date(verifiedExpirationDate).toISOString() : null,
              premiumStatusPreserved: verifiedIsPremium || (verifiedExpirationDate && new Date(verifiedExpirationDate) > new Date())
            });
            
            // CRITICAL CHECK: If isPremium was true but is now false, something went wrong!
            if (currentIsPremium && !verifiedIsPremium) {
              console.error('❌ CRITICAL ERROR: isPremium was TRUE but after touch it is FALSE!');
              console.error('This means the touch operation removed the premium status!');
              console.error('Original isPremium:', currentIsPremium);
              console.error('Verified isPremium:', verifiedIsPremium);
              throw new Error('Premium status was lost during touch operation. Please try again.');
            }
          }
        } else {
          const touchErrorText = await touchResponse.text();
          console.warn('⚠️ Failed to touch user document:', touchResponse.status, touchErrorText);
          // If touch fails, still try to save - the document should be valid
        }
        } // End of else block (touch operation)
      } catch (touchError) {
        // If it's our critical error about premium status being lost, re-throw it
        if (touchError.message && touchError.message.includes('Premium status was lost')) {
          throw touchError;
        }
        console.warn('⚠️ Error touching user document (non-critical):', touchError);
        // Continue anyway - the document should still be valid
      }
    }
  } catch (finalCheckError) {
    if (finalCheckError.message.includes('Premium')) {
      throw finalCheckError;
    }
    console.warn('Final user document check failed, proceeding anyway:', finalCheckError);
  }
  
  // CRITICAL: Do multiple "warm-up" reads of the user document right before saving
  // Firestore rules get() has eventual consistency - can take 5-10 seconds to see updates
  // Multiple reads help "warm up" the cache that Firestore rules uses
  try {
    console.log('🔥 Starting aggressive warm-up of Firestore rules cache...');
    
    // Do 3 warm-up reads with delays between them
    // This helps ensure the cache that Firestore rules uses is updated
    for (let i = 1; i <= 3; i++) {
      console.log(`🔥 Warm-up read ${i}/3...`);
      const warmupRead = await fetch(userDocUrl, {
        method: 'GET',
        headers: {
          'Authorization': `Bearer ${idToken}`
        }
      });
      
      if (warmupRead.ok) {
        const warmupData = await warmupRead.json();
        const warmupIsPremium = warmupData.fields?.isPremium?.booleanValue === true;
        const warmupExpiration = warmupData.fields?.premiumExpirationDate?.timestampValue;
        const warmupIsPremiumByExpiration = warmupExpiration && new Date(warmupExpiration) > new Date();
        
        console.log(`🔥 Warm-up read ${i}/3 result:`, {
          isPremium: warmupIsPremium,
          isPremiumByExpiration: warmupIsPremiumByExpiration,
          willPassRules: warmupIsPremium || warmupIsPremiumByExpiration,
          updateTime: warmupData.updateTime
        });
        
        if (!warmupIsPremium && !warmupIsPremiumByExpiration) {
          console.error(`❌ Warm-up read ${i}/3 shows user is NOT premium - rules will deny!`);
          throw new Error('Premium status check failed during warm-up read. Please ensure your premium status is synced from the mobile app.');
        }
        
        // Wait between reads to allow cache to propagate
        if (i < 3) {
          console.log(`⏳ Waiting 2 seconds before next warm-up read...`);
          await new Promise(resolve => setTimeout(resolve, 2000));
        }
      } else {
        console.warn(`⚠️ Warm-up read ${i}/3 failed:`, warmupRead.status);
      }
    }
    
    // Final wait after all warm-up reads to ensure rules cache is fully ready
    console.log('⏳ Final wait after warm-up reads for Firestore rules cache (5 seconds)...');
    console.log('⚠️ Firestore rules get() can have up to 5-10 seconds of eventual consistency');
    await new Promise(resolve => setTimeout(resolve, 5000)); // 5 seconds final wait
    console.log('✅ Ready to save - Firestore rules cache should be fully warmed up');
  } catch (warmupError) {
    if (warmupError.message && warmupError.message.includes('Premium')) {
      throw warmupError;
    }
    console.warn('⚠️ Warm-up reads failed (non-critical):', warmupError);
  }

  // Now create the subcollection document
  // Use POST to the collection path (without document ID) and specify documentId in the request
  // Path format: projects/{project}/databases/(default)/documents/{collection}/{doc}/{subcollection}
  const collectionUrl = `https://firestore.googleapis.com/v1/projects/${getFirebaseProjectId()}/databases/(default)/documents/bookmarks/${userId}/items`;

  // Convert to Firestore document format
  const firestoreDoc = {
    fields: {
      id: { nullValue: null },
      t: { stringValue: bookmarkData.t || 'Untitled' },
      u: { stringValue: bookmarkData.u || '' },
      ty: { stringValue: bookmarkData.ty || 'url' },
      d: bookmarkData.d ? { stringValue: bookmarkData.d } : { nullValue: null },
      img: bookmarkData.img ? { stringValue: bookmarkData.img } : { nullValue: null },
      c: bookmarkData.c ? { stringValue: bookmarkData.c } : { nullValue: null },
      // Save 'ca' as integer (milliseconds) to match mobile app format and preserve original dates
      // This ensures dates are preserved when syncing between mobile and Chrome extension
      ca: { integerValue: Date.now().toString() },
      ua: { integerValue: Date.now().toString() },
      ar: { integerValue: bookmarkData.ar || '0' },
      fav: { integerValue: bookmarkData.fav || '0' },
      fid: bookmarkData.fid ? { integerValue: bookmarkData.fid.toString() } : { nullValue: null },
      tags: { stringValue: bookmarkData.tags || '' },
      dom: bookmarkData.dom ? { stringValue: bookmarkData.dom } : { nullValue: null },
      pn: bookmarkData.pn ? { stringValue: bookmarkData.pn } : { nullValue: null },
      pi: bookmarkData.pi ? { stringValue: bookmarkData.pi } : { nullValue: null },
      a: bookmarkData.a ? { stringValue: bookmarkData.a } : { nullValue: null },
      rt: bookmarkData.rt ? { stringValue: bookmarkData.rt } : { nullValue: null },
      lang: bookmarkData.lang ? { stringValue: bookmarkData.lang } : { nullValue: null },
      rp: { doubleValue: bookmarkData.rp || 0 },
      tsr: { integerValue: bookmarkData.tsr || '0' },
      lra: bookmarkData.lra ? { timestampValue: new Date(bookmarkData.lra).toISOString() } : { nullValue: null },
      lpu: bookmarkData.lpu ? { stringValue: bookmarkData.lpu } : { nullValue: null },
      lpt: bookmarkData.lpt ? { stringValue: bookmarkData.lpt } : { nullValue: null },
      lpd: bookmarkData.lpd ? { stringValue: bookmarkData.lpd } : { nullValue: null },
      cid: bookmarkData.cid ? { stringValue: bookmarkData.cid } : { nullValue: null },
      cc: bookmarkData.cc ? { doubleValue: bookmarkData.cc } : { nullValue: null },
      createdAt: { timestampValue: new Date().toISOString() },
      updatedAt: { timestampValue: new Date().toISOString() }
    }
  };

  // Use POST to create a document with a specific ID
  // Include documentId in the query parameter
  // Add retry logic in case of permission denied (might be caching issue)
  let response;
  let retryCount = 0;
  const maxRetries = 2;
  
  while (retryCount <= maxRetries) {
    response = await fetch(`${collectionUrl}?documentId=${bookmarkId}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${idToken}`
      },
      body: JSON.stringify(firestoreDoc)
    });
    
    // If successful, break out of retry loop
    if (response.ok) {
      break;
    }
    
    // If permission denied and we haven't retried yet, wait and retry
    if (response.status === 403 && retryCount < maxRetries) {
      console.warn(`⚠️ Permission denied (attempt ${retryCount + 1}/${maxRetries + 1}). Retrying after delay...`);
      console.warn('⏳ Waiting for Firestore rules cache to refresh (eventual consistency issue)...');
      console.warn('⚠️ Firestore rules get() can have up to 5-10 seconds of eventual consistency');
      // Wait longer between retries to allow Firestore rules cache to refresh
      // Firestore rules get() can have up to 5-10 seconds of eventual consistency
      const retryDelay = 5000 + (retryCount * 2000); // 5s, 7s, 9s - increasing delays
      console.warn(`⏳ Waiting ${retryDelay / 1000} seconds before retry...`);
      await new Promise(resolve => setTimeout(resolve, retryDelay));
      console.warn('✅ Waited for Firestore rules cache refresh, retrying...');
      retryCount++;
      continue;
    }
    
    // If not permission denied, or we've exhausted retries, break
    break;
  }

  if (!response.ok) {
    const errorText = await response.text();
    let errorData;
    try {
      errorData = JSON.parse(errorText);
    } catch (e) {
      errorData = { error: { message: errorText } };
    }
    
    // Check for permission denied errors
    if (response.status === 403 || errorData.error?.code === 403 || 
        errorData.error?.message?.includes('PERMISSION_DENIED') ||
        errorData.error?.message?.includes('permission')) {
      console.error('Permission denied when saving bookmark:', JSON.stringify(errorData, null, 2));
      console.error('Full error object:', errorData);
      console.error('This usually means:');
      console.error('1. User document does not exist in Firestore');
      console.error('2. User document exists but premium status is not set correctly');
      console.error('3. Premium status check in Firestore rules failed');
      console.error('4. Firestore rules might be checking the document differently');
      
      // Try to get the user document again to see what Firestore rules see
      try {
        const userDocRecheck = await fetch(userDocUrl, {
          method: 'GET',
          headers: {
            'Authorization': `Bearer ${idToken}`
          }
        });
        
        if (userDocRecheck.ok) {
          const userDataRecheck = await userDocRecheck.json();
          console.error('User document at time of error:', JSON.stringify(userDataRecheck, null, 2));
          console.error('isPremium field:', userDataRecheck.fields?.isPremium);
          console.error('premiumExpirationDate field:', userDataRecheck.fields?.premiumExpirationDate);
        } else {
          console.error('Could not re-check user document:', userDocRecheck.status);
        }
      } catch (recheckError) {
        console.error('Error re-checking user document:', recheckError);
      }
      
      throw new Error('Premium subscription required. Please ensure your premium status is synced from the mobile app. If you have premium, try signing out and signing in again.');
    }
    
    throw new Error(`Firestore error: ${errorText}`);
  }

  return bookmarkId;
}

async function checkPremiumStatus(userId, idToken) {
  const url = `https://firestore.googleapis.com/v1/projects/${getFirebaseProjectId()}/databases/(default)/documents/users/${userId}`;

  try {
    const response = await fetch(url, {
      method: 'GET',
      headers: {
        'Authorization': `Bearer ${idToken}`
      }
    });

    if (!response.ok) {
      const errorText = await response.text();
      console.error('Premium status check failed:', {
        status: response.status,
        statusText: response.statusText,
        error: errorText,
        userId
      });

      if (response.status === 404) {
        console.warn('User document does not exist in Firestore. Premium status cannot be verified.');
        console.warn('To fix: Sign in to the mobile app with the same account to create the user document.');
        throw new Error('User document not found. Please sign in to the mobile app to sync your premium status.');
      } else if (response.status === 403) {
        console.warn('Permission denied when checking premium status.');
        throw new Error('Permission denied. Please ensure you are signed in with the correct account.');
      } else {
        throw new Error(`Failed to check premium status: ${response.status} ${response.statusText}`);
      }
    }

    const data = await response.json();
    const fields = data.fields || {};

    // Check isPremium field (booleanValue) - highest priority
    const isPremiumField = fields.isPremium?.booleanValue === true;
    
    // Check premiumExpirationDate (timestampValue) - second priority
    const expirationDate = fields.premiumExpirationDate?.timestampValue;
    const isPremiumByExpiration = expirationDate && new Date(expirationDate) > new Date();
    
    // Check subscription status as fallback
    const subscriptionCancelled = fields.subscriptionCancelled?.booleanValue === true;
    const subscriptionExpired = fields.subscriptionExpired?.booleanValue === true;
    const willRenew = fields.willRenew?.booleanValue === true;
    const subscriptionExpirationDate = fields.subscriptionExpirationDate?.timestampValue;
    const isSubscriptionActive = subscriptionExpirationDate && new Date(subscriptionExpirationDate) > new Date();
    
    // User is premium if:
    // 1. isPremium field is explicitly true (highest priority - overrides everything), OR
    // 2. premiumExpirationDate is in the future (lifetime or active subscription), OR
    // 3. subscription is active (not cancelled, not expired, and expiration date is in future)
    // Note: isPremium: true takes precedence over subscription status
    const isPremium = isPremiumField || 
                     isPremiumByExpiration || 
                     (isSubscriptionActive && !subscriptionCancelled && !subscriptionExpired);

    // Debug logging
    console.log('Premium status check:', {
      userId,
      userDocumentExists: true,
      isPremiumField,
      expirationDate: expirationDate ? new Date(expirationDate).toISOString() : null,
      isPremiumByExpiration,
      subscriptionCancelled,
      subscriptionExpired,
      willRenew,
      subscriptionExpirationDate: subscriptionExpirationDate ? new Date(subscriptionExpirationDate).toISOString() : null,
      isSubscriptionActive,
      finalIsPremium: isPremium,
      allFields: Object.keys(fields)
    });

    if (!isPremium) {
      // Log detailed information about why user is not premium
      // Log each field separately for better console readability
      const isPremiumValue = fields.isPremium?.booleanValue;
      const premiumExpirationDateValue = fields.premiumExpirationDate?.timestampValue;
      const subscriptionCancelledValue = fields.subscriptionCancelled?.booleanValue;
      const subscriptionExpiredValue = fields.subscriptionExpired?.booleanValue;
      const subscriptionExpirationDateValue = fields.subscriptionExpirationDate?.timestampValue;
      
      console.warn('═══════════════════════════════════════════════════════════');
      console.warn('❌ USER IS NOT PREMIUM - Detailed Field Analysis');
      console.warn('═══════════════════════════════════════════════════════════');
      console.warn('isPremium (booleanValue):', isPremiumValue);
      console.warn('isPremium (raw Firestore field):', JSON.stringify(fields.isPremium, null, 2));
      console.warn('premiumExpirationDate (timestampValue):', premiumExpirationDateValue ? new Date(premiumExpirationDateValue).toISOString() : 'null/undefined');
      console.warn('premiumExpirationDate (raw Firestore field):', JSON.stringify(fields.premiumExpirationDate, null, 2));
      console.warn('subscriptionCancelled:', subscriptionCancelledValue);
      console.warn('subscriptionExpired:', subscriptionExpiredValue);
      console.warn('subscriptionExpirationDate:', subscriptionExpirationDateValue ? new Date(subscriptionExpirationDateValue).toISOString() : 'null/undefined');
      console.warn('All available fields in user document:', Object.keys(fields).join(', '));
      console.warn('Full user document JSON:', JSON.stringify(data, null, 2));
      console.warn('═══════════════════════════════════════════════════════════');
      
      // Provide specific guidance based on what's missing
      if (!fields.isPremium && !fields.premiumExpirationDate) {
        console.error('❌ CRITICAL: User document is missing BOTH isPremium and premiumExpirationDate fields.');
        console.error('This means the mobile app has not synced premium status to Firestore yet.');
        console.error('SOLUTION: Open the mobile app, sign in, and ensure premium subscription is active.');
        console.error('The mobile app will automatically sync premium status to Firestore when you sign in.');
      } else if (!fields.isPremium && fields.premiumExpirationDate) {
        console.warn('⚠️ User document has premiumExpirationDate but not isPremium field.');
        console.warn('The expiration date is:', premiumExpirationDateValue ? new Date(premiumExpirationDateValue).toISOString() : 'missing');
        console.warn('Is expiration in future?', isPremiumByExpiration);
        if (!isPremiumByExpiration) {
          console.error('⚠️ Premium expiration date is in the past - subscription has expired.');
        }
      }
    }

    return isPremium;
  } catch (error) {
    console.error('Error checking premium status:', error);
    // Re-throw the error so popup.js can handle it
    throw error;
  }
}

async function updateBookmark(bookmarkId, bookmarkData, userId, idToken) {
  const docUrl = `https://firestore.googleapis.com/v1/projects/${getFirebaseProjectId()}/databases/(default)/documents/bookmarks/${userId}/items/${bookmarkId}`;
  
  // Convert to Firestore document format
  const fields = {
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
    // When updating, only update 'ua' (updatedAt), NOT 'ca' (createdAt) to preserve original date
    ua: { integerValue: Date.now().toString() },
    updatedAt: { timestampValue: new Date().toISOString() }
  };
  
  // Build updateMask query parameter (Firestore REST API format)
  const fieldPaths = Object.keys(fields);
  const updateMask = fieldPaths.map(path => `updateMask.fieldPaths=${encodeURIComponent(path)}`).join('&');
  
  const response = await fetch(`${docUrl}?${updateMask}`, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${idToken}`
    },
    body: JSON.stringify({
      fields: fields
    })
  });
  
  if (!response.ok) {
    const errorText = await response.text();
    throw new Error(`Failed to update bookmark: ${errorText}`);
  }
  
  return true;
}

async function deleteBookmark(bookmarkId, userId, idToken) {
  const docUrl = `https://firestore.googleapis.com/v1/projects/${getFirebaseProjectId()}/databases/(default)/documents/bookmarks/${userId}/items/${bookmarkId}`;
  
  const response = await fetch(docUrl, {
    method: 'DELETE',
    headers: {
      'Authorization': `Bearer ${idToken}`
    }
  });
  
  if (!response.ok) {
    const errorText = await response.text();
    throw new Error(`Failed to delete bookmark: ${errorText}`);
  }
  
  return true;
}

window.firestoreAPI = {
  saveBookmark,
  updateBookmark,
  deleteBookmark,
  checkPremiumStatus
};

