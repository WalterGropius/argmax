#nullable disable
using System;

namespace Argmax.Sim
{
    /// <summary>
    /// The System-1 move classifier, after TypeSafe's Jev: no search, no graph, one typed choice question
    /// per unit per step: "which of these 9 tiles?", answered with a probability for every option.
    ///
    /// Each candidate tile is described by 13 integer features read from the unit's 5x5 receptive field
    /// (the union of the 3x3 neighbourhoods of all 9 candidates) plus world gradient fields. A unit's program
    /// supplies the weights, so a program is literally a linear softmax classifier. Players write weights.
    /// ML can learn them. Same function either way.
    /// </summary>
    public static class Jev
    {
        public const int Options = 9; // index 4 = stay
        public const int BaseTemperature = 6;
        // round(4096 * e^(-i/8)), baked so no floating point ever touches the simulation
        static readonly int[] Exp =
        {
            4096, 3615, 3190, 2815, 2484, 2192, 1935, 1707, 1507, 1330, 1174, 1036, 914, 807, 712, 628,
            554, 489, 432, 381, 336, 297, 262, 231, 204, 180, 159, 140, 124, 109, 96, 85,
            75, 66, 58, 52, 46, 40, 35, 31, 28, 24, 21, 19, 17, 15, 13, 12,
            10, 9, 8, 7, 6, 5, 5, 4, 4, 3, 3, 3, 2, 2, 2, 2,
        };

        public struct Choice
        {
            public int Pick;
            public int Best;
            public int[] Scores;   // per option, int.MinValue = blocked
            public int[] Permille; // softmax probability per option
        }

        /// <summary>Alignment of a step (dx,dy) with vector v, in -10..10.</summary>
        public static int Align(int dx, int dy, int vx, int vy)
        {
            if ((dx == 0 && dy == 0) || (vx == 0 && vy == 0)) return 0;
            int len = ISqrt(vx * vx + vy * vy);
            if (len == 0) len = 1;
            int dlen = dx != 0 && dy != 0 ? 141 : 100;
            return (dx * vx + dy * vy) * 1000 / (dlen * len);
        }

        public static int ISqrt(int n)
        {
            if (n <= 0) return 0;
            int x = n, y = (x + 1) >> 1;
            while (y < x) { x = y; y = (x + n / x) >> 1; }
            return x;
        }

        public static bool Pattern(byte pattern, int x, int y)
        {
            switch (pattern)
            {
                case 1: return ((x + y) & 1) == 0;            // checker: splash-proof spacing
                case 2: return (y & 1) == 0;                  // rows
                case 3: return (x & 1) == 0;                  // cols
                case 4: return (x & 1) == 0 && (y & 1) == 0;  // grid
                case 5: return ((x - y) % 3 + 3) % 3 == 0;    // diagonals
                default: return true;
            }
        }

        // 3x3 neighbourhood of each candidate, minus its centre, as bits of the mover's 5x5 window
        static readonly uint[] SepMask = BuildSepMask();

        static uint[] BuildSepMask()
        {
            var m = new uint[Options];
            for (int k = 0; k < Options; k++)
            {
                int cx = k % 3 - 1, cy = k / 3 - 1;
                for (int ey = -1; ey <= 1; ey++)
                    for (int ex = -1; ex <= 1; ex++)
                        if (ex != 0 || ey != 0) m[k] |= 1u << ((cy + ey + 2) * 5 + cx + ex + 2);
            }
            return m;
        }

        static int PopCount(uint v)
        {
            v -= (v >> 1) & 0x55555555u;
            v = (v & 0x33333333u) + ((v >> 2) & 0x33333333u);
            return (int)((((v + (v >> 4)) & 0x0F0F0F0Fu) * 0x01010101u) >> 24);
        }

        static int AlignL(int dx, int dy, int vx, int vy, int len)
        {
            if (len == 0 || (dx == 0 && dy == 0)) return 0;
            return (dx * vx + dy * vy) * 1000 / ((dx != 0 && dy != 0 ? 141 : 100) * len);
        }

        /// <summary>
        /// Score every candidate and sample one. Deterministic: the "random" draws are hashes of (seed, tick, id).
        /// Everything that does not depend on the candidate is computed once per mote, not 9 times.
        /// scores/permille may be null when the caller doesn't need the explanation.
        /// </summary>
        internal static int Classify(World w, int i, int[] weights, int wOff, int temperature, byte pattern,
            int[] scores, int[] permille)
        {
            var S = w.S;
            var map = w.Map;
            ref var m = ref w.Motes[i];
            ref var ctx = ref S.Ctx[i];
            int W = map.W;
            int x = m.X, y = m.Y, cur = y * W + x;
            int seat = m.Seat;
            var step = map.Step9;
            var occ = S.Occ;

            ushort[] home = seat < 2 ? map.Home(seat) : null;
            ushort[] core = seat < 2 ? (map.HasCore[1 - seat] ? map.Home(1 - seat) : null) : map.AnyCore;
            ushort[] beacon = seat < 2 && w.Seats[seat].BeaconOn ? S.Beacon[seat] : null;
            byte[] trail = seat < 2 ? w.Trails[seat] : null;
            ushort[] scrapF = S.ScrapField;

            int wOrder = m.Has(MoteFlags.HasOrder) ? weights[wOff + (int)Channel.Order] : 0;
            int wEnemy = ctx.HasEnemy ? weights[wOff + (int)Channel.Enemy] : 0;
            int wScrap = weights[wOff + (int)Channel.Scrap];
            int wHome = home != null ? weights[wOff + (int)Channel.Home] : 0;
            int wBeacon = beacon != null ? weights[wOff + (int)Channel.Beacon] : 0;
            int wCore = core != null ? weights[wOff + (int)Channel.Core] : 0;
            int wCoh = ctx.Flock > 0 ? weights[wOff + (int)Channel.Cohesion] : 0;
            int wAli = ctx.Flock > 0 ? weights[wOff + (int)Channel.Align] : 0;
            int wSep = weights[wOff + (int)Channel.Separation];
            int wMom = weights[wOff + (int)Channel.Momentum];
            int wPat = pattern != 0 ? weights[wOff + (int)Channel.Pattern] : 0;
            int wTrail = trail != null ? weights[wOff + (int)Channel.Trail] : 0;
            int wNoise = weights[wOff + (int)Channel.Noise];

            // per-mote invariants
            int ovx = m.Ox - x, ovy = m.Oy - y, olen = wOrder != 0 ? ISqrt(ovx * ovx + ovy * ovy) : 0;
            int odcur = Math.Max(Math.Abs(ovx), Math.Abs(ovy));
            int evx = ctx.Ex - x, evy = ctx.Ey - y, elen = wEnemy != 0 ? ISqrt(evx * evx + evy * evy) : 0;
            int edcur = Math.Max(Math.Abs(evx), Math.Abs(evy));
            int clen = wCoh != 0 ? ISqrt(ctx.SumDx * ctx.SumDx + ctx.SumDy * ctx.SumDy) : 0;
            int alen = wAli != 0 ? ISqrt(ctx.SumHx * ctx.SumHx + ctx.SumHy * ctx.SumHy) : 0;
            int mlen = m.Hx != 0 || m.Hy != 0 ? 1 : 0;
            int fScrap = scrapF[cur], fHome = home != null ? home[cur] : 0, fBeacon = beacon != null ? beacon[cur] : 0;
            int fCore = core != null ? core[cur] : 0, fTrail = trail != null ? trail[cur] : 0;
            ulong noise = wNoise != 0 ? Rng.Roll64(w.Seed, w.Tick, m.Id, 0) : 0;
            uint allies = ctx.Allies25;

            int best = int.MinValue, bestK = 4;
            Span<int> sc = stackalloc int[Options];
            int b9 = cur * 9;
            for (int k = 0; k < Options; k++)
            {
                int n = step[b9 + k];
                if (n < 0) { sc[k] = int.MinValue; continue; }
                if (k != 4)
                {
                    int o = occ[n];
                    if (o != 0 && o != i + 1) { sc[k] = int.MinValue; continue; }
                }
                int dx = k % 3 - 1, dy = k / 3 - 1;
                int nx = x + dx, ny = y + dy;
                int s = 0;
                if (wOrder != 0)
                {
                    int dn = Math.Max(Math.Abs(m.Ox - nx), Math.Abs(m.Oy - ny));
                    s += wOrder * ((odcur - dn) * 8 + AlignL(dx, dy, ovx, ovy, olen) / 5);
                }
                if (wEnemy != 0)
                {
                    int dn = Math.Max(Math.Abs(ctx.Ex - nx), Math.Abs(ctx.Ey - ny));
                    s += wEnemy * ((edcur - dn) * 8 + AlignL(dx, dy, evx, evy, elen) / 5);
                }
                if (wScrap != 0) s += wScrap * Grad(fScrap, scrapF[n]);
                if (wHome != 0) s += wHome * Grad(fHome, home[n]);
                if (wBeacon != 0) s += wBeacon * Grad(fBeacon, beacon[n]);
                if (wCore != 0) s += wCore * Grad(fCore, core[n]);
                if (wCoh != 0) s += wCoh * AlignL(dx, dy, ctx.SumDx, ctx.SumDy, clen);
                if (wAli != 0) s += wAli * AlignL(dx, dy, ctx.SumHx, ctx.SumHy, alen);
                if (wSep != 0) s -= wSep * 3 * PopCount(allies & SepMask[k]);
                if (wMom != 0 && mlen != 0) s += wMom * AlignL(dx, dy, m.Hx, m.Hy, 1);
                if (wPat != 0) s += wPat * (Pattern(pattern, nx, ny) ? 4 : -4);
                if (wTrail != 0) s += wTrail * Clamp((trail[n] - fTrail) / 8, -10, 10);
                if (wNoise != 0) s += wNoise * ((int)((noise >> (5 * k)) & 31) % 21 - 10);
                sc[k] = s;
                if (s > best) { best = s; bestK = k; }
            }

            // softmax over integer scores via lookup table; temperature from the program (wander raises it)
            int t = Math.Max(1, temperature);
            Span<int> p = stackalloc int[Options];
            int sum = 0;
            for (int k = 0; k < Options; k++)
            {
                if (sc[k] == int.MinValue) { p[k] = 0; continue; }
                long gap = ((long)best - sc[k]) * 8 / t;
                p[k] = gap >= 64 ? 0 : Exp[gap];
                sum += p[k];
            }
            int pick = bestK;
            if (sum > 0)
            {
                int r = (int)(Rng.Roll(w.Seed, w.Tick, m.Id, 977) % (uint)sum);
                for (int k = 0; k < Options; k++)
                {
                    r -= p[k];
                    if (r < 0) { pick = k; break; }
                }
            }
            if (scores != null) for (int k = 0; k < Options; k++) scores[k] = sc[k];
            if (permille != null) for (int k = 0; k < Options; k++) permille[k] = sum > 0 ? p[k] * 1000 / sum : (k == 4 ? 1000 : 0);
            return pick;
        }

        static int Grad(int a, int b)
        {
            if (a == Fields.Unreachable || b == Fields.Unreachable) return 0;
            int d = a - b;
            return (d < -2 ? -2 : d > 2 ? 2 : d) * 8;
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
