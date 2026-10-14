# Self-hosting ARGMAX

ARGMAX doesn't depend on any central server. The campaign, the chambers, the sandbox and AI-vs-AI all run
inside the game. Online play (skirmish against a server AI, two-player duels, verified chamber records)
needs a SpacetimeDB database, and you host it yourself. Release builds connect only to `localhost` or an
address you type in.

## 1. Run SpacetimeDB

Install the CLI (version 2.10 or newer):

```bash
curl -sSf https://install.spacetimedb.com | sh
```

Start a server on your machine (or on a VPS, so friends can reach it):

```bash
spacetime start --listen-addr 0.0.0.0:3000 --data-dir ./argmax-data
```

## 2. Publish the game module

Each release ships the compiled module as `argmax-server-<version>.wasm`. Publish it:

```bash
spacetime publish argmax --bin-path argmax-server-0.3.0.wasm --server http://127.0.0.1:3000
```

Or build it from source (.NET 8 SDK with the `wasi-experimental` workload):

```bash
spacetime publish argmax --module-path server --server http://127.0.0.1:3000
```

## 3. Connect

In the game's main menu, under **CONNECTION**:

- **server:** `LOCAL 127.0.0.1:3000` if the server runs on the same machine, or `CUSTOM SERVER` plus its
  address (for example `http://203.0.113.7:3000`).
- **db:** `argmax`, or whatever name you published under.

Then use **SKIRMISH**, or **DUEL**: one player hosts and shares the four-letter code, the other joins with it.

## Notes

- The simulation is deterministic and the server is authoritative. Chamber and campaign records are verified
  by re-running the submitted program on the server, so a leaderboard entry is a proof.
- The server ticks at 10 Hz, and only while a match is live.
- To serve over the internet, put the server behind TLS (for example Caddy or nginx with `https://` and
  `wss://`) and use the `https://` address in the game.
- Optional: install `wasm-opt` (binaryen) before `spacetime build` for a faster module.
