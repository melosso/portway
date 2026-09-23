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

SCALAR_VERSION="1.68.0"

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

# npm tarball, checked against registry sha512
vendor_npm() {
    local name="$1" version="$2" path="$3" dest="$4" tarball integrity
    read -r tarball integrity < <(curl -fsSL "https://registry.npmjs.org/$name/$version" \
        | python3 -c "import sys,json; d=json.load(sys.stdin)['dist']; print(d['tarball'], d.get('integrity', ''))")
    fetch "$tarball" "$TMP_DIR/package.tgz"
    python3 - "$TMP_DIR/package.tgz" "$integrity" <<'PY'
import base64, hashlib, sys
algorithm, _, expected = sys.argv[2].partition("-")
if algorithm != "sha512":
    sys.exit(f"no sha512 integrity published, refusing: {sys.argv[2]!r}")
actual = base64.b64encode(hashlib.sha512(open(sys.argv[1], "rb").read()).digest()).decode()
if actual != expected:
    sys.exit("tarball does not match the registry integrity, refusing")
PY
    tar -xzf "$TMP_DIR/package.tgz" -C "$TMP_DIR" "package/$path"
    mv "$TMP_DIR/package/$path" "$dest"
    rm -rf "$TMP_DIR/package" "$TMP_DIR/package.tgz"
}

# Scalar API Reference
echo ""
echo "[Scalar API Reference]"
echo "   Pinned: $SCALAR_VERSION"
vendor_npm "@scalar/api-reference" "$SCALAR_VERSION" "dist/browser/standalone.js" "$VENDOR_DIR/scalar-api-reference.js"
echo "   Verified and saved to: wwwroot/js/vendor/scalar-api-reference.js"

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
