// Metadata fetching utility
// Fetches title, description, and image from URL
// Uses Vercel endpoint as primary, Cloud Function as fallback

/**
 * Fetch metadata (title, description, image) from a URL
 * @param {string} url - The URL to fetch metadata for
 * @returns {Promise<{title: string, description: string, image: string}>}
 */
async function fetchUrlMetadata(url) {
  const endpoints = window.METADATA_ENDPOINTS || {
    vercel: 'https://save-it-fetching.vercel.app/api/fetch-content',
    cloudFunction: 'https://fetchpreview-ztdink6mca-uc.a.run.app/fetchPreview'
  };
  
  let metadata = {
    title: null,
    description: null,
    image: null
  };
  
  // Try Vercel endpoint first (better CORS support)
  try {
    const vercelResponse = await fetch(endpoints.vercel, {
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
      // Vercel returns data directly (no 'success' wrapper)
      if (vercelData.title && vercelData.title.trim()) {
        metadata.title = vercelData.title.trim();
      }
      if (vercelData.description && vercelData.description.trim()) {
        metadata.description = vercelData.description.trim();
      }
      if (vercelData.image && vercelData.image.trim()) {
        metadata.image = vercelData.image.trim();
      }
      console.log('✅ Fetched metadata from Vercel:', metadata);
      return metadata;
    }
  } catch (vercelError) {
    console.warn('Vercel endpoint failed:', vercelError);
  }
  
  // Fallback to Cloud Function if Vercel didn't work
  try {
    const cloudEndpoint = `${endpoints.cloudFunction}?url=${encodeURIComponent(url)}`;
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
          metadata.title = cloudData.title.trim();
        }
        if (cloudData.description && cloudData.description.trim()) {
          metadata.description = cloudData.description.trim();
        }
        if (cloudData.image && cloudData.image.trim()) {
          metadata.image = cloudData.image.trim();
        }
        console.log('✅ Fetched metadata from Cloud Function:', metadata);
        return metadata;
      }
    }
  } catch (cloudError) {
    console.warn('Cloud Function endpoint also failed:', cloudError);
  }
  
  // Return empty metadata if both endpoints failed
  console.warn('All metadata endpoints failed, returning empty metadata');
  return metadata;
}

// Export for use in other scripts
if (typeof window !== 'undefined') {
  window.metadataFetcher = {
    fetchUrlMetadata
  };
}

