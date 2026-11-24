#!/bin/sh
# Packages the plugin built for <tag> the way Jellyfin installs it, and adds
# that version to manifest.json, the plugin repository Jellyfin reads.
#
# Writes to <out dir>:
#   <slug>_<version>.zip  the files listed under artifacts in build.yaml, plus
#                         a meta.json describing the plugin
#   release-notes.md      .changes/<tag>.md without its heading: the release
#                         body and the changelog Jellyfin shows
# and adds an entry to manifest.json that points at the zip's release download
# URL, with the zip's MD5 that Jellyfin checks before installing it.
#
# Usage: package.sh <tag> <publish dir> <out dir>
set -eu

tag="${1:?usage: $0 <tag> <publish dir> <out dir>}"
publish="${2:?missing publish dir}"
out="${3:?missing out dir}"
server="${GITHUB_SERVER_URL:-https://code.hosaka.cc}"
repo="${GITHUB_REPOSITORY:-hosaka/jellyfin-plugin-timeisup}"

version="${tag#v}.0"
notes=".changes/$tag.md"
if [ ! -f "$notes" ]; then
  echo "No release notes $notes for $tag" >&2
  exit 1
fi

plugin=$(yq -o=json '.' build.yaml)
built=$(printf '%s' "$plugin" | jq -r .version)
if [ "$built" != "$version" ]; then
  echo "build.yaml says $built, but $tag needs $version" >&2
  exit 1
fi

slug=$(printf '%s' "$plugin" | jq -r '.name | ascii_downcase | gsub(" "; "-")')
mkdir -p "$out"
zip="$out/${slug}_$version.zip"
url="$server/$repo/releases/download/$tag/$(basename "$zip")"
timestamp=$(date -u +%Y-%m-%dT%H:%M:%SZ)

sed '1{/^## /d}' "$notes" > "$out/release-notes.md"
changelog=$(cat "$out/release-notes.md")

stage=$(mktemp -d)
for file in $(printf '%s' "$plugin" | jq -r '.artifacts[]'); do
  cp "$publish/$file" "$stage/"
done
printf '%s' "$plugin" | jq --arg changelog "$changelog" --arg timestamp "$timestamp" \
  '{category, changelog: $changelog, description, guid, name, overview, owner, targetAbi, framework, timestamp: $timestamp, version}' \
  > "$stage/meta.json"
rm -f "$zip"
python3 -m zipfile -c "$zip" "$stage"/*
checksum=$(md5sum "$zip" | cut -d' ' -f1)

entry=$(printf '%s' "$plugin" | jq --arg changelog "$changelog" --arg url "$url" --arg checksum "$checksum" --arg timestamp "$timestamp" \
  '{version, changelog: $changelog, targetAbi, sourceUrl: $url, checksum: $checksum, timestamp: $timestamp}')

# Newest version first. Re-packaging a version replaces its entry, and the
# plugin's own fields always come from build.yaml.
[ -f manifest.json ] || echo '[]' > manifest.json
jq --argjson plugin "$plugin" --argjson entry "$entry" '
  ($plugin | {guid, name, description, overview, owner, category}) as $info
  | (map(select(.guid == $info.guid)) | first // {versions: []}) as $current
  | [$info + {versions: ([$entry] + ($current.versions | map(select(.version != $entry.version))))}]
    + map(select(.guid != $info.guid))
' manifest.json > manifest.json.tmp
mv manifest.json.tmp manifest.json

echo "Packaged $zip (md5 $checksum) for $url"
