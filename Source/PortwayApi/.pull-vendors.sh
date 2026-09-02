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

# Onest font (Google Fonts / gstatic)
echo ""
echo "[Onest font]"

# Fetch the CSS from Google Fonts (Chrome UA to get woff2 + variable-font ranges)
FONTS_CSS="$(curl -fsSL \
    -A "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36" \
    "https://fonts.googleapis.com/css2?family=Onest:wght@400..700&display=swap")"

# Parse out the version tag (e.g. v6) from one of the URLs
ONEST_VERSION="$(echo "$FONTS_CSS" \
    | grep -oP 'gstatic\.com/s/onest/\K[^/]+' \
    | head -1)"
echo "   Latest: Onest ${ONEST_VERSION}"

# Onest ships latin and latin-ext only; the @font-face blocks live at the top of site.css
SUBSETS=("latin-ext" "latin")

TMP_CSS="$(mktemp)"

for subset in "${SUBSETS[@]}"; do
    filename="onest-${subset}.woff2"

    url="$(echo "$FONTS_CSS" \
        | grep -A6 "/\* $subset \*/" \
        | grep -oP 'https://[^\)]+\.woff2' \
        | head -1)"

    if [ -z "$url" ]; then
        echo "   ERROR: no URL found for subset '$subset'"
        rm -f "$TMP_CSS"
        exit 1
    fi

    unicode_range="$(echo "$FONTS_CSS" \
        | grep -A8 "/\* $subset \*/" \
        | grep "unicode-range" \
        | head -1 \
        | sed 's/^[[:space:]]*//' \
        | tr -d ';')"

    fetch "$url" "$FONTS_DIR/$filename"

    cat >> "$TMP_CSS" <<CSS
@font-face {
    font-family: 'Onest';
    font-style: normal;
    font-weight: 400 700;
    font-display: swap;
    src: url('../fonts/$filename') format('woff2');
    $unicode_range;
}

CSS
done

# Splice the generated blocks in ahead of the first :root, leaving the rest of site.css untouched
SITE_CSS="$CSS_DIR/site.css"
if ! grep -q '^:root {' "$SITE_CSS"; then
    echo "   ERROR: no ':root {' anchor in site.css, refusing to rewrite it"
    rm -f "$TMP_CSS"
    exit 1
fi

TMP_SITE="$(mktemp)"
cat "$TMP_CSS" > "$TMP_SITE"
sed -n '/^:root {/,$p' "$SITE_CSS" >> "$TMP_SITE"
mv "$TMP_SITE" "$SITE_CSS"
rm -f "$TMP_CSS"
echo "   Updated: wwwroot/css/site.css (@font-face blocks)"

# Done
echo ""
echo "Done. Vendor assets are up to date."
echo "  Scalar : $SCALAR_VERSION  → wwwroot/js/vendor/scalar-api-reference.js"
echo "  Onest  : ${ONEST_VERSION}       → wwwroot/fonts/ + wwwroot/css/site.css"
