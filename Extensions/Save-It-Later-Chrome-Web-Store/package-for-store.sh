#!/bin/bash

# Package Chrome Extension for Chrome Web Store
# This script creates a ZIP file ready for upload

echo "📦 Packaging Chrome Extension for Chrome Web Store..."

# Check if we're in the chrome-extension directory
if [ ! -f "manifest.json" ]; then
    echo "❌ Error: manifest.json not found."
    echo "   Please run this script from the chrome-extension/ directory"
    exit 1
fi

# Set output filename
OUTPUT_FILE="../save-it-later-extension-v$(grep -o '"version": "[^"]*"' manifest.json | cut -d'"' -f4).zip"

# Remove old ZIP if exists
if [ -f "$OUTPUT_FILE" ]; then
    echo "🗑️  Removing old ZIP file..."
    rm "$OUTPUT_FILE"
fi

# Create ZIP file (exclude documentation and git files)
echo "📦 Creating ZIP file..."
zip -r "$OUTPUT_FILE" . \
    -x "*.md" \
    -x ".git/*" \
    -x "*.gitignore" \
    -x ".DS_Store" \
    -x "PUBLISH_GUIDE.md" \
    -x "README.md" \
    -x "SAVE_FLOW.md" \
    -x "SECURITY.md" \
    -x "package-for-store.sh"

if [ $? -eq 0 ]; then
    echo ""
    echo "✅ Extension packaged successfully!"
    echo ""
    echo "📁 Output file: $OUTPUT_FILE"
    echo ""
    echo "📋 File size: $(du -h "$OUTPUT_FILE" | cut -f1)"
    echo ""
    echo "📝 Next steps:"
    echo "   1. Go to: https://chrome.google.com/webstore/devconsole"
    echo "   2. Click 'New Item'"
    echo "   3. Upload: $OUTPUT_FILE"
    echo ""
    echo "📖 See PUBLISH_GUIDE.md for detailed instructions"
else
    echo "❌ Failed to create ZIP file"
    exit 1
fi

