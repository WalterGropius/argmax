#!/usr/bin/env bash
# Python side of ML-Agents 4.x (release_23). Needs uv (https://docs.astral.sh/uv/). Python must be <= 3.10.12.
set -euo pipefail
cd "$(dirname "$0")"
uv venv -p 3.10.12 .venv
uv pip install --python .venv/bin/python --only-binary grpcio "grpcio==1.53.2"
uv pip install --python .venv/bin/python \
  "mlagents-envs @ git+https://github.com/Unity-Technologies/ml-agents.git@release_23#subdirectory=ml-agents-envs" \
  "mlagents @ git+https://github.com/Unity-Technologies/ml-agents.git@release_23#subdirectory=ml-agents"
# release_23 still imports pkg_resources; newest torch is untested with it
uv pip install --python .venv/bin/python "setuptools<70" "torch==2.8.*"
.venv/bin/mlagents-learn --help | head -1
