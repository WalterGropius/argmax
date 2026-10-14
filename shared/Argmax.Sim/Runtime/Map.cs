#nullable disable
using System;
using System.Collections.Generic;

namespace Argmax.Sim
{
    public enum Terrain : byte { Floor, Wall, Core, Portal }

    /// <summary>
    /// Static terrain. Never stored in the database: both server and client regenerate it
    /// from the match seed (or a chamber's ASCII layout), so it costs zero bandwidth.
    /// </summary>
    public sealed class Map
    {
        public int W, H;
        public Terrain[] Cell;
        public int[] Twin;                  // portal partner cell, -1 otherwise
        public short[] CoreX = new short[2], CoreY = new short[2];
        public bool[] HasCore = new bool[2];
        public List<ScrapNode> ScrapSpawns = new List<ScrapNode>();
        public List<int> Wells = new List<int>();
        public List<int> Goals = new List<int>();
        public List<int> Portals = new List<int>();
        ushort[][] home = new ushort[2][];
        ushort[] anyCore;
        int[] step9;

        public Map(int w, int h)
        {
            W = w; H = h;
            Cell = new Terrain[w * h];
            Twin = new int[w * h];
            for (int i = 0; i < Twin.Length; i++) Twin[i] = -1;
        }

        public int Idx(int x, int y) => y * W + x;
        public bool In(int x, int y) => (uint)x < (uint)W && (uint)y < (uint)H;
        public bool Passable(int x, int y) => In(x, y) && (Cell[y * W + x] == Terrain.Floor || Cell[y * W + x] == Terrain.Portal);

        /// <summary>One step from (x,y) by (dx,dy) ignoring units. Diagonals may not squeeze between two walls.</summary>
        public bool CanStep(int x, int y, int dx, int dy)
        {
            int nx = x + dx, ny = y + dy;
            if (!Passable(nx, ny)) return false;
            if (dx != 0 && dy != 0 && !Passable(x + dx, y) && !Passable(x, y + dy)) return false;
            return true;
        }

        /// <summary>
        /// For every cell, where each of the 9 Jev candidates leads (grid order, index 4 = stay), or -1 if the
        /// static terrain forbids that step. Built once per map; hot loops never call CanStep.
        /// </summary>
        public int[] Step9
        {
            get
            {
                if (step9 != null) return step9;
                var t = new int[W * H * 9];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        int c = y * W + x, b = c * 9;
                        bool here = Passable(x, y);
                        for (int k = 0; k < 9; k++)
                        {
                            int dx = k % 3 - 1, dy = k / 3 - 1;
                            t[b + k] = k == 4 ? (here ? c : -1) : (here && CanStep(x, y, dx, dy) ? c + dy * W + dx : -1);
                        }
                    }
                step9 = t;
                return t;
            }
        }

        /// <summary>Distance to the nearest core of either player (what feral `seek core` follows). Static.</summary>
        public ushort[] AnyCore
        {
            get
            {
                if (anyCore != null) return anyCore;
                var f = new ushort[W * H];
                var h0 = Home(0); var h1 = Home(1);
                for (int i = 0; i < f.Length; i++)
                {
                    ushort a = h0 != null ? h0[i] : Fields.Unreachable, b = h1 != null ? h1[i] : Fields.Unreachable;
                    f[i] = a < b ? a : b;
                }
                anyCore = f;
                return f;
            }
        }

        public void PlaceCore(int seat, int cx, int cy)
        {
            CoreX[seat] = (short)cx; CoreY[seat] = (short)cy; HasCore[seat] = true;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (In(cx + dx, cy + dy)) Cell[Idx(cx + dx, cy + dy)] = Terrain.Core;
        }

        public void LinkPortals(int a, int b)
        {
            Cell[a] = Terrain.Portal; Cell[b] = Terrain.Portal;
            Twin[a] = b; Twin[b] = a;
            Portals.Add(a); Portals.Add(b);
        }

        /// <summary>Distance field to the ring of cells touching a seat's core. Static, so cached.</summary>
        public ushort[] Home(int seat)
        {
            if (!HasCore[seat]) return null;
            if (home[seat] != null) return home[seat];
            var f = new ushort[W * H];
            var q = new int[W * H * 2];
            var src = new List<int>();
            int cx = CoreX[seat], cy = CoreY[seat];
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                    if ((Math.Abs(dx) == 2 || Math.Abs(dy) == 2) && Passable(cx + dx, cy + dy)) src.Add(Idx(cx + dx, cy + dy));
            Fields.Bfs(this, src, f, q);
            home[seat] = f;
            return f;
        }

        public static int Cheb(int ax, int ay, int bx, int by) => Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));

        /// <summary>Chebyshev distance from a point to a seat's 3x3 core block (0 = touching).</summary>
        public int CoreDist(int seat, int x, int y) => Math.Max(0, Cheb(x, y, CoreX[seat], CoreY[seat]) - 1);

        /// <summary>Nearest passable cell to (x,y) by expanding rings. Returns -1 if none within r.</summary>
        public int NearestPassable(int x, int y, int r)
        {
            for (int d = 0; d <= r; d++)
                for (int dy = -d; dy <= d; dy++)
                    for (int dx = -d; dx <= d; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != d) continue;
                        if (Passable(x + dx, y + dy)) return Idx(x + dx, y + dy);
                    }
            return -1;
        }

        // ------------------------------------------------------------------ generation

        static readonly Dictionary<ulong, Map> cache = new Dictionary<ulong, Map>();

        /// <summary>Procedural, point-symmetric duel map. Pure function of the seed.</summary>
        public static Map Duel(ulong seed)
        {
            lock (cache)
            {
                Map m;
                if (cache.TryGetValue(seed, out m)) return m;
                for (int attempt = 0; attempt < 16; attempt++)
                {
                    m = GenerateDuel(seed + (ulong)attempt * 7919UL);
                    if (m != null) break;
                }
                if (cache.Count > 64) cache.Clear();
                cache[seed] = m;
                return m;
            }
        }

        static Map GenerateDuel(ulong seed)
        {
            int W = Rules.MapW, H = Rules.MapH;
            var m = new Map(W, H);
            var rng = new Rng(seed);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (x == 0 || y == 0 || x == W - 1 || y == H - 1) m.Cell[m.Idx(x, y)] = Terrain.Wall;

            int c0x = 8, c0y = 8;
            m.PlaceCore(0, c0x, c0y);
            m.PlaceCore(1, W - 1 - c0x, H - 1 - c0y);

            // points of interest in the lower-left half; mirrored through the centre
            var poi = new List<(int x, int y, int amount)>
            {
                (14, 6, 260), (6, 15, 260), (15, 14, 220),
                (24, 9, 380), (9, 24, 380),
                (22 + rng.Range(-2, 3), 30 + rng.Range(-2, 3), 520),
                (30, 31, 900),
            };
            foreach (var p in poi)
            {
                m.ScrapSpawns.Add(new ScrapNode { X = (short)p.x, Y = (short)p.y, Amount = p.amount });
                m.ScrapSpawns.Add(new ScrapNode { X = (short)(W - 1 - p.x), Y = (short)(H - 1 - p.y), Amount = p.amount });
            }
            m.Wells.Add(m.Idx(10, 52)); m.Wells.Add(m.Idx(W - 1 - 10, H - 1 - 52));
            // two portal pairs, each linking a cell to its mirror: flank routes across the map
            m.LinkPortals(m.Idx(3, 34), m.Idx(W - 1 - 3, H - 1 - 34));
            m.LinkPortals(m.Idx(34, 3), m.Idx(W - 1 - 34, H - 1 - 3));

            bool Reserved(int x, int y)
            {
                if (Cheb(x, y, c0x, c0y) <= 5 || Cheb(x, y, W - 1 - c0x, H - 1 - c0y) <= 5) return true;
                foreach (var s in m.ScrapSpawns) if (Cheb(x, y, s.X, s.Y) <= 2) return true;
                foreach (var c in m.Wells) if (Cheb(x, y, c % W, c / W) <= 2) return true;
                foreach (var c in m.Portals) if (Cheb(x, y, c % W, c / W) <= 1) return true;
                return false;
            }

            // ruins: rectangles and L shapes, mirrored
            int pieces = 30 + rng.Range(0, 8);
            for (int i = 0; i < pieces; i++)
            {
                int x = rng.Range(2, W - 3), y = rng.Range(2, H - 3);
                if (x + y > W) continue; // stay in our half; the mirror covers the rest
                bool horizontal = (rng.Next() & 1) == 0;
                int len = rng.Range(2, 7), thick = rng.Range(1, 3);
                bool ell = rng.Range(0, 4) == 0;
                for (int a = 0; a < len; a++)
                    for (int b = 0; b < thick; b++)
                    {
                        int px = horizontal ? x + a : x + b, py = horizontal ? y + b : y + a;
                        Carve(px, py);
                        if (ell && a == len - 1) for (int k = 1; k < 4; k++) Carve(horizontal ? px : px + k, horizontal ? py + k : py);
                    }
            }
            void Carve(int x, int y)
            {
                if (!m.In(x, y) || Reserved(x, y)) return;
                int i0 = m.Idx(x, y), i1 = m.Idx(W - 1 - x, H - 1 - y);
                if (m.Cell[i0] != Terrain.Floor || m.Cell[i1] != Terrain.Floor) return;
                m.Cell[i0] = Terrain.Wall; m.Cell[i1] = Terrain.Wall;
            }

            // every point of interest must be reachable from both cores
            var f = m.Home(0);
            foreach (var s in m.ScrapSpawns)
            {
                bool ok = false;
                for (int d = 0; d < 9 && !ok; d++)
                {
                    int nx = s.X + d % 3 - 1, ny = s.Y + d / 3 - 1;
                    if (m.Passable(nx, ny) && f[m.Idx(nx, ny)] != Fields.Unreachable) ok = true;
                }
                if (!ok) return null;
            }
            foreach (var c in m.Wells) if (f[c] == Fields.Unreachable) return null;
            int ring1 = m.Idx(m.CoreX[1] + 2, m.CoreY[1]);
            if (f[ring1] == Fields.Unreachable) return null;
            return m;
        }

        /// <summary>
        /// Chambers are drawn in ASCII. '#' wall, 'C' your core, 'D' a target core, 'S' scrap,
        /// 'A'/'a' and 'B'/'b' portal pairs, 'W' well, 'G' goal cell. Row 0 is the top line.
        /// </summary>
        public static Map Ascii(string[] rows, int scrapAmount)
        {
            int h = rows.Length, w = 0;
            foreach (var r in rows) w = Math.Max(w, r.Length);
            var m = new Map(w, h);
            int a0 = -1, a1 = -1, b0 = -1, b1 = -1;
            for (int ry = 0; ry < h; ry++)
            {
                int y = h - 1 - ry;
                for (int x = 0; x < w; x++)
                {
                    char ch = x < rows[ry].Length ? rows[ry][x] : '#';
                    int i = m.Idx(x, y);
                    switch (ch)
                    {
                        case '#': m.Cell[i] = Terrain.Wall; break;
                        case 'S': m.ScrapSpawns.Add(new ScrapNode { X = (short)x, Y = (short)y, Amount = scrapAmount }); break;
                        case 'W': m.Wells.Add(i); break;
                        case 'G': m.Goals.Add(i); break;
                        case 'A': a0 = i; break;
                        case 'a': a1 = i; break;
                        case 'B': b0 = i; break;
                        case 'b': b1 = i; break;
                    }
                }
            }
            for (int ry = 0; ry < h; ry++)
                for (int x = 0; x < rows[ry].Length; x++)
                {
                    if (rows[ry][x] == 'C') m.PlaceCore(0, x, h - 1 - ry);
                    if (rows[ry][x] == 'D') m.PlaceCore(1, x, h - 1 - ry);
                }
            if (a0 >= 0 && a1 >= 0) m.LinkPortals(a0, a1);
            if (b0 >= 0 && b1 >= 0) m.LinkPortals(b0, b1);
            return m;
        }
    }

    public static class Fields
    {
        public const ushort Unreachable = ushort.MaxValue;

        /// <summary>
        /// Multi-source BFS over passable cells, 8-connected, portals are free edges.
        /// Units never search: the world broadcasts gradients, each unit reads 3x3 of them.
        /// </summary>
        public static void Bfs(Map m, List<int> sources, ushort[] dist, int[] queue)
        {
            for (int i = 0; i < dist.Length; i++) dist[i] = Unreachable;
            int head = 0, tail = 0;
            foreach (var s in sources)
            {
                if (dist[s] == 0) continue;
                dist[s] = 0; queue[tail++] = s;
                int t = m.Twin[s];
                if (t >= 0 && dist[t] != 0) { dist[t] = 0; queue[tail++] = t; }
            }
            Run(m, dist, queue, head, tail);
        }

        internal static void Run(Map m, ushort[] dist, int[] queue, int head, int tail)
        {
            var step = m.Step9;
            var twin = m.Twin;
            while (head < tail)
            {
                int c = queue[head++];
                ushort nd = (ushort)(dist[c] + 1);
                int b = c * 9;
                for (int k = 0; k < 9; k++)
                {
                    int n = step[b + k];
                    if (n < 0 || k == 4 || dist[n] <= nd) continue;
                    dist[n] = nd; queue[tail++] = n;
                    int t = twin[n];
                    if (t >= 0 && dist[t] > nd) { dist[t] = nd; queue[tail++] = t; }
                }
            }
        }
    }

    /// <summary>splitmix64. All randomness is a pure function of (seed, tick, id): no hidden state.</summary>
    public struct Rng
    {
        ulong s;
        public Rng(ulong seed) { s = seed; }
        public uint Next() { s += 0x9E3779B97F4A7C15UL; return (uint)(Mix(s) >> 32); }
        public int Range(int lo, int hiExclusive) => lo + (int)(Next() % (uint)(hiExclusive - lo));

        public static ulong Mix(ulong z)
        {
            unchecked
            {
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public static uint Roll(ulong seed, int tick, uint id, int salt) => (uint)(Roll64(seed, tick, id, salt) >> 32);

        public static ulong Roll64(ulong seed, int tick, uint id, int salt)
        {
            unchecked
            {
                ulong z = seed ^ ((ulong)(uint)tick << 32 | id) * 0x9E3779B97F4A7C15UL ^ (ulong)(uint)salt * 0xD6E8FEB86659FD93UL;
                return Mix(z + 0x9E3779B97F4A7C15UL);
            }
        }
    }
}
