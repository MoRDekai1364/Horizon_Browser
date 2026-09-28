// Categorization service for Chrome extension
// Generates auto tags based on URL, title, and description
// Ported from lib/services/categorization_service.dart

const CategorizationService = {
  // Platform keywords mapping
  _platformKeywords: {
    'social': [
      'facebook.com', 'instagram.com', 'twitter.com', 'x.com', 'tiktok.com',
      'linkedin.com', 'snapchat.com', 'pinterest.com', 'reddit.com'
    ],
    'video': [
      'youtube.com', 'youtu.be', 'vimeo.com', 'twitch.tv', 'tiktok.com',
      'instagram.com/tv', 'facebook.com/watch'
    ],
    'news': [
      'cnn.com', 'bbc.com', 'reuters.com', 'nytimes.com', 'washingtonpost.com',
      'theguardian.com', 'bloomberg.com', 'wsj.com', 'ft.com'
    ],
    'shopping': [
      'amazon.com', 'ebay.com', 'etsy.com', 'shopify.com', 'alibaba.com',
      'walmart.com', 'target.com', 'bestbuy.com'
    ],
    'education': [
      'coursera.org', 'udemy.com', 'khanacademy.org', 'edx.org', 'mit.edu',
      'stanford.edu', 'harvard.edu', 'youtube.com/education'
    ],
    'development': [
      'github.com', 'stackoverflow.com', 'dev.to', 'medium.com/@',
      'hashnode.com', 'freecodecamp.org', 'codepen.io'
    ],
    'entertainment': [
      'netflix.com', 'hulu.com', 'disney.com', 'hbo.com'
    ],
    'audio': [
      'spotify.com', 'open.spotify.com', 'music.apple.com', 'soundcloud.com',
      'music.youtube.com', 'pandora.com', 'deezer.com', 'tidal.com',
      'bandcamp.com', 'audiomack.com', 'mixcloud.com'
    ]
  },

  // Enhanced content keywords
  _enhancedContentKeywords: {
    'work': [
      'meeting', 'project', 'deadline', 'work', 'job', 'career', 'business',
      'office', 'team', 'client', 'presentation', 'report', 'conference',
      'interview', 'resume', 'cv', 'employment', 'professional'
    ],
    'learning': [
      'tutorial', 'course', 'learn', 'study', 'education', 'training',
      'how to', 'guide', 'lesson', 'class', 'academy', 'university',
      'skill', 'knowledge', 'research', 'documentation', 'manual'
    ],
    'shopping': [
      'buy', 'purchase', 'deal', 'sale', 'price', 'shop', 'store',
      'discount', 'offer', 'product', 'item', 'cart', 'checkout',
      'shipping', 'delivery', 'review', 'rating', 'compare'
    ],
    'travel': [
      'trip', 'vacation', 'hotel', 'flight', 'travel', 'destination',
      'booking', 'reservation', 'tourism', 'adventure', 'journey',
      'airline', 'accommodation', 'itinerary', 'passport', 'visa'
    ],
    'health': [
      'fitness', 'workout', 'diet', 'health', 'medical', 'exercise',
      'nutrition', 'wellness', 'gym', 'yoga', 'running', 'training',
      'doctor', 'hospital', 'medicine', 'therapy', 'mental health'
    ],
    'finance': [
      'money', 'investment', 'budget', 'finance', 'banking', 'crypto',
      'stock', 'trading', 'savings', 'loan', 'credit', 'insurance',
      'retirement', 'tax', 'expense', 'income', 'wealth'
    ],
    'tech': [
      'code', 'programming', 'software', 'tech', 'development', 'coding',
      'app', 'website', 'database', 'api', 'algorithm', 'debug',
      'framework', 'library', 'tool', 'system', 'computer'
    ],
    'entertainment': [
      'movie', 'game', 'fun', 'entertainment', 'hobby', 'leisure',
      'music', 'book', 'series', 'show', 'comedy', 'drama',
      'sport', 'gaming', 'streaming', 'podcast', 'comic'
    ],
    'personal': [
      'family', 'friend', 'personal', 'home', 'life', 'relationship',
      'birthday', 'anniversary', 'wedding', 'baby', 'pet',
      'hobby', 'interest', 'passion', 'dream', 'goal'
    ],
    'news': [
      'news', 'article', 'breaking', 'update', 'report', 'story',
      'politics', 'world', 'local', 'national', 'international',
      'economy', 'sports', 'technology', 'science', 'weather'
    ]
  },

  // Content keywords
  _contentKeywords: {
    'idea': [
      'idea', 'concept', 'brainstorm', 'innovation', 'creative', 'inspiration',
      'thought', 'suggestion', 'proposal', 'vision'
    ],
    'task': [
      'todo', 'task', 'reminder', 'deadline', 'schedule', 'meeting',
      'appointment', 'due', 'complete', 'finish', 'do this'
    ],
    'note': [
      'note', 'memo', 'remember', 'important', 'reference', 'info',
      'information', 'details', 'summary', 'recap'
    ],
    'app': [
      'app', 'application', 'software', 'tool', 'download', 'install',
      'mobile app', 'desktop app', 'web app', 'extension', 'ios app', 'android app',
      'play store', 'app store', 'chrome extension'
    ],
    'anime': [
      'anime', 'manga', 'otaku', 'japanese animation', 'anime series',
      'anime episode', 'anime movie', 'crunchyroll', 'funimation',
      'myanimelist', 'anilist', 'shonen', 'shoujo', 'seinen', 'josei',
      'naruto', 'one piece', 'dragon ball', 'attack on titan', 'demon slayer',
      'jujutsu kaisen', 'tokyo ghoul', 'death note', 'fullmetal alchemist'
    ],
    'cartoon': [
      'cartoon', 'animation', 'animated', 'cartoon series', 'animated show',
      'disney cartoon', 'cartoon network', 'nickelodeon', 'adult swim',
      'rick and morty', 'south park', 'family guy', 'simpsons', 'futurama',
      'adventure time', 'regular show', 'spongebob', 'avatar the last airbender'
    ],
    'books': [
      'book', 'novel', 'ebook', 'kindle', 'reading', 'author', 'writer',
      'literature', 'fiction', 'non-fiction', 'biography', 'memoir',
      'poetry', 'poem', 'short story', 'chapter', 'page', 'library',
      'goodreads', 'book review', 'bookstore', 'publisher'
    ],
    'manga': [
      'manga', 'comic', 'graphic novel', 'manhwa', 'manhua', 'webtoon',
      'manga chapter', 'manga volume', 'manga scan', 'manga reader',
      'mangadex', 'mangakakalot', 'read manga', 'manga online'
    ],
    'rated x': [
      'rated x', 'adult', 'nsfw', '18+', 'explicit', 'mature content',
      'adult content', 'xxx', 'porn', 'pornography', 'hentai', 'ecchi',
      'adult video', 'adult site', 'adult entertainment'
    ],
    'mp4': [
      'mp4', 'video file', 'download video', 'video download', 'video format',
      '.mp4', 'video player', 'video streaming', 'video content',
      'movie download', 'video clip', 'video file download'
    ]
  },

  // Helper: Check if URL is valid
  _isValidUrl(url) {
    try {
      const uri = new URL(url);
      return uri.protocol === 'http:' || uri.protocol === 'https:';
    } catch (e) {
      return false;
    }
  },

  // Helper: Check if text contains any keywords
  _containsKeywords(text, keywords) {
    return keywords.some(keyword => text.includes(keyword));
  },

  // Get platform from URL
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

  // Get category from URL (same as platform for now)
  _getCategoryFromUrl(url) {
    return this._getPlatformFromUrl(url);
  },

  // Analyze URL patterns
  analyzeUrlPattern(url) {
    if (!this._isValidUrl(url)) return 'other';

    try {
      const uri = new URL(url);
      const path = uri.path.toLowerCase();
      const host = uri.host.toLowerCase();

      // Video content patterns
      if (path.includes('/video/') ||
          path.includes('/watch') ||
          path.includes('/v/') ||
          host.includes('youtube') ||
          host.includes('vimeo') ||
          host.includes('tiktok')) {
        return 'video';
      }

      // Article content patterns
      if (path.includes('/article/') ||
          path.includes('/news/') ||
          path.includes('/post/') ||
          path.includes('/blog/') ||
          path.includes('/story/')) {
        return 'article';
      }

      // Shopping patterns
      if (path.includes('/product/') ||
          path.includes('/shop/') ||
          path.includes('/buy/') ||
          path.includes('/item/') ||
          host.includes('amazon') ||
          host.includes('ebay')) {
        return 'shopping';
      }

      // Learning patterns
      if (path.includes('/tutorial/') ||
          path.includes('/learn/') ||
          path.includes('/course/') ||
          path.includes('/lesson/') ||
          path.includes('/guide/')) {
        return 'learning';
      }

      // Work patterns
      if (path.includes('/meeting/') ||
          path.includes('/project/') ||
          path.includes('/work/') ||
          host.includes('linkedin') ||
          host.includes('slack')) {
        return 'work';
      }

      // Entertainment patterns
      if (path.includes('/movie/') ||
          path.includes('/show/') ||
          path.includes('/game/') ||
          host.includes('netflix') ||
          host.includes('hulu')) {
        return 'entertainment';
      }

      // Anime patterns
      if (host.includes('crunchyroll') ||
          host.includes('funimation') ||
          host.includes('myanimelist') ||
          host.includes('anilist') ||
          host.includes('anime') ||
          path.includes('/anime/')) {
        return 'anime';
      }

      // Manga patterns
      if (host.includes('mangadex') ||
          host.includes('mangakakalot') ||
          host.includes('webtoon') ||
          host.includes('manga') ||
          path.includes('/manga/') ||
          path.includes('/chapter/')) {
        return 'manga';
      }

      // Books patterns
      if (host.includes('goodreads') ||
          host.includes('amazon.com/kindle') ||
          host.includes('book') ||
          path.includes('/book/') ||
          path.includes('/novel/') ||
          path.includes('/ebook/')) {
        return 'books';
      }

      // MP4/Video file patterns
      if (url.toLowerCase().endsWith('.mp4') ||
          url.toLowerCase().endsWith('.avi') ||
          url.toLowerCase().endsWith('.mov') ||
          url.toLowerCase().endsWith('.mkv') ||
          path.includes('/video/') ||
          path.includes('/download/')) {
        return 'mp4';
      }

      return 'other';
    } catch (e) {
      return 'other';
    }
  },

  // Get suggested tags (main function)
  getSuggestedTags({ url, title, description = null, content = null }) {
    const tags = [];

    // Add platform-based tags
    const platform = this._getPlatformFromUrl(url);
    if (platform) {
      tags.push(platform);
    }

    // Add category-based tags from URL pattern analysis
    const urlPattern = this.analyzeUrlPattern(url);
    if (urlPattern !== 'other') {
      tags.push(urlPattern);
    }

    // Add category-based tags
    const category = this._getCategoryFromUrl(url);
    if (category) {
      tags.push(category);
    }

    // Add content-based tags
    const textToAnalyze = `${title.toLowerCase()} ${description ? description.toLowerCase() : ''} ${content ? content.toLowerCase() : ''}`;

    // Check content keywords
    for (const [tag, keywords] of Object.entries(this._contentKeywords)) {
      if (this._containsKeywords(textToAnalyze, keywords)) {
        tags.push(tag);
      }
    }

    // Check enhanced content keywords
    for (const [tag, keywords] of Object.entries(this._enhancedContentKeywords)) {
      if (this._containsKeywords(textToAnalyze, keywords)) {
        tags.push(tag);
      }
    }

    // Remove duplicates and return
    return [...new Set(tags)];
  }
};

// Export for use in other scripts
if (typeof window !== 'undefined') {
  window.CategorizationService = CategorizationService;
}

