#!/bin/sh
# Publishes the Forgejo release for <tag>: the release notes package.sh wrote
# as the body, and the zips in <dist dir> attached. manifest.json already points
# at these attachments, so Jellyfin can install the version once this is done.
# Also runs locally to retry a release that failed after the tag was pushed.
#
# Usage: TOKEN=... publish-release.sh <tag> <dist dir>
set -eu

tag="${1:?usage: $0 <tag> <dist dir>}"
dist="${2:?missing dist dir}"
TOKEN="${TOKEN:-}"
api="${GITHUB_SERVER_URL:-https://code.hosaka.cc}/api/v1/repos/${GITHUB_REPOSITORY:-hosaka/jellyfin-plugin-timeisup}"

if [ -z "$TOKEN" ]; then
  echo "No token to publish $tag with" >&2
  exit 1
fi

call() {
  curl -sS --fail-with-body --header "Authorization: token $TOKEN" "$@"
}

payload=$(jq -n --arg tag "$tag" --arg name "$(yq '.name' build.yaml) $tag" --rawfile body "$dist/release-notes.md" \
  '{tag_name: $tag, name: $name, body: $body}')

# Not piped straight into jq: the pipeline's status would be jq's, so a failed
# call would carry on with id "null". The forge's error body is printed instead.
release=$(call --request POST \
  --header "Content-Type: application/json" \
  --data "$payload" \
  "$api/releases") || {
  echo "$release" >&2
  exit 1
}
id=$(printf '%s' "$release" | jq -r .id)
echo "Created release $id for $tag"

for zip in "$dist"/*.zip; do
  asset=$(basename "$zip")
  upload=$(call --request POST \
    --form "attachment=@$zip" \
    "$api/releases/$id/assets?name=$asset") || {
    echo "$upload" >&2
    exit 1
  }
  echo "Attached $asset"
done
