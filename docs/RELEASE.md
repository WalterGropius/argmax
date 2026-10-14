## ARGMAX 0.3.0: BOOT SEQUENCE

The first playable release. **[Play in your browser](https://waltergropius.github.io/argmax/play/)** or download
for your system below.

### New: the campaign

- **28 tests in six chapters** (HANDS → RULES → FIELDS → NUMBERS → TIME → MINDS) that teach the game from a
  mouse and three dots to beating OVERMIND without touching anything.
- **You earn every word of Argot.** The editor accepts only words you've been taught, says where a locked
  word is learned, and inserts words when you click them.
- **A concept card per test:** finite state machines, BFS and multi-source BFS, greedy vs global, binary
  carries and popcount, round-robin scheduling, the Strategy and Command patterns, stigmergy, genetic
  algorithms, reward hacking. Many cards add an *interview echo*: the LeetCode problems that are the same idea.
- **An evolution lab:** set the reward, watch sixteen programs per generation compete, and keep the winner. Then
  test 6-3 shows you what a bad reward does.
- PROCTOR's narration, stars (pass, par time, par symbols or no losses), components you keep (FORAGER), and
  an epilogue that plots your learning curve.
- Every level is verified by the test suite: its reference solution passes and its starter code fails.

### New: MORPHOGENESIS, the look

- **MORPH mode** (default, key `G`): the world is raymarched signed distance fields. Allies smooth-union into
  organic matter (cobalt spheres, vermilion rounded cubes, chrome octahedra), so a 2048 fusion is two blobs
  melting together. Walls are rounded stone extruded from a distance field, and the grid is implicit.
- **Turing patterns on the floor:** a Gray-Scott reaction-diffusion seeded by the swarms, with spots, labyrinths
  and worms depending on whose territory it is.
- **Fields as water:** `F` shows the gradient a program follows as ripples flowing toward the source.
- **GRID mode:** the same world as flat Bauhaus shapes with cut-paper shadows and Kandinsky rings.
- **A designed interface:** warm ink and bone with the three primaries, square geometry, a constructed lowercase
  logotype, and type by role: Michroma, Jost, Comfortaa and Space Mono (SIL OFL), plus Futura or Alte Haas
  Grotesk if installed.
- A print finish (soft highlight shoulder, risograph misregistration, paper grain), geometric confetti, camera shake.
- All sounds and the ambient score are synthesized in code, with no audio assets.

### Self-hosted

- Release builds connect only to `localhost` or a server you type in. To host one, see
  [docs/SELFHOST.md](https://github.com/WalterGropius/argmax/blob/main/docs/SELFHOST.md) and use
  `argmax-server-0.3.0.wasm`.
- MIT licensed.

### Downloads

| file | for |
|---|---|
| `ARGMAX-0.3.0-macos-universal.zip` | macOS 11+, Apple Silicon and Intel. Right-click → Open the first time (not notarized). |
| `ARGMAX-0.3.0-windows-x64.zip` | Windows 10/11, 64-bit |
| `ARGMAX-0.3.0-linux-x64.tar.gz` | Linux x86-64 |
| `ARGMAX-0.3.0-webgl.zip` | the browser build, to host anywhere static |
| `argmax-server-0.3.0.wasm` | the SpacetimeDB module for your own server |
| `SHA256SUMS` | checksums |

🤖 Generated with [Claude Code](https://claude.com/claude-code)
