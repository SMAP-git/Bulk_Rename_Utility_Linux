#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VERSION="${VERSION:-1.0.1}"
ARCHITECTURE="$(dpkg --print-architecture)"

if [[ "$ARCHITECTURE" != "amd64" ]]; then
    printf 'Unsupported architecture: %s (expected amd64)\n' "$ARCHITECTURE" >&2
    exit 1
fi
if [[ ! "$VERSION" =~ ^[0-9][A-Za-z0-9.+~:-]*$ ]]; then
    printf 'Invalid Debian package version: %s\n' "$VERSION" >&2
    exit 1
fi

BUILD_DIR="$(mktemp -d)"
trap 'rm -rf "$BUILD_DIR"' EXIT
PUBLISH_DIR="$BUILD_DIR/publish"
PACKAGE_ROOT="$BUILD_DIR/package"
OUTPUT_DIR="$ROOT_DIR/artifacts/debian"
OUTPUT_PACKAGE="$OUTPUT_DIR/bulk-rename-utility_${VERSION}_${ARCHITECTURE}.deb"

dotnet publish "$ROOT_DIR/src/BulkRenameUtility/BulkRenameUtility.csproj" \
    --configuration Release --runtime linux-x64 --self-contained true \
    --output "$PUBLISH_DIR"

install -d "$PACKAGE_ROOT/DEBIAN" "$PACKAGE_ROOT/usr/lib/bulk-rename-utility" \
    "$PACKAGE_ROOT/usr/bin" "$PACKAGE_ROOT/usr/share/applications" \
    "$PACKAGE_ROOT/usr/share/icons/hicolor/512x512/apps"
cp -a "$PUBLISH_DIR/." "$PACKAGE_ROOT/usr/lib/bulk-rename-utility/"
install -m 0644 "$ROOT_DIR/packaging/bulk-rename-utility.desktop" \
    "$PACKAGE_ROOT/usr/share/applications/bulk-rename-utility.desktop"
install -m 0644 "$ROOT_DIR/src/BulkRenameUtility/Assets/bru-linux.png" \
    "$PACKAGE_ROOT/usr/share/icons/hicolor/512x512/apps/bulk-rename-utility.png"
printf '%s\n' '#!/bin/sh' 'exec /usr/lib/bulk-rename-utility/BulkRenameUtility "$@"' \
    > "$PACKAGE_ROOT/usr/bin/BulkRenameUtility"
chmod 0755 "$PACKAGE_ROOT/usr/bin/BulkRenameUtility" \
    "$PACKAGE_ROOT/usr/lib/bulk-rename-utility/BulkRenameUtility"

cat > "$PACKAGE_ROOT/DEBIAN/control" <<EOF
Package: bulk-rename-utility
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCHITECTURE
Maintainer: Bulk Rename Utility contributors
Depends: libfontconfig1, libice6, libsm6, libx11-6, libxext6, libxrender1, libxrandr2, libxcursor1, libxi6
Description: Preview-first batch file renaming utility
 Desktop tool for applying and previewing batch filename transformations.
EOF

mkdir -p "$OUTPUT_DIR"
dpkg-deb --build --root-owner-group "$PACKAGE_ROOT" "$OUTPUT_PACKAGE"
printf 'Created %s\n' "$OUTPUT_PACKAGE"
