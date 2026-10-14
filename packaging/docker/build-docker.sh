#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VERSION="${VERSION:-1.0.2}"
DEB_PACKAGE="${DEB_PACKAGE:-$ROOT_DIR/artifacts/debian/bulk-rename-utility_${VERSION}_amd64.deb}"
IMAGE_NAME="${IMAGE_NAME:-bulk-rename-utility:${VERSION}}"
BUILD_DIR="$(mktemp -d)"
trap 'rm -rf "$BUILD_DIR"' EXIT

if [[ ! -f "$DEB_PACKAGE" ]]; then
    printf 'Debian package not found: %s\nBuild it first with packaging/debian/build-deb.sh.\n' "$DEB_PACKAGE" >&2
    exit 1
fi
if ! command -v docker >/dev/null 2>&1 || ! docker info >/dev/null 2>&1; then
    printf 'Docker is not installed or the current user cannot access the Docker daemon.\n' >&2
    exit 1
fi

cp "$ROOT_DIR/packaging/docker/Dockerfile" "$BUILD_DIR/Dockerfile"
cp "$ROOT_DIR/packaging/docker/docker-entrypoint.sh" "$BUILD_DIR/docker-entrypoint.sh"
cp "$ROOT_DIR/packaging/docker/openbox-rc.xml" "$BUILD_DIR/openbox-rc.xml"
cp "$DEB_PACKAGE" "$BUILD_DIR/bulk-rename-utility.deb"
docker build --tag "$IMAGE_NAME" "$BUILD_DIR"
printf 'Created Docker image %s\n' "$IMAGE_NAME"
