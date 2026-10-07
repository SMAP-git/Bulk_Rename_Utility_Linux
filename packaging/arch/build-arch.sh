#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VERSION="${VERSION:-1.0.1}"
BUILD_DIR="$(mktemp -d)"
trap 'rm -rf "$BUILD_DIR"' EXIT

if [[ "$(uname -m)" != "x86_64" ]]; then
    printf 'Arch package build currently targets x86_64 Linux.\n' >&2
    exit 1
fi
if ! command -v makepkg >/dev/null 2>&1 || ! command -v dotnet >/dev/null 2>&1; then
    printf 'Install base-devel and dotnet-sdk before building the Arch package.\n' >&2
    exit 1
fi
if [[ ! "$VERSION" =~ ^[0-9][A-Za-z0-9._+-]*$ ]]; then
    printf 'Invalid package version: %s\n' "$VERSION" >&2
    exit 1
fi

PACKAGE_DIR="$BUILD_DIR/Bulk_Rename_Utility_Linux"
SOURCE_DIR="$PACKAGE_DIR/Bulk_Rename_Utility_Linux"
mkdir -p "$SOURCE_DIR" "$ROOT_DIR/artifacts/arch"
cp -a "$ROOT_DIR/src" "$ROOT_DIR/Directory.Packages.props" "$ROOT_DIR/packaging" "$SOURCE_DIR/"
cp "$ROOT_DIR/packaging/arch/PKGBUILD" "$PACKAGE_DIR/"
sed -i "s/^pkgver=.*/pkgver='$VERSION'/" "$PACKAGE_DIR/PKGBUILD"
tar -czf "$PACKAGE_DIR/Bulk_Rename_Utility_Linux-${VERSION}.tar.gz" \
    -C "$PACKAGE_DIR" Bulk_Rename_Utility_Linux
(cd "$PACKAGE_DIR" && makepkg --clean --force --nodeps --noconfirm)
find "$PACKAGE_DIR" -maxdepth 1 -name '*.pkg.tar.*' \
    -exec cp {} "$ROOT_DIR/artifacts/arch/" \;
printf 'Created package(s) in %s\n' "$ROOT_DIR/artifacts/arch"
