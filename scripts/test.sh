#!/usr/bin/env bash
# Everything that can be checked headlessly: sim unit tests, Unity EditMode tests (same golden hashes),
# and the server determinism smoke against local SpacetimeDB.
set -euo pipefail
cd "$(dirname "$0")/.."
export PATH="$HOME/.dotnet:$PATH"
UNITY=${UNITY:-/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity}
mkdir -p logs
echo "== sim (dotnet, C# 9)"; dotnet test tests/Argmax.Sim.Tests --filter "Category!=Slow"
echo "== unity editmode"; "$UNITY" -batchmode -nographics -projectPath "$PWD/client" -runTests -testPlatform EditMode -testResults "$PWD/logs/editmode-results.xml" -logFile "$PWD/logs/unity-tests.log" && echo "unity: pass" || { echo "unity: fail (logs/unity-tests.log)"; exit 1; }
echo "== server determinism (local stdb)"; scripts/local.sh >/dev/null && scripts/smoke.sh local
