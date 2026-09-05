#!/usr/bin/env bash
# fetch-tools.sh: Download release binaries from four upstream repositories and
# place them in the repository's resources/ tree:
#
#   qmk_flashutils        flash tool binaries (avrdude, dfu-util, etc.) for all platforms
#   qmk_hidapi            hidapi native library for all platforms
#   qmk_driver_installer  WinUSB driver installer (Windows only)
#   qmk_udev              udev rules + qmk_id helper binary (Linux only)
#
# All outputs live in version control so builds and CI need no network access.
# Run this script to pick up new upstream releases.
#
# Usage:  ./scripts/fetch-tools.sh
# Deps:   curl, jq, zstd, tar

set -eEuo pipefail

for cmd in curl jq zstd tar; do
    command -v "${cmd}" >/dev/null 2>&1 || { echo "Error: required command '${cmd}' not found." >&2; exit 1; }
done

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel)"

# Map: resource directory name -> qmk_flashutils platform tag
# Both macOS architectures share one set of universal binaries.
declare -A PLATFORMS=(
    ["linux-x64"]="linuxX64"
    ["linux-arm64"]="linuxARM64"
    ["osx"]="macosUNIVERSAL"
    ["win-x64"]="windowsX64"
)

# Name that HidApi.Net 1.x searches for on each platform
# (see badcel/HidApi.Net NativeHidApiLibrary.cs)
declare -A HIDAPI_NAMES=(
    ["linux-x64"]="libhidapi-hidraw.so.0"
    ["linux-arm64"]="libhidapi-hidraw.so.0"
    ["osx"]="libhidapi.dylib"
    ["win-x64"]="hidapi.dll"
)

TOOLS_ROOT="${REPO_ROOT}/resources/flashutils"
HIDAPI_ROOT="${REPO_ROOT}/resources/hidapi"

# Scratch dir for downloads and archive extraction, cleaned up on exit.
SCRATCH_DIR="$(mktemp -d)"
trap 'rm -rf "${SCRATCH_DIR}"' EXIT

CURL_OPTS=(-fsSL)

FLASHUTILS_TAG="$(curl "${CURL_OPTS[@]}" "https://api.github.com/repos/qmk/qmk_flashutils/releases/latest" | jq -r '.tag_name')"
echo "qmk_flashutils release: ${FLASHUTILS_TAG}"
BASE_URL="https://github.com/qmk/qmk_flashutils/releases/download/${FLASHUTILS_TAG}"

fetch_archive() {
    local name="$1"
    echo "  Downloading ${name}..." >&2
    curl "${CURL_OPTS[@]}" -o "${SCRATCH_DIR}/${name}" "${BASE_URL}/${name}" >&2
    echo "${SCRATCH_DIR}/${name}"
}

for RID in "${!PLATFORMS[@]}"; do
    PLATFORM="${PLATFORMS[$RID]}"
    HIDAPI_NAME="${HIDAPI_NAMES[$RID]}"

    echo "=== ${RID} (qmk platform: ${PLATFORM}) ==="

    TOOLS_DIR="${TOOLS_ROOT}/${RID}"
    HIDAPI_DIR="${HIDAPI_ROOT}/${RID}"
    mkdir -p "${TOOLS_DIR}" "${HIDAPI_DIR}"

    echo "  qmk_flashutils-${FLASHUTILS_TAG}-${PLATFORM}.tar.zst -> ${TOOLS_DIR}"
    TOOLS_ARCHIVE="$(fetch_archive "qmk_flashutils-${FLASHUTILS_TAG}-${PLATFORM}.tar.zst")"
    tar --zstd -xf "${TOOLS_ARCHIVE}" --strip-components=1 -C "${TOOLS_DIR}"

    # Drop tools QMK Toolbox does not use
    rm -f "${TOOLS_DIR}"/dfu-prefix "${TOOLS_DIR}"/dfu-prefix.exe \
          "${TOOLS_DIR}"/dfu-suffix "${TOOLS_DIR}"/dfu-suffix.exe

    # hidapi native library: extract to a temp dir then rename to the name
    # HidApi.Net 1.x searches for on this platform.
    echo "  qmk_hidapi-${FLASHUTILS_TAG}-${PLATFORM}.tar.zst -> ${HIDAPI_DIR}"
    HIDAPI_ARCHIVE="$(fetch_archive "qmk_hidapi-${FLASHUTILS_TAG}-${PLATFORM}.tar.zst")"
    HIDAPI_TMP="${SCRATCH_DIR}/hidapi-${RID}"
    mkdir -p "${HIDAPI_TMP}"
    tar --zstd -xf "${HIDAPI_ARCHIVE}" --strip-components=1 -C "${HIDAPI_TMP}"

    # The library name inside the archive varies
    HIDAPI_SRC="$(find "${HIDAPI_TMP}" -maxdepth 1 -type f \( \
        -name '*.so' -o -name '*.so.*' -o -name '*.dylib' -o -name '*.dll' \
    \) | head -1)"

    if [[ -z "${HIDAPI_SRC}" ]]; then
        echo "  ERROR: no library file found in qmk_hidapi-${PLATFORM} archive" >&2
        exit 1
    fi

    cp "${HIDAPI_SRC}" "${HIDAPI_DIR}/${HIDAPI_NAME}"

    HIDAPI_MANIFEST="$(find "${HIDAPI_TMP}" -maxdepth 1 -name 'hidapi_release_*' | head -1)"
    if [[ -n "${HIDAPI_MANIFEST}" ]]; then
        cp "${HIDAPI_MANIFEST}" "${HIDAPI_DIR}/$(basename "${HIDAPI_MANIFEST}")"
    fi

    if [[ "${RID}" != win-* ]]; then
        chmod +x "${TOOLS_DIR}"/* 2>/dev/null || true
    fi

    echo "  Done."
done

# ── Windows-only: qmk_driver_installer ───────────────────────────────────────
# Embedded as a resource in Windows builds.
DRIVER_INSTALLER_ROOT="${REPO_ROOT}/resources/windows-drivers"
mkdir -p "${DRIVER_INSTALLER_ROOT}"

echo ""
echo "=== qmk_driver_installer (win-x64 only) ==="

DRIVER_INSTALLER_REPO="https://github.com/qmk/qmk_driver_installer"
DRIVER_INSTALLER_URL="${DRIVER_INSTALLER_REPO}/releases/latest/download/qmk_driver_installer.exe"
DRIVER_INSTALLER_DEST="${DRIVER_INSTALLER_ROOT}/qmk_driver_installer.exe"

echo "  Downloading qmk_driver_installer.exe..."
curl "${CURL_OPTS[@]}" -L -o "${DRIVER_INSTALLER_DEST}" "${DRIVER_INSTALLER_URL}"
echo "  Saved to ${DRIVER_INSTALLER_DEST}"
echo "  Done."

# ── Linux-only: qmk_udev (qmk_id helper + udev rules) ───────────────────────
declare -A UDEV_PLATFORMS=(
    ["linux-x64"]="linuxX64"
    ["linux-arm64"]="linuxARM64"
)

UDEV_ROOT="${REPO_ROOT}/resources/udev"
mkdir -p "${UDEV_ROOT}"

echo ""
echo "=== qmk_udev ==="

UDEV_TAG="$(curl "${CURL_OPTS[@]}" "https://api.github.com/repos/qmk/qmk_udev/releases/latest" | jq -r '.tag_name')"
echo "qmk_udev release: ${UDEV_TAG}"
UDEV_BASE_URL="https://github.com/qmk/qmk_udev/releases/download/${UDEV_TAG}"

echo "  Downloading 50-qmk.rules..."
curl "${CURL_OPTS[@]}" -o "${UDEV_ROOT}/50-qmk.rules" "${UDEV_BASE_URL}/50-qmk.rules"

for RID in "${!UDEV_PLATFORMS[@]}"; do
    PLATFORM="${UDEV_PLATFORMS[$RID]}"
    UDEV_DIR="${UDEV_ROOT}/${RID}"
    mkdir -p "${UDEV_DIR}"
    echo "  Downloading qmk_id-${PLATFORM} -> ${UDEV_DIR}/qmk_id"
    curl "${CURL_OPTS[@]}" -o "${UDEV_DIR}/qmk_id" "${UDEV_BASE_URL}/qmk_id-${PLATFORM}"
    chmod +x "${UDEV_DIR}/qmk_id"

    # Per-arch release manifest for version-checking at runtime.
    cat > "${UDEV_DIR}/qmk_udev_release_${PLATFORM}" <<MANIFEST
COMMIT_DATE=${UDEV_TAG}
COMMIT_HASH=${UDEV_TAG}
MANIFEST
done

echo "  Done."

echo ""
echo "All platforms fetched into resources/."
echo "Commit the result to update the bundled binaries."
