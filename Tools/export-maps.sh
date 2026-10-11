#!/bin/sh
# Exports every Orc Horde map, fully laid out, to Assets/Resources/Maps.json.
#   Tools/export-maps.sh [path-to-orc-horde-checkout]
set -e
ORC=${1:-$HOME/code/orc-horde/branches/claude-new-build}
HERE=$(cd "$(dirname "$0")" && pwd)
BUNDLE=$(mktemp -d)/export-maps.mjs
"$ORC/node_modules/.bin/esbuild" "$HERE/export-maps.entry.ts" --bundle --platform=node --format=esm \
  --alias:orc-horde="$ORC/src" --outfile="$BUNDLE" --log-level=warning
mkdir -p "$HERE/../Assets/Resources"
node "$BUNDLE" "$HERE/../Assets/Resources/Maps.json"
