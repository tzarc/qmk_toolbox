#!/usr/bin/env bash
# make-macos-dmg.sh: Build a DMG disk image from the existing .app bundle.
#
# Packages whatever bundle is on disk, so when signing runs between building
# and packaging the DMG picks up the signed bundle.
#
# Input:  publish-osx-dmg/QMK Toolbox.app  (from make-macos-app.sh)
# Output: artifacts/QMK Toolbox.dmg
# Usage:  ./scripts/make-macos-dmg.sh
# Deps:   Docker (ghcr.io/tzarc/qmk_toolchains:builder)

set -eEuo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel)"
ARTIFACTS_DIR="${REPO_ROOT}/artifacts"
APP_SRC="${REPO_ROOT}/publish-osx-dmg/QMK Toolbox.app"

if [[ ! -d "${APP_SRC}" ]]; then
    echo "Error: .app bundle not found: ${APP_SRC}" >&2
    echo "Run ./scripts/make-macos-app.sh first." >&2
    exit 1
fi
mkdir -p "${ARTIFACTS_DIR}"

echo "=== Building macOS .dmg from ${APP_SRC} ==="

# genisoimage comes from the container, not the host. It appends to an existing
# image, so remove the old DMG first.
rm -f "${ARTIFACTS_DIR}/QMK Toolbox.dmg"
docker run --rm -i \
    -v "${REPO_ROOT}:/app" \
    -w /app \
    -e TC_WORKDIR=/app \
    ghcr.io/tzarc/qmk_toolchains:builder \
    bash << 'DMG'
set -euo pipefail
genisoimage -V "QMK Toolbox" -D -R -apple -no-pad -o "artifacts/QMK Toolbox.dmg" publish-osx-dmg
DMG

echo "  Done: ${ARTIFACTS_DIR}/QMK Toolbox.dmg"
