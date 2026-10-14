#!/usr/bin/env bash
# Publish to maincloud (needs `spacetime login`). Database names are global; default argmax-deadnet.
#   scripts/publish-maincloud.sh [database-name]
# Never pass --delete-data here unless you mean to wipe live players' data.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"
DB=${1:-${ARGMAX_MAINCLOUD_DB:-argmax-deadnet}}
spacetime publish "$DB" --server maincloud --module-path server --yes
echo "set the in-game db field (server: MAINCLOUD) to: $DB"
