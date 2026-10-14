#!/usr/bin/env bash
# Optional: mirror a release on itch.io with butler (https://itch.io/docs/butler/).
# You need an itch.io account and a project page first (text for it: docs/itch.md), then either
# `butler login` once, or BUTLER_API_KEY in your environment. Nothing here stores a key.
#   ITCH_TARGET=yourname/argmax scripts/itch.sh 0.3.0
set -euo pipefail
cd "$(dirname "$0")/.."
VER=${1:?usage: ITCH_TARGET=user/game scripts/itch.sh <version>}
TARGET=${ITCH_TARGET:?set ITCH_TARGET=yourname/argmax}
command -v butler >/dev/null || { echo "install butler: https://itch.io/docs/butler/installing.html" >&2; exit 1; }
push() { [ -e "$1" ] && butler push "$1" "$TARGET:$2" --userversion "$VER"; }
push builds/mac/ARGMAX.app mac-universal
push builds/windows windows-x64
push builds/linux linux-x64
push builds/webgl html5
butler status "$TARGET"
