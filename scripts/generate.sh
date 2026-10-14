#!/usr/bin/env bash
# Regenerate the Unity client bindings from the module (run after changing tables or reducers).
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"
spacetime generate --lang csharp --out-dir client/Assets/Argmax/Net/Bindings --module-path server
