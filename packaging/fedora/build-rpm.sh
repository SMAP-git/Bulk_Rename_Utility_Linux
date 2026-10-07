#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VERSION="${VERSION:-1.0.0}"
BUILD_DIR="$(mktemp -d)"
trap 'rm -rf "$BUILD_DIR"' EXIT
RPM_TOPDIR="$BUILD_DIR/rpmbuild"

if [[ "$(uname -m)" != "x86_64" ]]; then
    printf 'RPM build currently targets x86_64 Linux.\n' >&2
    exit 1
fi
if ! command -v dotnet >/dev/null 2>&1 || ! command -v rpmbuild >/dev/null 2>&1; then
    printf 'The .NET 10 SDK and rpmbuild are required.\n' >&2
    exit 1
fi
if [[ ! "$VERSION" =~ ^[0-9][A-Za-z0-9._+-]*$ ]]; then
    printf 'Invalid RPM package version: %s\n' "$VERSION" >&2
    exit 1
fi

mkdir -p "$RPM_TOPDIR/BUILD" "$RPM_TOPDIR/BUILDROOT" "$RPM_TOPDIR/RPMS" \
    "$RPM_TOPDIR/SOURCES" "$RPM_TOPDIR/SPECS" "$RPM_TOPDIR/SRPMS" \
    "$ROOT_DIR/artifacts/fedora"
SOURCE_NAME="Bulk_Rename_Utility_Linux-${VERSION}.tar.gz"
tar -czf "$RPM_TOPDIR/SOURCES/$SOURCE_NAME" \
    --exclude='*/bin' --exclude='*/obj' --exclude='*/artifacts' \
    -C "$ROOT_DIR" src Directory.Packages.props packaging
cp "$ROOT_DIR/packaging/fedora/bulk-rename-utility.spec" "$RPM_TOPDIR/SPECS/"

rpmbuild -bb --define "_topdir $RPM_TOPDIR" --define "pkgversion $VERSION" \
    "$RPM_TOPDIR/SPECS/bulk-rename-utility.spec"
find "$RPM_TOPDIR/RPMS" -type f -name '*.rpm' -exec cp {} "$ROOT_DIR/artifacts/fedora/" \;
printf 'Created package(s) in %s\n' "$ROOT_DIR/artifacts/fedora"
