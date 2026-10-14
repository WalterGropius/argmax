#!/usr/bin/env bash
# Package a release from builds/ (made by ArgmaxBuild.CI): zips per platform, the server module for
# self-hosting, and SHA256SUMS, all in dist/. Refuses to package anything that looks like a secret.
#   scripts/release.sh 0.3.0            package only
#   scripts/release.sh 0.3.0 --build    run the Unity builds first
set -euo pipefail
cd "$(dirname "$0")/.."
VER=${1:?usage: scripts/release.sh <version> [--build]}
UNITY=${UNITY:-/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity}
export PATH="$HOME/.dotnet:$PATH"

if [ "${2:-}" = "--build" ]; then
  mkdir -p logs
  ARGMAX_VERSION="$VER" "$UNITY" -batchmode -quit -projectPath "$PWD/client" \
    -executeMethod Argmax.EditorTools.ArgmaxBuild.CI -logFile "$PWD/logs/build-release.log"
fi

echo "== secret scan"
# key shapes we use or have seen: TypeSafe (apikey_), GitHub (gho_/ghp_/github_pat_), generic sk- keys
PATTERN='apikey_[A-Za-z0-9]{8,}|gh[opsu]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9]{20,}'
if grep -rEIl --exclude-dir=Library --exclude-dir=.git --exclude=.env.local "$PATTERN" builds client/Assets shared server docs README.md 2>/dev/null; then
  echo "refusing to package: the files above contain something that looks like a secret" >&2
  exit 1
fi
echo "clean"

echo "== server module"
spacetime build --module-path server >/dev/null
rm -rf dist && mkdir -p dist
cp server/bin/Release/net8.0/wasi-wasm/AppBundle/StdbModule.wasm "dist/argmax-server-$VER.wasm"

echo "== players"
[ -d builds/mac/ARGMAX.app ] && (cd builds/mac && codesign --force --deep -s - ARGMAX.app >/dev/null 2>&1 || true; ditto -c -k --keepParent ARGMAX.app "../../dist/ARGMAX-$VER-macos-universal.zip")
[ -d builds/windows ] && (cd builds && rm -rf windows/ARGMAX_BurstDebugInformation_DoNotShip && ditto -c -k --keepParent windows "../dist/ARGMAX-$VER-windows-x64.zip")
[ -d builds/linux ] && (cd builds && rm -rf linux/ARGMAX_BurstDebugInformation_DoNotShip && tar -czf "../dist/ARGMAX-$VER-linux-x64.tar.gz" linux)
[ -d builds/webgl ] && (cd builds && ditto -c -k --keepParent webgl "../dist/ARGMAX-$VER-webgl.zip")

(cd dist && shasum -a 256 * > SHA256SUMS)
ls -lh dist
echo "next: gh release create v$VER dist/* --title 'ARGMAX $VER' --notes-file docs/RELEASE.md"
