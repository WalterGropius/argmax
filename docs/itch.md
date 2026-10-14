# itch.io page kit

Use this to mirror releases on itch.io. GitHub Releases stays the primary home. To push builds, create
the project page on itch.io, then run `ITCH_TARGET=<you>/argmax scripts/itch.sh <version>` (needs
`butler login` once).

**Title:** ARGMAX

**Short description (tagline):** A swarm-programming RTS for the dead internet. You don't command the
swarm. You write the function it maximizes.

**Classification:** Game · Strategy / Puzzle · Released · Free (name your own price) · MIT source.

**Tags:** programming, real-time-strategy, puzzle, swarm, machine-learning, zachtronics-like,
educational, cyberpunk, open-source, singleplayer

**Kind of project:** Downloadable + HTML (mark the `html5` channel as "playable in browser", viewport
1600×900, fullscreen button on).

**Body:**

> The internet died. What's left are swarms of motes and the programs that steer them.
>
> **BOOT SEQUENCE** is a 28-test campaign that teaches you to program a swarm, one word at a time.
> You start with a mouse, write your first rule (`always: seek beacon 3`), and work up through
> conditionals, state machines, gradient fields, binary arithmetic, scheduling, stigmergy,
> metaprograms, genetic algorithms and reward hacking, until you beat OVERMIND without touching
> anything. Every test runs on the same deterministic simulation as a ranked match.
>
> - Units fuse like 2048 tiles: 2 + 2 = 4, up to a 2048 Singularity.
> - No pathfinding: each unit takes a softmax over 9 tiles, and your program supplies the weights.
> - An editor that only accepts the words you've been taught, with a concept card after every test.
>   PROCTOR, the test-administration AI, has opinions.
> - Evolve programs with a genetic algorithm, then learn why "lose nothing" is a terrible wish.
> - Skirmish, duels and verified leaderboards on a SpacetimeDB server you host yourself.
>
> Free and open source (MIT): https://github.com/WalterGropius/argmax

**Screenshots:** `docs/screens/campaign-*.png`, `docs/screens/battle-1.jpg`, `docs/screens/fields-2.jpg`.
**Cover image (630×500):** crop from `docs/media/poster.jpg`.
