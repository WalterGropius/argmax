#!/usr/bin/env bash
# Headless ML-Agents self-play: trainer + Unity editor gym, no window. Exports ml/results/<run>/Commander.onnx
# and copies it into the client so the ML pilot uses it.
#   scripts/train.sh [run-id] [minutes] [arenas]
set -euo pipefail
cd "$(dirname "$0")/.."
RUN=${1:-argmax-v1}; MIN=${2:-10}; ARENAS=${3:-8}
UNITY=${UNITY:-/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity}
[ -x ml/.venv/bin/mlagents-learn ] || { echo "run ml/setup.sh first"; exit 1; }
mkdir -p logs
ml/.venv/bin/mlagents-learn ml/config/commander.yaml --run-id="$RUN" --results-dir=ml/results --force > "logs/train-$RUN.log" 2>&1 &
TRAINER=$!
sleep 8
ARGMAX_TRAIN_MINUTES=$MIN ARGMAX_ARENAS=$ARENAS "$UNITY" -batchmode -nographics -projectPath "$PWD/client" \
  -executeMethod Argmax.EditorTools.ArgmaxTools.Train -logFile "logs/unity-train-$RUN.log" || true
# in editor mode the trainer waits for a new connection after Unity exits; Ctrl-C makes it export the model
kill -INT $TRAINER 2>/dev/null || true
wait $TRAINER || true
ONNX="ml/results/$RUN/Commander.onnx"
if [ -f "$ONNX" ]; then
  cp "$ONNX" client/Assets/Argmax/Resources/Argmax/Brains/Commander.onnx
  echo "model -> client/Assets/Argmax/Resources/Argmax/Brains/Commander.onnx"
else
  echo "no model exported; see logs/train-$RUN.log"
fi
