#!/usr/bin/env bash
# Local SpacetimeDB for sessions and tests: start (isolated data dir), build, publish, regenerate bindings.
#   scripts/local.sh            start server if needed + publish (keeps data)
#   scripts/local.sh --reset    wipe the database first
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"
DB=${ARGMAX_DB:-argmax}
if ! curl -s -m 1 http://127.0.0.1:3000/v1/ping >/dev/null; then
  mkdir -p .stdb-local logs
  echo "starting local SpacetimeDB on 127.0.0.1:3000 (data: .stdb-local)"
  nohup spacetime start --listen-addr 127.0.0.1:3000 --data-dir "$PWD/.stdb-local" --non-interactive > logs/stdb-local.log 2>&1 &
  for i in $(seq 1 30); do curl -s -m 1 http://127.0.0.1:3000/v1/ping >/dev/null && break; sleep 0.5; done
fi
FLAGS=(--server local --module-path server --yes)
[ "${1:-}" = "--reset" ] && FLAGS+=(--delete-data=always)
spacetime publish "$DB" "${FLAGS[@]}"
scripts/generate.sh
echo "local: http://127.0.0.1:3000  database: $DB"
