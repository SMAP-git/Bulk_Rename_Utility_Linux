#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
FLATPAK_DIR="$ROOT_DIR/packaging/flatpak"
APP_ID="io.github.smapgit.bulkrenameutility"
MANIFEST="$FLATPAK_DIR/$APP_ID.yml"
STAGE_DIR="$FLATPAK_DIR/.build-input"
OUTPUT_DIR="$ROOT_DIR/artifacts/flatpak"
OUTPUT_FILE="$OUTPUT_DIR/$APP_ID.flatpak"
BUILD_DIR="$(mktemp -d)"

if [[ "$(uname -m)" != "x86_64" ]]; then
    printf 'Flatpak build currently targets x86_64 Linux.\n' >&2
    exit 1
fi
if ! command -v dotnet >/dev/null 2>&1 ||
    ! command -v flatpak >/dev/null 2>&1 ||
    ! command -v flatpak-builder >/dev/null 2>&1; then
    printf 'The .NET 10 SDK, Flatpak, and flatpak-builder are required.\n' >&2
    exit 1
fi
if [[ -e "$STAGE_DIR" ]]; then
    printf 'Flatpak staging directory already exists: %s\n' "$STAGE_DIR" >&2
    exit 1
fi

mkdir -p "$STAGE_DIR/publish" "$OUTPUT_DIR"
trap 'rm -rf "$STAGE_DIR" "$BUILD_DIR"' EXIT
sed -e 's/^Exec=.*/Exec=bulk-rename-utility/' \
    -e 's/^Icon=.*/Icon=io.github.smapgit.bulkrenameutility/' \
    "$ROOT_DIR/packaging/bulk-rename-utility.desktop" \
    > "$STAGE_DIR/bulk-rename-utility.desktop"
cp "$ROOT_DIR/src/BulkRenameUtility/Assets/bru-linux.png" "$STAGE_DIR/"
cp "$FLATPAK_DIR/$APP_ID.metainfo.xml" "$STAGE_DIR/"
cat > "$STAGE_DIR/bulk-rename-utility-launcher" <<'EOF'
#!/bin/sh
exec /app/lib/bulk-rename-utility/BulkRenameUtility "$@"
EOF
chmod 0755 "$STAGE_DIR/bulk-rename-utility-launcher"

dotnet publish "$ROOT_DIR/src/BulkRenameUtility/BulkRenameUtility.csproj" \
    --configuration Release --runtime linux-x64 --self-contained true \
    --output "$STAGE_DIR/publish"
flatpak-builder --user --force-clean --disable-rofiles-fuse --default-branch=stable \
    --repo="$BUILD_DIR/repo" "$BUILD_DIR/build" "$MANIFEST"
flatpak build-bundle "$BUILD_DIR/repo" "$OUTPUT_FILE" "$APP_ID" stable
printf 'Created %s\n' "$OUTPUT_FILE"
