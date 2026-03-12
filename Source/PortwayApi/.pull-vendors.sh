#!/usr/bin/env bash
set -euo pipefail

# Paths
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FONTS_DIR="$SCRIPT_DIR/wwwroot/fonts"
VENDOR_DIR="$SCRIPT_DIR/wwwroot/js/vendor"
CSS_DIR="$SCRIPT_DIR/wwwroot/css"

mkdir -p "$FONTS_DIR" "$VENDOR_DIR"

# Helpers
fetch() {
    local url="$1" dest="$2"
    echo "  ↓ $(basename "$dest")"
    curl -fsSL "$url" -o "$dest"
}

latest_npm_version() {
    curl -fsSL "https://registry.npmjs.org/$1/latest" \
        | python3 -c "import sys,json; print(json.load(sys.stdin)['version'])"
}

# Scalar API Reference
echo ""
echo "[Scalar API Reference]"
SCALAR_VERSION="$(latest_npm_version "@scalar/api-reference")"
echo "   Latest: $SCALAR_VERSION"
fetch \
    "https://cdn.jsdelivr.net/npm/@scalar/api-reference@${SCALAR_VERSION}" \
    "$VENDOR_DIR/scalar-api-reference.js"
echo "   Saved to: wwwroot/js/vendor/scalar-api-reference.js"

# Inter font (Google Fonts / gstatic)
echo ""
echo "[Inter font]"

# Fetch the CSS from Google Fonts (Chrome UA to get woff2 + variable-font ranges)
FONTS_CSS="$(curl -fsSL \
    -A "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36" \
    "https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap")"

# Parse out the version tag (e.g. v20) from one of the URLs
INTER_VERSION="$(echo "$FONTS_CSS" \
    | grep -oP 'gstatic\.com/s/inter/\K[^/]+' \
    | head -1)"
echo "   Latest: Inter ${INTER_VERSION}"

# Extract unique woff2 URLs and their unicode-range blocks, download each file
# Map subset comment → local filename
declare -A SUBSET_MAP=(
    ["cyrillic-ext"]="inter-cyrillic-ext.woff2"
    ["cyrillic"]="inter-cyrillic.woff2"
    ["greek-ext"]="inter-greek-ext.woff2"
    ["greek"]="inter-greek.woff2"
    ["vietnamese"]="inter-vietnamese.woff2"
    ["latin-ext"]="inter-latin-ext.woff2"
    ["latin"]="inter-latin.woff2"
)

# Build a temporary file for the new inter.css
TMP_CSS="$(mktemp)"
cat > "$TMP_CSS" <<CSS
/* Inter ${INTER_VERSION} — self-hosted, variable font (weights 400/700) */

CSS

# Process each subset — find unique urls per subset name (first occurrence per subset)
for subset in "cyrillic-ext" "cyrillic" "greek-ext" "greek" "vietnamese" "latin-ext" "latin"; do
    filename="${SUBSET_MAP[$subset]}"

    # Extract the woff2 URL for this subset (pick one — all weights share the same file)
    url="$(echo "$FONTS_CSS" \
        | grep -A6 "/\* $subset \*/" \
        | grep -oP 'https://[^\)]+\.woff2' \
        | head -1)"

    if [ -z "$url" ]; then
        echo "   WARN: no URL found for subset '$subset', skipping"
        continue
    fi

    # Extract the unicode-range for this subset (first occurrence)
    unicode_range="$(echo "$FONTS_CSS" \
        | grep -A8 "/\* $subset \*/" \
        | grep "unicode-range" \
        | head -1 \
        | sed 's/^[[:space:]]*//' \
        | tr -d ';')"

    fetch "$url" "$FONTS_DIR/$filename"

    # Append @font-face block for this subset
    cat >> "$TMP_CSS" <<CSS
/* $subset */
@font-face {
  font-family: 'Inter';
  font-style: normal;
  font-weight: 400 700;
  font-display: swap;
  src: url('/fonts/$filename') format('woff2');
  $unicode_range;
}
CSS
done

# Replace inter.css
mv "$TMP_CSS" "$CSS_DIR/inter.css"
echo "   Updated: wwwroot/css/inter.css"

# Done
echo ""
echo "Done. Vendor assets are up to date."
echo "  Scalar : $SCALAR_VERSION  → wwwroot/js/vendor/scalar-api-reference.js"
echo "  Inter  : ${INTER_VERSION}       → wwwroot/fonts/ + wwwroot/css/inter.css"
