#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIRECTORY="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
readonly SCRIPT_DIRECTORY
REPOSITORY_ROOT="$(cd -- "${SCRIPT_DIRECTORY}/../.." && pwd -P)"
readonly REPOSITORY_ROOT
readonly GH_COMMAND="${MEMORIA_NOTE_GH_COMMAND:-gh}"

fail() {
    echo "error: $*" >&2
    exit 1
}

usage() {
    cat <<'EOF'
Usage: generate-release-artifact-profiles.sh <tag>

Download a published Memoria Note release and generate its Linux and Windows
performance artifact profiles. The tag must use v<major>.<minor>.<patch>.
EOF
}

find_python() {
    if command -v python3 >/dev/null 2>&1; then
        command -v python3
    elif command -v python >/dev/null 2>&1; then
        command -v python
    else
        fail "Python 3 is required."
    fi
}

sha256_file() {
    "${PYTHON_COMMAND}" - "$1" <<'PY'
import hashlib
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
digest = hashlib.sha256()
with path.open("rb") as stream:
    for block in iter(lambda: stream.read(1024 * 1024), b""):
        digest.update(block)
print(digest.hexdigest())
PY
}

read_release_metadata() {
    "${PYTHON_COMMAND}" - "$@" <<'PY'
import json
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
expected_tag = sys.argv[2]
repository = sys.argv[3]
required_names = sys.argv[4:]

try:
    release = json.loads(path.read_text(encoding="utf-8"))
except (OSError, json.JSONDecodeError) as error:
    raise SystemExit(f"error: invalid release metadata: {error}") from error

if release.get("tagName") != expected_tag:
    raise SystemExit("error: release tag does not match the requested tag.")
if release.get("isDraft") is not False:
    raise SystemExit("error: draft releases cannot be used for artifact profiles.")
if release.get("isPrerelease") is not False:
    raise SystemExit("error: prereleases cannot be used for artifact profiles.")

assets = release.get("assets")
if not isinstance(assets, list):
    raise SystemExit("error: release assets are missing or invalid.")

for required_name in required_names:
    matches = [asset for asset in assets if asset.get("name") == required_name]
    if len(matches) != 1:
        raise SystemExit(
            f"error: release must contain exactly one {required_name} asset."
        )
    asset = matches[0]
    if asset.get("state") != "uploaded":
        raise SystemExit(f"error: release asset is not uploaded: {required_name}")
    url = asset.get("url")
    expected_url = (
        f"https://github.com/{repository}/releases/download/"
        f"{expected_tag}/{required_name}"
    )
    if url != expected_url:
        raise SystemExit(f"error: release asset URL is invalid: {required_name}")
    print(url)
PY
}

read_product_version() {
    "${PYTHON_COMMAND}" - "$1" <<'PY'
import pathlib
import sys
import xml.etree.ElementTree as ET

path = pathlib.Path(sys.argv[1])
try:
    root = ET.parse(path).getroot()
except (OSError, ET.ParseError) as error:
    raise SystemExit(f"error: invalid tagged Directory.Build.props: {error}") from error

versions = {
    (element.text or "").strip()
    for element in root.iter()
    if element.tag.rsplit("}", 1)[-1] == "MemoriaNoteProductVersion"
    and (element.text or "").strip()
}
if len(versions) != 1:
    raise SystemExit(
        "error: tagged source must define exactly one MemoriaNoteProductVersion."
    )
print(versions.pop())
PY
}

read_checksums() {
    "${PYTHON_COMMAND}" - "$1" "$2" "$3" <<'PY'
import pathlib
import re
import sys

path = pathlib.Path(sys.argv[1])
required_names = sys.argv[2:]
expected = {}

try:
    lines = path.read_text(encoding="utf-8").splitlines()
except OSError as error:
    raise SystemExit(f"error: cannot read SHA256SUMS: {error}") from error

for line in lines:
    match = re.fullmatch(r"([0-9a-f]{64})  ([^/\\]+)", line)
    if match is None:
        raise SystemExit("error: SHA256SUMS contains an invalid entry.")
    digest, name = match.groups()
    if name in expected:
        raise SystemExit(f"error: SHA256SUMS contains a duplicate entry: {name}")
    expected[name] = digest

if set(expected) != set(required_names):
    raise SystemExit(
        "error: SHA256SUMS must contain exactly the Linux and Windows archives."
    )
for name in required_names:
    print(expected[name])
PY
}

write_profile() {
    local output_path="$1"
    local tag="$2"
    local version="$3"
    local source_commit="$4"
    local host_os="$5"
    local rid="$6"
    local executable="$7"
    local archive_format="$8"
    local archive_name="$9"
    local archive_url="${10}"
    local archive_sha256="${11}"

    "${PYTHON_COMMAND}" - \
        "${output_path}" "${tag}" "${version}" "${source_commit}" \
        "${host_os}" "${rid}" "${executable}" "${archive_format}" \
        "${archive_name}" "${archive_url}" "${archive_sha256}" <<'PY'
import json
import pathlib
import sys

(
    output_path,
    tag,
    version,
    source_commit,
    host_os,
    rid,
    executable,
    archive_format,
    archive_name,
    archive_url,
    archive_sha256,
) = sys.argv[1:]

profile = {
    "schemaVersion": "1",
    "artifactId": f"{tag}-{rid}-self-contained",
    "productVersion": version,
    "sourceCommit": source_commit,
    "hostOs": host_os,
    "rid": rid,
    "configuration": "Release",
    "deploymentKind": "self-contained",
    "artifactInputKind": "extracted-archive",
    "executableRelativePath": executable,
    "expectedVersionOutput": f"mn {version}",
    "archive": {
        "format": archive_format,
        "fileName": archive_name,
        "url": archive_url,
        "sha256": archive_sha256,
        "rootDirectory": f"mn-{tag}-{rid}",
    },
}
pathlib.Path(output_path).write_text(
    json.dumps(profile, indent=2) + "\n",
    encoding="utf-8",
    newline="\n",
)
PY
}

check_existing_file() {
    local generated="$1"
    local destination="$2"
    local description="$3"

    if [[ -e "${destination}" || -L "${destination}" ]]; then
        [[ -f "${destination}" && ! -L "${destination}" ]] || \
            fail "${description} destination is not a regular file: ${destination}"
        cmp -s -- "${generated}" "${destination}" || \
            fail "${description} already exists with different content: ${destination}"
    fi
}

install_if_missing() {
    local generated="$1"
    local destination="$2"

    if [[ -f "${destination}" ]]; then
        echo "Unchanged: ${destination#"${REPOSITORY_ROOT}/"}"
        return
    fi
    mkdir -p -- "$(dirname -- "${destination}")"
    mv -- "${generated}" "${destination}"
    echo "Created: ${destination#"${REPOSITORY_ROOT}/"}"
}

if [[ $# -ne 1 || "$1" == "-h" || "$1" == "--help" ]]; then
    usage
    [[ $# -eq 1 ]] && exit 0
    exit 2
fi

readonly TAG="$1"
[[ "${TAG}" =~ ^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]] || \
    fail "release tag must use v<major>.<minor>.<patch> with no leading zeros."
readonly VERSION="${TAG#v}"

command -v "${GH_COMMAND}" >/dev/null 2>&1 || fail "GitHub CLI is required: ${GH_COMMAND}"
PYTHON_COMMAND="$(find_python)"
readonly PYTHON_COMMAND

REPOSITORY="$("${GH_COMMAND}" repo view --json nameWithOwner --jq .nameWithOwner)"
readonly REPOSITORY
[[ "${REPOSITORY}" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || \
    fail "GitHub repository name is invalid."

readonly CACHE_DIRECTORY="${REPOSITORY_ROOT}/performance/.cache/artifacts/${TAG}/download"
readonly PROFILE_DIRECTORY="${REPOSITORY_ROOT}/performance/artifacts"
readonly TEMPORARY_PARENT="${REPOSITORY_ROOT}/performance/.cache/artifacts/${TAG}"
mkdir -p -- "${TEMPORARY_PARENT}"
TEMPORARY_DIRECTORY="$(mktemp -d "${TEMPORARY_PARENT}/generate.XXXXXX")"
readonly TEMPORARY_DIRECTORY
trap 'rm -rf -- "${TEMPORARY_DIRECTORY}"' EXIT

readonly LINUX_ARCHIVE="mn-${TAG}-linux-x64.tar.gz"
readonly WINDOWS_ARCHIVE="mn-${TAG}-win-x64.zip"
readonly CHECKSUM_FILE="SHA256SUMS"

"${GH_COMMAND}" release view "${TAG}" \
    --repo "${REPOSITORY}" \
    --json tagName,isDraft,isPrerelease,assets \
    > "${TEMPORARY_DIRECTORY}/release.json"

mapfile -t ASSET_URLS < <(
    read_release_metadata \
        "${TEMPORARY_DIRECTORY}/release.json" \
        "${TAG}" "${REPOSITORY}" \
        "${LINUX_ARCHIVE}" "${WINDOWS_ARCHIVE}" "${CHECKSUM_FILE}"
)
[[ ${#ASSET_URLS[@]} -eq 3 ]] || fail "release metadata did not produce all required asset URLs."
readonly LINUX_URL="${ASSET_URLS[0]}"
readonly WINDOWS_URL="${ASSET_URLS[1]}"

SOURCE_COMMIT="$("${GH_COMMAND}" api \
    "repos/${REPOSITORY}/commits/${TAG}" --jq .sha)"
readonly SOURCE_COMMIT
[[ "${SOURCE_COMMIT}" =~ ^[0-9a-f]{40}$ ]] || \
    fail "release tag did not resolve to a full lowercase source commit."

"${GH_COMMAND}" api \
    -H "Accept: application/vnd.github.raw+json" \
    "repos/${REPOSITORY}/contents/Directory.Build.props?ref=${TAG}" \
    > "${TEMPORARY_DIRECTORY}/Directory.Build.props"
TAGGED_PRODUCT_VERSION="$(
    read_product_version "${TEMPORARY_DIRECTORY}/Directory.Build.props"
)"
readonly TAGGED_PRODUCT_VERSION
[[ "${TAGGED_PRODUCT_VERSION}" == "${VERSION}" ]] || \
    fail "tag version ${VERSION} does not match tagged product version ${TAGGED_PRODUCT_VERSION}."

mkdir -- "${TEMPORARY_DIRECTORY}/download"
"${GH_COMMAND}" release download "${TAG}" \
    --repo "${REPOSITORY}" \
    --dir "${TEMPORARY_DIRECTORY}/download" \
    --pattern "${CHECKSUM_FILE}"

[[ -f "${TEMPORARY_DIRECTORY}/download/${CHECKSUM_FILE}" ]] || \
    fail "downloaded release asset is missing: ${CHECKSUM_FILE}"

mapfile -t EXPECTED_CHECKSUMS < <(
    read_checksums \
        "${TEMPORARY_DIRECTORY}/download/${CHECKSUM_FILE}" \
        "${LINUX_ARCHIVE}" "${WINDOWS_ARCHIVE}"
)
[[ ${#EXPECTED_CHECKSUMS[@]} -eq 2 ]] || fail "SHA256SUMS did not produce both archive checksums."
readonly LINUX_SHA256="${EXPECTED_CHECKSUMS[0]}"
readonly WINDOWS_SHA256="${EXPECTED_CHECKSUMS[1]}"
readonly LINUX_CACHE="${CACHE_DIRECTORY}/${LINUX_ARCHIVE}"
readonly WINDOWS_CACHE="${CACHE_DIRECTORY}/${WINDOWS_ARCHIVE}"
readonly CHECKSUM_CACHE="${CACHE_DIRECTORY}/${CHECKSUM_FILE}"

check_existing_file \
    "${TEMPORARY_DIRECTORY}/download/${CHECKSUM_FILE}" \
    "${CHECKSUM_CACHE}" \
    "Artifact cache entry"

DOWNLOAD_PATTERNS=()
if [[ -e "${LINUX_CACHE}" || -L "${LINUX_CACHE}" ]]; then
    [[ -f "${LINUX_CACHE}" && ! -L "${LINUX_CACHE}" ]] || \
        fail "Artifact cache entry destination is not a regular file: ${LINUX_CACHE}"
    [[ "$(sha256_file "${LINUX_CACHE}")" == "${LINUX_SHA256}" ]] || \
        fail "cached archive checksum mismatch: ${LINUX_ARCHIVE}"
else
    DOWNLOAD_PATTERNS+=(--pattern "${LINUX_ARCHIVE}")
fi
if [[ -e "${WINDOWS_CACHE}" || -L "${WINDOWS_CACHE}" ]]; then
    [[ -f "${WINDOWS_CACHE}" && ! -L "${WINDOWS_CACHE}" ]] || \
        fail "Artifact cache entry destination is not a regular file: ${WINDOWS_CACHE}"
    [[ "$(sha256_file "${WINDOWS_CACHE}")" == "${WINDOWS_SHA256}" ]] || \
        fail "cached archive checksum mismatch: ${WINDOWS_ARCHIVE}"
else
    DOWNLOAD_PATTERNS+=(--pattern "${WINDOWS_ARCHIVE}")
fi

if [[ ${#DOWNLOAD_PATTERNS[@]} -gt 0 ]]; then
    "${GH_COMMAND}" release download "${TAG}" \
        --repo "${REPOSITORY}" \
        --dir "${TEMPORARY_DIRECTORY}/download" \
        "${DOWNLOAD_PATTERNS[@]}"
fi

if [[ -f "${LINUX_CACHE}" ]]; then
    LINUX_INPUT="${LINUX_CACHE}"
else
    LINUX_INPUT="${TEMPORARY_DIRECTORY}/download/${LINUX_ARCHIVE}"
fi
readonly LINUX_INPUT
if [[ -f "${WINDOWS_CACHE}" ]]; then
    WINDOWS_INPUT="${WINDOWS_CACHE}"
else
    WINDOWS_INPUT="${TEMPORARY_DIRECTORY}/download/${WINDOWS_ARCHIVE}"
fi
readonly WINDOWS_INPUT

[[ -f "${LINUX_INPUT}" ]] || fail "downloaded release asset is missing: ${LINUX_ARCHIVE}"
[[ -f "${WINDOWS_INPUT}" ]] || fail "downloaded release asset is missing: ${WINDOWS_ARCHIVE}"
[[ "$(sha256_file "${LINUX_INPUT}")" == "${LINUX_SHA256}" ]] || \
    fail "downloaded archive checksum mismatch: ${LINUX_ARCHIVE}"
[[ "$(sha256_file "${WINDOWS_INPUT}")" == "${WINDOWS_SHA256}" ]] || \
    fail "downloaded archive checksum mismatch: ${WINDOWS_ARCHIVE}"

readonly GENERATED_LINUX_PROFILE="${TEMPORARY_DIRECTORY}/${TAG}-linux-x64.json"
readonly GENERATED_WINDOWS_PROFILE="${TEMPORARY_DIRECTORY}/${TAG}-win-x64.json"
write_profile \
    "${GENERATED_LINUX_PROFILE}" "${TAG}" "${VERSION}" "${SOURCE_COMMIT}" \
    linux linux-x64 mn tar.gz "${LINUX_ARCHIVE}" "${LINUX_URL}" "${LINUX_SHA256}"
write_profile \
    "${GENERATED_WINDOWS_PROFILE}" "${TAG}" "${VERSION}" "${SOURCE_COMMIT}" \
    windows win-x64 mn.exe zip "${WINDOWS_ARCHIVE}" "${WINDOWS_URL}" "${WINDOWS_SHA256}"

readonly LINUX_PROFILE="${PROFILE_DIRECTORY}/${TAG}-linux-x64.json"
readonly WINDOWS_PROFILE="${PROFILE_DIRECTORY}/${TAG}-win-x64.json"
check_existing_file "${GENERATED_LINUX_PROFILE}" "${LINUX_PROFILE}" "Linux artifact profile"
check_existing_file "${GENERATED_WINDOWS_PROFILE}" "${WINDOWS_PROFILE}" "Windows artifact profile"

install_if_missing \
    "${LINUX_INPUT}" \
    "${LINUX_CACHE}"
install_if_missing \
    "${WINDOWS_INPUT}" \
    "${WINDOWS_CACHE}"
install_if_missing \
    "${TEMPORARY_DIRECTORY}/download/${CHECKSUM_FILE}" \
    "${CHECKSUM_CACHE}"
install_if_missing "${GENERATED_LINUX_PROFILE}" "${LINUX_PROFILE}"
install_if_missing "${GENERATED_WINDOWS_PROFILE}" "${WINDOWS_PROFILE}"

echo "Generated artifact profiles for ${TAG} (${SOURCE_COMMIT})."
