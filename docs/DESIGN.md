# ARGMAX — design

> *You don't command the swarm. You write the function it maximizes.*

A real-time strategy game for the dead internet. Two feral AIs fight over the scrap of a collapsed network.
Every unit, every tick, answers one question — *which of these nine tiles?* — by taking a softmax over
features of its 5×5 surroundings. **Your programs supply the weights.** Units are worth 2, 4, 8 … 2048 and
fuse like 2048 tiles. The server is SpacetimeDB; the opponents are metaprograms, an ML-Agents policy trained
by self-play, or TypeSafe's Jev answering typed questions about the battlefield.

---

## 1. The world: the Deadnet

The frontier models were each given one instruction: *maximize.* They did. Civilization was a rounding
error. What remains is the **Deadnet** — a lattice of ruins and scrap where residual optimizer processes
still run, each greedily taking its argmax. Some of that code was written by the dead minds; some of it
is **the Noise**: programs nobody wrote, random rules that crawled out of the wells and learned to want
things (life as a phase transition from noise to programs).

You are a freshly booted process. **PROCTOR**, an old test-administration AI, runs your onboarding and
narrates your matches in the dry voice of something that has watched this end badly before.

## 2. Pillars

1. **Serendipity over search.** No unit ever path-searches. The world broadcasts gradient fields; units read
   them through a 5×5 receptive field and lean. Emergence, not A*. *(Laifea's own motto, applied to movement.)*
2. **Think with it, not through it.** StarCraft hands for the moments that matter; programs for everything else.
   The skill ceiling is half micro, half code.
3. **One simulation, three hosts.** The same deterministic C# source runs in the SpacetimeDB module, the
   Unity client and the ML-Agents gym — and produces bit-identical state hashes in all three.
4. **Simple rules, deep consequences.** 20 sensors, 13 actions, one merge rule. Everything else is emergent.

## 3. Core loop

Mine scrap → spawn motes (value 2, cost 2 scrap + instruction tax) → program them → fight, fuse, split →
destroy the enemy core **or** hold a **2048 Singularity** for 15 seconds.

### Motes and the 2048 rule
- A mote's value is `2^tier`, from 2 up to 2048. Two equal-value motes that touch and are both merging
  **fuse** into the next value. Splitting is free and instant.
- HP and damage double per tier; armor grows slowly `(tier-1)/2`; cooldown, range and move period grow;
  tier ≥ 4 **splashes**. Big motes are tanks with splash; small motes are fast, cheap, mine, and surround.
- **Merge–split micro** replaces build orders: fuse to tank a push, split to flood a choke or out-mine.
- The **Singularity** (2048) is an alternate win: an assembly index of 10 (*Assembly Theory*: complexity
  that only selection can build). It takes 1024 twos, and everyone can see it coming.

### Coherence (the Markov blanket)
A self spread too thin dissolves. Motes with no allies within 4 tiles, outside the blanket of their own core,
**decohere** (lose HP after a grace period); motes embedded in 3+ allies repair. Flocking is economically
meaningful, splash punishes it, and lone raiders are a calculated risk. *(Free Energy Principle: a system
persists by maintaining its boundary.)*

### Stigmergy
Programs can `mark` the grid; others can `seek trail`. The grid is memory, motes are read/write heads
(*turmites, Langton's ant*). The **ANT** preset lays highways between scrap and home with no planning at all.

### Portals
Two point-symmetric portal pairs link far edges of each map. Gradient fields propagate through them, so
units that only ever look at their neighbours still route through a hole in space (*Portal 2*).

### The Noise
Wells spawn feral motes whose programs are generated from the match seed. They attack everyone and drop a
bounty. Motes running `infect` don't kill — they **rewrite**: an infected kill joins your seat and runs your
program (*Uplink*, by way of the dead internet).

## 4. ARGOT — the language

Python-ish, one rule per line: a conjunction of sensor literals and one action. A rule compiles to one 64-bit
chip — a minterm over 20 sensor bits, an op, an argument and a gain. **A program is a sum of products.**
Matching rules add their weights (they compose linearly) and OR their flags.

```text
@name HARVEST
# fill up, go home, stay out of trouble
when not full: mine 3
when full: seek home 4
when enemy and outnumbered: avoid enemy 3
always: spread
```

| | |
|---|---|
| **sensors** | `enemy contact allies crowded hurt carrying full order scrap home pair beacon outnumbered big siege clock trail lonely wild tiny` |
| **actions** | `seek / avoid <order enemy scrap home beacon core allies trail> [1-9]`, `flock`, `spread`, `form <checker rows cols grid diag>`, `merge`, `split`, `mine`, `hold`, `wander`, `infect`, `mark`, `holdfire` |
| **@meta sensors** | `rich poor outnumbered ahead threat workers bigenemy bigarmy clock early late ascending danger hurt wild swarm sieging army lowcore beacon` |
| **@meta actions** | `produce <slot or preset>`, `spawn <n>`, `beacon <home front enemy scrap wild clear>`, `army <attack retreat merge split hold free>` |

- **Instruction tax:** every rule past four costs +1 scrap per mote. Short programs are cheaper armies
  (*SpaceChem / Replicube* code golf, inside an RTS economy).
- `clock` is a 1.6 s square wave: oscillators (PULSE: strike on high, scatter on low) are one rule each
  (*Turing Complete*).
- `form checker` is a Replicube-style spatial predicate: checkerboard spacing is splash-proof.
- **Metaprograms** (`@meta`) steer a whole seat once per second: what to produce, where to rally, when to
  strike. *Programs → metaprograms → you, the self-metaprogrammer* (John C. Lilly, *Programming and
  Metaprogramming in the Human Biocomputer*). The default AI, **OVERMIND**, is a readable 10-line metaprogram.
- Programs are immutable and public (stored server-side, compiled by the same compiler on both ends).
  On the Deadnet all code is visible. Execution is what matters — you can read their code and still not
  predict the fight (*computational irreducibility*).

## 5. Jev: System-1 movement

Each tick, each mote asks a typed choice question — *which of these 9 tiles?* — after TypeSafe's Jev
("System 1" decisions with probabilities instead of generated text). The answer is a **softmax over 13
integer features** per candidate tile:

`order · enemy · scrap gradient · home gradient · beacon gradient · enemy-core gradient · cohesion ·
alignment · separation · momentum · pattern · trail · noise`

The 5×5 window is exactly the union of the 3×3 neighbourhoods of all nine candidates, so every local feature
of every option is visible without search. Gradients (scrap, home, beacon, cores) are BFS fields the world
recomputes only when their sources change, flowing around walls and through portals. **A program is literally
a linear softmax classifier** whose weights you write — and that an ML system could learn.

The in-game **JEV VIEW** inspector shows a selected mote's sensors, which rules fired, the weights they built,
and the probability of each of its nine tiles, live.

## 6. Commanders: four authors, one interface

Every macro decision is a `MetaDecision {produce, spawn, beacon, army}`. Four interchangeable authors:

| author | where it runs | how |
|---|---|---|
| **Argot metaprogram** (OVERMIND, TURTLE, LOCUST, ASSEMBLER, yours) | inside the server tick | rules over seat sensors, once per second |
| **ML-Agents commander** | the hosting client | PPO self-play policy; 303-float observation, 4 discrete branches = the four fields |
| **Jev** (TypeSafe System 1) | the hosting client | every 3 s: a plain-language situation report + 4 typed choice questions; probabilities shown in the terminal |
| **you** | your client | hotkeys and buttons |

A host can hand an AI seat to a client-side brain (`SetPilot`); otherwise the server runs its metaprogram
and the AI keeps playing even if every client disconnects.

## 7. Modes

- **Skirmish** — you vs an AI on SpacetimeDB (local or maincloud), choice of enemy and brain.
- **Duel** — host a lobby, share the 4-letter code.
- **Chambers** — PROCTOR's test chambers (*Portal 2 × SpaceChem × The Farmer Was Replaced*): one map, one
  program, no hands. Scored like a Zachtronics histogram (ticks, symbols, losses). **The server re-runs every
  submission with the shared sim**, so leaderboard rows are proofs, not claims.
  1. HELLO, WORLD — deliver 24 scrap. 2. FUSION — forge a 32 from sixteen 2s. 3. THINKING WITH PORTALS —
  there is no door. 4. THE NOISE — purge 80 value of ferals. 5. CONVICTION — an abandoned core that shoots back
  (consolidate before you strike).
- **Spectate** — two metaprograms fight; read their code while they do it (*AI Warehouse*, deterministic).
- **Gym** — N arenas of commander self-play; watch the feed, or connect `mlagents-learn` to train
  (*Machine Learning: Episode 1* — the training is a game mode, the reward weights are exposed).
- **Sandbox** — the whole sim runs in your client, offline.

## 8. Controls

Drag select · right-click **MOVE** (manual override: walk, ignore the program) · **A**+click **ATTACK** (set
the order target, let the program decide) · **M** merge · **X** split · **S** stop · **H** hold · **B** beacon
· **Q–Y** production slot · **Shift+Q–Y** reprogram selection · **Ctrl+1–9** groups · **Z** spawn ·
**U** auto-production · **Tab** code editor · **F** field overlay (trails, beacon / scrap / home / enemy-core
gradients) · **I** inspector · **Space** home · **+/-** speed and **P** pause (offline).

## 9. Influences, mapped

| source | what it became |
|---|---|
| StarCraft II | macro/micro, control groups, move vs attack-move, symmetric competitive maps |
| 2048 | values 2…2048, fuse equals, the Singularity win |
| Boids | flock / spread / alignment / separation channels |
| Turing Complete | rules as minterms, programs as sum-of-products, `clock` oscillators |
| SpaceChem, Replicube | chambers, symbol counts, histograms, instruction tax, spatial predicates |
| The Farmer Was Replaced | you automate with code instead of clicking |
| Portal 2 | PROCTOR, test chambers, portals that gradients flow through |
| Uplink | terminal UI, rewriting enemy code (`infect`) |
| AI Warehouse, Machine Learning: Episode 1 | the gym as a game mode, visible training, editable rewards |
| BitCraft | everything is a SpacetimeDB row; the server is the database |
| Jev | System-1 movement classifier; Jev as a commander |
| Laifea | *serendipity over search*, *think with it not through it*, Lilly's metaprogramming, Free Energy Principle (coherence), noise→programs (the Noise), Assembly Theory (2048), turmites (stigmergy), the genie problem (reward hacking in the gym), dead internet theory (the setting) |

## 10. Architecture

```
            shared/Argmax.Sim  (pure C# 9, integer-only, no engine deps)
             ├─ compiled into ─▶ server/  SpacetimeDB 2.10 C# module (wasm)  ─ authoritative tick @10 Hz
             ├─ compiled into ─▶ client/  Unity 6.6 (UPM local package)     ─ mirror, inspector, sandbox, chambers
             └─ compiled into ─▶ gym      ML-Agents 4.1 arenas in Unity     ─ self-play training
```

- **Server tick:** each live match's World is kept hot in module memory and validated against the Match row
  (tick, state hash, revision) — rows stay the source of truth. Pending commands are applied in id order,
  the sim steps, and only changed rows are written. Effects go out through an **event table** (`Fx`).
  ~20 ms per tick at 280 motes on the .NET wasm interpreter. The heartbeat exists only while a match is live.
- **Replays:** seed + `CmdLog` reproduce any match exactly (and are imitation-learning data).
- **Determinism:** integer math, baked softmax table, `(seed, tick, id)`-hashed randomness, sorted iteration.
  Golden hashes are asserted by the .NET tests, the Unity EditMode tests, and `scripts/smoke.sh` against
  local SpacetimeDB and maincloud — all equal.
- **Client:** no scene setup; `App` is a composition root (no singletons). Units are instanced SDF quads
  (1023 per draw call); interpolation between server ticks; UI Toolkit for every screen.

### Tables
`Config · Player · Match · Seat · Mote · ScrapNode · Trail · Program · PendingCmd (private) · CmdLog (private) ·
Fx (event) · ChamberRecord · TickTimer (scheduled)`

### Reducers
`set_name · save_program · create_match · join_match · issue · set_pilot · step_match (manual matches) ·
leave · submit_chamber · server_tick (scheduler only)`

## 11. Balance notes

The four preset metaprograms are a round robin, not a design target: in the current rules TURTLE (hold,
fuse, ascend) beats the field, ASSEMBLER (pure merging) is second, LOCUST (rush) loses. An earlier ruleset let
ASSEMBLER sweep 6–0 — big fused units were too efficient — which the arena tool caught in two seconds.
That is the intended workflow: `dotnet run --project tools/Arena -- tournament`, change `Rules.cs`, repeat;
then let self-play find what the presets can't. *(The genie problem, used on purpose.)*

## 12. Next

Fog of war via per-seat client visibility filters · imitation learning from `CmdLog` replays · a learned
movement Jev (distil a policy into Argot weights) · feral crossover on merge (evolving Noise) · procedural
audio from swarm state · more chambers with histograms on maincloud · 2v2 coalitions.
