#!/usr/bin/env bash
# End-to-end determinism smoke: the server (wasm) must produce the exact state hash the native and Unity
# simulations do. Uses a manual match so it is stepped explicitly, then closes it.
#   scripts/smoke.sh [local|maincloud] [database]
set -euo pipefail
SERVER=${1:-local}; DB=${2:-$([ "$SERVER" = maincloud ] && echo argmax-deadnet || echo argmax)}
GOLDEN=4987065449793598049   # Sim.NewDuel(42, Human, Ai, 0, TURTLE) after 300 ticks (see tests/GoldenTests)
spacetime call -s "$SERVER" "$DB" create_match true true 42 101 >/dev/null
MID=$(spacetime sql -s "$SERVER" "$DB" "SELECT id FROM match WHERE manual = true AND state = 1" 2>/dev/null | tail -1 | tr -d ' ')
spacetime call -s "$SERVER" "$DB" step_match "$MID" 300 >/dev/null
HASH=$(spacetime sql -s "$SERVER" "$DB" "SELECT state_hash FROM match WHERE id = $MID" 2>/dev/null | tail -1 | tr -d ' ')
spacetime call -s "$SERVER" "$DB" leave "$MID" >/dev/null || true
if [ "$HASH" = "$GOLDEN" ]; then echo "PASS  $SERVER/$DB match $MID hash $HASH == native golden"; else echo "FAIL  $SERVER/$DB hash $HASH != $GOLDEN"; exit 1; fi
