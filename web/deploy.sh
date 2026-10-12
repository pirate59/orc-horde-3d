#!/bin/sh
# Builds the browser version and deploys it, with the save API, to Cloudflare.
#   web/deploy.sh            build, migrate the database, deploy
#   web/deploy.sh --no-build deploy the build already in Builds/Web
#
# First time only (see README, "Browser build and Cloudflare"):
#   cd web/worker && npm install && npx wrangler login
#   npx wrangler d1 create orc-horde-saves   # then put its database_id in wrangler.jsonc
set -e
ROOT=$(cd "$(dirname "$0")/.." && pwd)
if [ "$1" != "--no-build" ]; then
  UNITY=${UNITY:-$(ls -d /Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity | head -1)}
  "$UNITY" -batchmode -quit -projectPath "$ROOT" -executeMethod ProjectSetup.BuildWeb -logFile "$ROOT/Logs/buildweb.log"
  grep "Web build" "$ROOT/Logs/buildweb.log"
  echo "Built into web/worker/public: commit it so Cloudflare's own builds deploy the same files."
fi
cd "$ROOT/web/worker"
npx wrangler d1 migrations apply orc-horde-saves --remote
npx wrangler deploy
