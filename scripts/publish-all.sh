#!/usr/bin/env bash
# publish-all.sh: Run dotnet publish for every supported platform RID.
#
# Builds all five RIDs by default; pass one or more RIDs to build only those.
# When both macOS targets are built, runs lipo to combine the osx-x64 and
# osx-arm64 outputs into the osx-universal binary.
#
# Usage:  ./scripts/publish-all.sh [RID...]
# Example: ./scripts/publish-all.sh linux-x64 win-x64
# Deps:   Docker (mcr.microsoft.com/dotnet/sdk:10.0, ghcr.io/tzarc/qmk_toolchains:builder)

set -eEuo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel)"
ALL_BUILD_TARGETS="linux-x64 linux-arm64 osx-x64 osx-arm64 win-x64"
REQUESTED_BUILD_TARGETS="${@:-$ALL_BUILD_TARGETS}"

# Run the container as the invoking user; otherwise the publish output is root-owned.
if [ "$(id -u)" -ne 0 ]; then
    DOCKER_RUN_USER="-u $(id -u):$(id -g)"
else
    DOCKER_RUN_USER=""
fi

cd "${REPO_ROOT}"
rm -rf "${REPO_ROOT}/"publish-* "${REPO_ROOT}/artifacts"
for RID in $REQUESTED_BUILD_TARGETS; do
    docker run --rm \
        ${DOCKER_RUN_USER} \
        -e HOME=/tmp \
        -v "${REPO_ROOT}":/app \
        -w /app/src/QmkToolbox.Desktop \
        mcr.microsoft.com/dotnet/sdk:10.0 \
        dotnet publish -o ../../publish-${RID} -r ${RID} -c Release
done

if [[ -d "${REPO_ROOT}/publish-osx-x64" && -d "${REPO_ROOT}/publish-osx-arm64" ]]; then
    # The publish output is a single self-contained executable (PublishSingleFile=true),
    # so lipo only needs the one binary.
    mkdir -p "${REPO_ROOT}/publish-osx-universal"
    docker run --rm \
        -v "${REPO_ROOT}":/app \
        -e TC_WORKDIR=/app \
        -w /app \
        ghcr.io/tzarc/qmk_toolchains:builder \
        aarch64-apple-darwin24-lipo -create \
            publish-osx-x64/qmk_toolbox \
            publish-osx-arm64/qmk_toolbox \
            -output publish-osx-universal/qmk_toolbox
fi
