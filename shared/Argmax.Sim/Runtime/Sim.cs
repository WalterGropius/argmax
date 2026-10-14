#nullable disable
using System;
using System.Collections.Generic;

namespace Argmax.Sim
{
    /// <summary>What happened during a step, for effects and replays. Packed ints, no allocations per event.</summary>
    public sealed class StepLog
    {
        public readonly List<int> Shots = new List<int>();     // from | to<<12 | seat<<24 | splash<<26 | infect<<27 | core<<28
        public readonly List<int> Deaths = new List<int>();    // cell | seat<<12 | tier<<14
        public readonly List<int> Merges = new List<int>();    // cell | tier<<12 | seat<<16
        public readonly List<int> Converts = new List<int>();  // cell | seat<<12
        public readonly List<int> Teleports = new List<int>(); // from | to<<12
        public readonly List<int> Spawns = new List<int>();    // cell | seat<<12

        public void Clear()
        {
            Shots.Clear(); Deaths.Clear(); Merges.Clear(); Converts.Clear(); Teleports.Clear(); Spawns.Clear();
        }

        public static int Shot(int from, int to, int seat, bool splash, bool infect, bool core) =>
            from | (to << 12) | (seat << 24) | (splash ? 1 << 26 : 0) | (infect ? 1 << 27 : 0) | (core ? 1 << 28 : 0);
    }

    public static partial class Sim
    {
        const int C = (int)Channel.Count;
        static readonly sbyte[] Dir8X = { 1, 1, 0, -1, -1, -1, 0, 1 };
        static readonly sbyte[] Dir8Y = { 0, 1, 1, 1, 0, -1, -1, -1 };

        // ================================================================== factories

        public static World NewDuel(ulong seed, SeatKind k0, SeatKind k1, uint meta0 = 0, uint meta1 = 0)
        {
            var map = Map.Duel(seed);
            var w = new World(map) { Seed = seed, Mode = WorldMode.Duel };
            w.Seats[0].Kind = k0; w.Seats[1].Kind = k1; w.Seats[2].Kind = SeatKind.Noise;
            w.Seats[0].Meta = meta0; w.Seats[1].Meta = meta1;
            var scrap = new ScrapNode[map.ScrapSpawns.Count];
            for (int i = 0; i < scrap.Length; i++) scrap[i] = map.ScrapSpawns[i];
            w.Scrap = scrap;
            Prepare(w);
            for (int s = 0; s < 2; s++)
            {
                var seat = w.Seats[s];
                seat.Scrap = Rules.StartScrap;
                seat.CoreHp = Rules.CoreHp;
                seat.CoreX = map.CoreX[s]; seat.CoreY = map.CoreY[s];
                Array.Copy(Presets.DefaultLoadout, seat.Loadout, Rules.LoadoutSlots);
                seat.Production = 1;
                for (int k = 0; k < 6; k++) SpawnMote(w, s, k < 4 ? Presets.Harvest : Presets.Swarm, 1);
            }
            return w;
        }

        // ================================================================== step

        public static void Step(World w, List<Cmd> cmds, StepLog log = null)
        {
            if (w.Ended) return;
            log?.Clear();
            var S = Prepare(w);
            int cells = w.W * w.H;

            if (cmds != null) for (int i = 0; i < cmds.Count; i++) Apply(w, cmds[i]);
            if (w.Tick % Rules.MetaEvery == 5)
                for (int s = 0; s < 2; s++)
                {
                    var seat = w.Seats[s];
                    if (!seat.Alive || seat.Meta == 0) continue;
                    var mp = w.GetProgram(seat.Meta);
                    if (mp != null && mp.Kind == ProgramKind.Meta) Meta.Apply(w, s, Meta.Decide(w, s, mp));
                }
            S.Ensure(cells, w.MoteCount + 64);
            ComputeFields(w);

            int n = w.MoteCount;
            Array.Clear(S.Dead, 0, S.Cap);
            Array.Clear(S.Used, 0, S.Cap);
            for (int i = 0; i < n; i++)
            {
                ref var m = ref w.Motes[i];
                m.Flags &= ~MoteFlags.Fired;
                if (m.Cooldown > 0) m.Cooldown--;
                S.Sensors[i] = Sense(w, i);
                Think(w, i);
            }

            Combat(w, n, log);
            Move(w, n, log);
            Economy(w, n);
            MergePairs(w, n, log);
            Coherence(w, n, log);
            Splits(w, n);
            Compact(w);
            BuildOcc(w);

            Produce(w, log);
            Ferals(w, log);
            Upkeep(w);
            CheckWin(w);
            w.Tick++;
        }

        // ================================================================== preparation

        /// <summary>Rebuild derived state (occupancy, seat tallies) after a world was edited from outside.</summary>
        public static void Refresh(World w) { Prepare(w); }

        internal static Scratch Prepare(World w)
        {
            if (w.S == null) w.S = new Scratch();
            w.S.Ensure(w.W * w.H, w.MoteCount + 64);
            BuildOcc(w);
            TallySeats(w);
            return w.S;
        }

        internal static void BuildOcc(World w)
        {
            var occ = w.S.Occ;
            Array.Clear(occ, 0, occ.Length);
            int W = w.W;
            for (int i = 0; i < w.MoteCount; i++) occ[w.Motes[i].Y * W + w.Motes[i].X] = i + 1;
        }

        internal static void TallySeats(World w)
        {
            var T = w.S.Tally;
            for (int s = 0; s < T.Length; s++) T[s] = default;
            var map = w.Map;
            for (int i = 0; i < w.MoteCount; i++)
            {
                ref var m = ref w.Motes[i];
                ref var t = ref T[m.Seat];
                t.Count++;
                t.Value += m.Value;
                if (m.Tier > t.MaxTier) t.MaxTier = m.Tier;
                var p = w.GetProgram(m.Program);
                if (p != null && p.Worker) t.Workers++; else t.ArmyValue += m.Value;
                for (int s = 0; s < 2; s++)
                {
                    if (s == m.Seat || !map.HasCore[s]) continue;
                    int d = map.CoreDist(s, m.X, m.Y);
                    if (d <= 10) { T[s].EnemiesNearCore++; if (m.Seat == Rules.NoiseSeat) T[s].FeralsNearCore++; }
                    if (d <= 6 && m.Seat < 2) T[m.Seat].SiegeMotes++;
                }
            }
        }

        internal static void ComputeFields(World w)
        {
            var S = w.S; var map = w.Map;
            ulong mask = 0;
            bool many = w.Scrap.Length > 64;
            for (int k = 0; k < w.Scrap.Length && !many; k++) if (w.Scrap[k].Amount > 0) mask |= 1UL << k;
            if (many || mask != S.ScrapMask)
            {
                var src = new List<int>();
                foreach (var node in w.Scrap)
                {
                    if (node.Amount <= 0) continue;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (map.Passable(node.X + dx, node.Y + dy)) src.Add(map.Idx(node.X + dx, node.Y + dy));
                }
                Fields.Bfs(map, src, S.ScrapField, S.Queue);
                S.ScrapMask = many ? ulong.MaxValue : mask;
            }
            for (int s = 0; s < 2; s++)
            {
                var seat = w.Seats[s];
                int at = seat.BeaconOn ? map.Idx(seat.BeaconX, seat.BeaconY) : -1;
                if (at == S.BeaconAt[s]) continue;
                S.BeaconAt[s] = at;
                if (at < 0) continue;
                Fields.Bfs(map, new List<int> { at }, S.Beacon[s], S.Queue);
            }
        }

        // ================================================================== commands

        public static void Apply(World w, Cmd c)
        {
            if (c.Seat >= 2) return;
            var seat = w.Seats[c.Seat];
            if (seat.Kind == SeatKind.Empty || (seat.HasCore && seat.CoreHp <= 0 && w.Mode == WorldMode.Duel)) return;
            var map = w.Map;
            switch (c.Kind)
            {
                case CmdKind.Move:
                case CmdKind.Attack:
                case CmdKind.Stop:
                case CmdKind.Hold:
                case CmdKind.Merge:
                case CmdKind.Split:
                case CmdKind.Reprogram:
                    if (c.Ids == null) return;
                    Program rp = null;
                    if (c.Kind == CmdKind.Reprogram)
                    {
                        rp = w.GetProgram(c.Arg);
                        if (rp == null || rp.Kind != ProgramKind.Unit) return;
                    }
                    short ox = (short)Clamp(c.X, 0, map.W - 1), oy = (short)Clamp(c.Y, 0, map.H - 1);
                    foreach (var id in c.Ids)
                    {
                        int i = w.FindMote(id);
                        if (i < 0) continue;
                        ref var m = ref w.Motes[i];
                        if (m.Seat != c.Seat) continue;
                        switch (c.Kind)
                        {
                            case CmdKind.Move:
                                m.Ox = ox; m.Oy = oy; m.Flags |= MoteFlags.HasOrder; m.Flags &= ~MoteFlags.HoldCmd;
                                m.Override = Rules.OverrideTicks;
                                break;
                            case CmdKind.Attack:
                                m.Ox = ox; m.Oy = oy; m.Flags |= MoteFlags.HasOrder; m.Flags &= ~MoteFlags.HoldCmd;
                                m.Override = 0;
                                break;
                            case CmdKind.Stop:
                                m.Flags &= ~(MoteFlags.HasOrder | MoteFlags.HoldCmd | MoteFlags.MergeCmd | MoteFlags.SplitCmd);
                                m.Override = 0;
                                break;
                            case CmdKind.Hold: m.Flags |= MoteFlags.HoldCmd; m.Override = 0; break;
                            case CmdKind.Merge: m.Flags |= MoteFlags.MergeCmd; m.Flags &= ~MoteFlags.SplitCmd; break;
                            case CmdKind.Split: m.Flags |= MoteFlags.SplitCmd; m.Flags &= ~MoteFlags.MergeCmd; break;
                            case CmdKind.Reprogram: m.Program = rp.Id; break;
                        }
                    }
                    break;
                case CmdKind.Beacon:
                    int bc = map.NearestPassable(Clamp(c.X, 0, map.W - 1), Clamp(c.Y, 0, map.H - 1), 4);
                    if (bc < 0) return;
                    seat.BeaconOn = true; seat.BeaconX = (short)(bc % map.W); seat.BeaconY = (short)(bc / map.W);
                    break;
                case CmdKind.ClearBeacon: seat.BeaconOn = false; break;
                case CmdKind.Produce:
                    if (c.Arg < Rules.LoadoutSlots) seat.Production = (byte)c.Arg;
                    break;
                case CmdKind.Auto: seat.Auto = c.Arg != 0; break;
                case CmdKind.Spawn: TrySpawn(w, c.Seat, (int)Math.Min(c.Arg, 20u), null); break;
                case CmdKind.SetSlot:
                    if (c.X < 0 || c.X >= Rules.LoadoutSlots) return;
                    var sp = w.GetProgram(c.Arg);
                    if (sp == null || sp.Kind != ProgramKind.Unit) return;
                    seat.Loadout[c.X] = sp.Id;
                    break;
                case CmdKind.SetMeta:
                    if (c.Arg == 0) { seat.Meta = 0; break; }
                    var mp = w.GetProgram(c.Arg);
                    if (mp != null && mp.Kind == ProgramKind.Meta) seat.Meta = mp.Id;
                    break;
                case CmdKind.Surrender:
                    if (w.Mode == WorldMode.Duel) w.Winner = (byte)w.Other(c.Seat);
                    else w.Winner = 1;
                    break;
            }
        }

        // ================================================================== sense & think

        static uint Sense(World w, int i)
        {
            var S = w.S; var map = w.Map;
            ref var m = ref w.Motes[i];
            ref var ctx = ref S.Ctx[i];
            ctx = default;
            int W = map.W, x = m.X, y = m.Y, seat = m.Seat;
            int r = Rules.SenseRadius, range = Rules.Range(m.Tier);
            int alliesNear = 0, enemiesNear = 0, adjacent = 0, bestD = int.MaxValue;
            uint bestId = uint.MaxValue;
            bool contact = false, pair = false, wild = false;
            int y0 = Math.Max(0, y - r), y1 = Math.Min(map.H - 1, y + r), x0 = Math.Max(0, x - r), x1 = Math.Min(W - 1, x + r);
            for (int yy = y0; yy <= y1; yy++)
                for (int xx = x0; xx <= x1; xx++)
                {
                    int o = S.Occ[yy * W + xx] - 1;
                    if (o < 0 || o == i) continue;
                    ref var om = ref w.Motes[o];
                    int dx = xx - x, dy = yy - y;
                    int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    if (om.Seat == seat)
                    {
                        alliesNear++;
                        if (d <= Rules.FlockRadius)
                        {
                            ctx.Flock++; ctx.SumDx += (short)dx; ctx.SumDy += (short)dy;
                            ctx.SumHx += om.Hx; ctx.SumHy += om.Hy;
                            ctx.Allies25 |= 1u << ((dy + 2) * 5 + dx + 2);
                        }
                        if (d <= 1) { adjacent++; if (om.Tier == m.Tier) pair = true; }
                    }
                    else
                    {
                        enemiesNear++;
                        if (om.Seat == Rules.NoiseSeat) wild = true;
                        if (d <= range) contact = true;
                        if (d < bestD || (d == bestD && om.Id < bestId)) { bestD = d; bestId = om.Id; ctx.Ex = (short)xx; ctx.Ey = (short)yy; ctx.HasEnemy = true; }
                    }
                }
            ctx.Near = (byte)Math.Min(255, alliesNear);

            bool siege = false;
            for (int s = 0; s < 2; s++)
            {
                if (s == seat || !map.HasCore[s] || !w.Seats[s].Alive) continue;
                int d = map.CoreDist(s, x, y);
                if (d <= r) siege = true;
                if (d <= range) contact = true;
            }

            uint b = 0;
            if (enemiesNear > 0) b |= 1u << 0;
            if (contact) b |= 1u << 1;
            if (ctx.Flock >= 3) b |= 1u << 2;
            if (adjacent >= 5) b |= 1u << 3;
            if (m.Hp * 2 < Rules.MaxHp(m.Tier)) b |= 1u << 4;
            if (m.Carry > 0) b |= 1u << 5;
            if (m.Carry >= Rules.Capacity(m.Tier)) b |= 1u << 6;
            if (m.Has(MoteFlags.HasOrder)) b |= 1u << 7;
            if (S.ScrapField[y * W + x] < r) b |= 1u << 8;
            if (seat < 2 && map.HasCore[seat] && map.CoreDist(seat, x, y) <= 3) b |= 1u << 9;
            if (pair) b |= 1u << 10;
            if (seat < 2 && w.Seats[seat].BeaconOn) b |= 1u << 11;
            if (enemiesNear > alliesNear + 1) b |= 1u << 12;
            if (m.Tier >= 4) b |= 1u << 13;
            if (siege) b |= 1u << 14;
            if (((w.Tick >> 4) & 1) != 0) b |= 1u << 15;
            if (seat < 2 && w.Trails[seat][y * W + x] > 0) b |= 1u << 16;
            if (ctx.Flock == 0) b |= 1u << 17;
            if (wild) b |= 1u << 18;
            if (m.Tier == 1) b |= 1u << 19;
            return b;
        }

        static void Think(World w, int i)
        {
            var S = w.S;
            ref var m = ref w.Motes[i];
            int off = i * C;
            var Wt = S.Weights;
            for (int k = 0; k < C; k++) Wt[off + k] = 0;
            Wt[off + (int)Channel.Momentum] = 2;
            Wt[off + (int)Channel.Noise] = 1;
            Wt[off + (int)Channel.Separation] = 1;
            int temp = Jev.BaseTemperature;
            var flags = BrainFlags.None;
            byte pattern = 0;
            uint sensors = S.Sensors[i];
            var prog = w.GetProgram(m.Program);
            if (prog != null && prog.Kind == ProgramKind.Unit)
            {
                foreach (var chip in prog.Chips)
                {
                    Argot.Unpack(chip, out uint mask, out uint want, out Op op, out byte arg, out sbyte gain);
                    if ((sensors & mask) != want) continue;
                    int g = gain;
                    switch (op)
                    {
                        case Op.Seek: Wt[off + (int)Argot.FieldChannel(arg)] += 2 * g; break;
                        case Op.Avoid: Wt[off + (int)Argot.FieldChannel(arg)] -= 2 * g; break;
                        case Op.Flock:
                            Wt[off + (int)Channel.Cohesion] += g; Wt[off + (int)Channel.Align] += g; Wt[off + (int)Channel.Separation] += g;
                            break;
                        case Op.Spread:
                            Wt[off + (int)Channel.Separation] += 2 * g;
                            if (pattern == 0) pattern = 1;
                            Wt[off + (int)Channel.Pattern] += g;
                            break;
                        case Op.Form: pattern = arg; Wt[off + (int)Channel.Pattern] += 2 * g; break;
                        case Op.Merge: flags |= BrainFlags.Merge; Wt[off + (int)Channel.Cohesion] += g; break;
                        case Op.Split: flags |= BrainFlags.Split; break;
                        case Op.Mine: flags |= BrainFlags.Mine; Wt[off + (int)Channel.Scrap] += 2 * g; break;
                        case Op.Hold: flags |= BrainFlags.Hold; break;
                        case Op.Wander: temp += 6 * g; Wt[off + (int)Channel.Noise] += g; break;
                        case Op.Infect: flags |= BrainFlags.Infect; break;
                        case Op.Mark: flags |= BrainFlags.Mark; break;
                        case Op.Holdfire: flags |= BrainFlags.Holdfire; break;
                    }
                }
            }
            if (m.Has(MoteFlags.HoldCmd)) flags |= BrainFlags.Hold;
            if (m.Has(MoteFlags.MergeCmd)) { flags |= BrainFlags.Merge; Wt[off + (int)Channel.Cohesion] += 2; }
            if (m.Has(MoteFlags.SplitCmd)) flags |= BrainFlags.Split;
            if (m.Override > 0)
            {
                for (int k = 0; k < C; k++) Wt[off + k] = 0;
                Wt[off + (int)Channel.Order] = 6; Wt[off + (int)Channel.Separation] = 1; Wt[off + (int)Channel.Momentum] = 1;
                flags &= ~(BrainFlags.Hold | BrainFlags.Mine | BrainFlags.Mark);
                temp = 4;
            }
            if (m.Seat == Rules.NoiseSeat) flags &= ~(BrainFlags.Mine | BrainFlags.Mark);
            S.Brain[i] = (byte)flags;
            S.Pattern[i] = pattern;
            S.Temp[i] = (byte)Math.Min(255, temp);
        }

        static bool Has(World w, int i, BrainFlags f) => (w.S.Brain[i] & (byte)f) != 0;

        // ================================================================== combat

        static void Combat(World w, int n, StepLog log)
        {
            var S = w.S; var map = w.Map; int W = map.W;
            Array.Clear(S.Dmg, 0, n);
            for (int i = 0; i < n; i++) { S.InfectSeat[i] = 255; S.LastHit[i] = 255; }
            Span<int> coreDmg = stackalloc int[2];
            Span<byte> coreLast = stackalloc byte[2];

            for (int i = 0; i < n; i++)
            {
                ref var m = ref w.Motes[i];
                if (m.Cooldown > 0 || Has(w, i, BrainFlags.Holdfire)) continue;
                int r = Rules.Range(m.Tier);
                int best = -1, bd = int.MaxValue;
                int y0 = Math.Max(0, m.Y - r), y1 = Math.Min(map.H - 1, m.Y + r), x0 = Math.Max(0, m.X - r), x1 = Math.Min(W - 1, m.X + r);
                for (int yy = y0; yy <= y1; yy++)
                    for (int xx = x0; xx <= x1; xx++)
                    {
                        int o = S.Occ[yy * W + xx] - 1;
                        if (o < 0 || o == i) continue;
                        ref var om = ref w.Motes[o];
                        if (om.Seat == m.Seat) continue;
                        int d = Math.Max(Math.Abs(xx - m.X), Math.Abs(yy - m.Y));
                        if (best < 0 || d < bd || (d == bd && (om.Hp < w.Motes[best].Hp || (om.Hp == w.Motes[best].Hp && om.Id < w.Motes[best].Id))))
                        { best = o; bd = d; }
                    }
                bool infect = Has(w, i, BrainFlags.Infect) && m.Seat < 2;
                int dmg = Rules.Damage(m.Tier);
                if (infect) dmg = Math.Max(1, dmg / 2);
                if (best >= 0)
                {
                    ref var t = ref w.Motes[best];
                    Hit(w, best, dmg, m.Seat, infect && m.Tier >= t.Tier ? m.Program : 0u);
                    bool splash = m.Tier >= Rules.SplashTier;
                    if (splash)
                        for (int k = 0; k < 8; k++)
                        {
                            int sx = t.X + Dir8X[k], sy = t.Y + Dir8Y[k];
                            if (!map.In(sx, sy)) continue;
                            int o = S.Occ[sy * W + sx] - 1;
                            if (o < 0 || w.Motes[o].Seat == m.Seat) continue;
                            Hit(w, o, Math.Max(1, dmg / 2), m.Seat, 0u);
                        }
                    m.Cooldown = (byte)Rules.Cooldown(m.Tier);
                    m.Flags |= MoteFlags.Fired;
                    log?.Shots.Add(StepLog.Shot(m.Y * W + m.X, t.Y * W + t.X, m.Seat, splash, infect, false));
                    continue;
                }
                // no unit in range: hit a core
                for (int s = 0; s < 2; s++)
                {
                    if (s == m.Seat || !map.HasCore[s] || !w.Seats[s].Alive) continue;
                    if (map.CoreDist(s, m.X, m.Y) > r) continue;
                    coreDmg[s] += Math.Max(1, Rules.Damage(m.Tier) - Rules.CoreArmor);
                    coreLast[s] = m.Seat;
                    m.Cooldown = (byte)Rules.Cooldown(m.Tier);
                    m.Flags |= MoteFlags.Fired;
                    log?.Shots.Add(StepLog.Shot(m.Y * W + m.X, map.Idx(map.CoreX[s], map.CoreY[s]), m.Seat, false, false, true));
                    break;
                }
            }

            // cores defend themselves
            for (int s = 0; s < 2; s++)
            {
                var seat = w.Seats[s];
                if (!map.HasCore[s] || !seat.Alive) continue;
                if (seat.CoreCooldown > 0) { seat.CoreCooldown--; continue; }
                int cx = map.CoreX[s], cy = map.CoreY[s], r = Rules.CoreRange + 1;
                int best = -1, bd = int.MaxValue;
                for (int yy = Math.Max(0, cy - r); yy <= Math.Min(map.H - 1, cy + r); yy++)
                    for (int xx = Math.Max(0, cx - r); xx <= Math.Min(W - 1, cx + r); xx++)
                    {
                        int o = S.Occ[yy * W + xx] - 1;
                        if (o < 0 || w.Motes[o].Seat == s) continue;
                        int d = Map.Cheb(xx, yy, cx, cy);
                        if (best < 0 || d < bd || (d == bd && w.Motes[o].Id < w.Motes[best].Id)) { best = o; bd = d; }
                    }
                if (best < 0) continue;
                Hit(w, best, Rules.CoreDamage, (byte)s, 0u);
                seat.CoreCooldown = Rules.CoreCooldown;
                log?.Shots.Add(StepLog.Shot(map.Idx(cx, cy), w.Motes[best].Y * W + w.Motes[best].X, s, false, false, true));
            }

            // simultaneous resolution
            for (int s = 0; s < 2; s++)
            {
                if (coreDmg[s] == 0) continue;
                w.Seats[s].CoreHp = Math.Max(0, w.Seats[s].CoreHp - coreDmg[s]);
            }
            for (int j = 0; j < n; j++)
            {
                if (S.Dmg[j] == 0) continue;
                ref var t = ref w.Motes[j];
                t.Hp -= S.Dmg[j];
                if (t.Hp > 0) continue;
                int cell = t.Y * W + t.X;
                byte killer = S.LastHit[j];
                if (S.InfectSeat[j] != 255 && S.InfectSeat[j] != t.Seat)
                {
                    // rewritten, not destroyed
                    if (t.Seat < 2) w.Seats[t.Seat].Lost += t.Value;
                    w.Seats[S.InfectSeat[j]].Killed += t.Value;
                    t.Seat = S.InfectSeat[j];
                    t.Program = S.InfectProgram[j];
                    t.Hp = Math.Max(1, Rules.MaxHp(t.Tier) / 2);
                    t.Flags = MoteFlags.None; t.Override = 0; t.Carry = 0; t.Lonely = 0;
                    log?.Converts.Add(cell | (t.Seat << 12));
                    continue;
                }
                Kill(w, j, killer, log);
            }
        }

        static void Hit(World w, int target, int dmg, byte attackerSeat, uint infectProgram)
        {
            var S = w.S;
            ref var t = ref w.Motes[target];
            S.Dmg[target] += Math.Max(1, dmg - Rules.Armor(t.Tier));
            S.LastHit[target] = attackerSeat;
            if (infectProgram != 0 && S.InfectSeat[target] == 255) { S.InfectSeat[target] = attackerSeat; S.InfectProgram[target] = infectProgram; }
        }

        static void Kill(World w, int j, byte killer, StepLog log)
        {
            var S = w.S;
            ref var t = ref w.Motes[j];
            int cell = t.Y * w.W + t.X;
            S.Dead[j] = true;
            if (S.Occ[cell] == j + 1) S.Occ[cell] = 0;
            if (t.Seat < 2) w.Seats[t.Seat].Lost += t.Value;
            if (killer < 2)
            {
                w.Seats[killer].Killed += t.Value;
                if (t.Seat == Rules.NoiseSeat) w.Seats[killer].Scrap += t.Value * Rules.FeralBountyPct / 100;
            }
            log?.Deaths.Add(cell | (t.Seat << 12) | (t.Tier << 14));
        }

        // ================================================================== movement

        static void Move(World w, int n, StepLog log)
        {
            var S = w.S; var map = w.Map; int W = map.W;
            for (int i = 0; i < n; i++)
            {
                if (S.Dead[i]) continue;
                ref var m = ref w.Motes[i];
                if (m.Has(MoteFlags.Fired) || Has(w, i, BrainFlags.Hold)) { MarkTrail(w, i); continue; }
                int period = Rules.MovePeriod(m.Tier);
                if (period > 1 && (w.Tick + (int)(m.Id % (uint)period)) % period != 0) { MarkTrail(w, i); continue; }
                int here = m.Y * W + m.X, exit = map.Twin[here];
                if (exit >= 0 && S.Occ[exit] == 0)
                {
                    // waiting on a portal: go through the moment the far side is clear
                    S.Occ[here] = 0;
                    m.X = (short)(exit % W); m.Y = (short)(exit / W);
                    S.Occ[exit] = i + 1;
                    log?.Teleports.Add(here | (exit << 12));
                    MarkTrail(w, i);
                    continue;
                }
                int k = Jev.Classify(w, i, S.Weights, i * C, S.Temp[i], S.Pattern[i], null, null);
                if (k != 4)
                {
                    int dx = k % 3 - 1, dy = k / 3 - 1;
                    int from = m.Y * W + m.X;
                    S.Occ[from] = 0;
                    m.X += (short)dx; m.Y += (short)dy;
                    m.Hx = (sbyte)dx; m.Hy = (sbyte)dy;
                    int to = m.Y * W + m.X;
                    int twin = map.Twin[to];
                    if (twin >= 0 && S.Occ[twin] == 0)
                    {
                        log?.Teleports.Add(to | (twin << 12));
                        m.X = (short)(twin % W); m.Y = (short)(twin / W);
                        to = twin;
                    }
                    S.Occ[to] = i + 1;
                }
                if (m.Override > 0 && m.Has(MoteFlags.HasOrder) && Map.Cheb(m.X, m.Y, m.Ox, m.Oy) <= 1) m.Override = 0;
                MarkTrail(w, i);
            }
        }

        static void MarkTrail(World w, int i)
        {
            ref var m = ref w.Motes[i];
            if (m.Seat >= 2 || !Has(w, i, BrainFlags.Mark)) return;
            var tr = w.Trails[m.Seat];
            int c = m.Y * w.W + m.X;
            tr[c] = (byte)Math.Min(255, tr[c] + Rules.TrailMark);
            w.S.TrailLive[m.Seat] = true;
        }

        // ================================================================== economy

        static void Economy(World w, int n)
        {
            var S = w.S; var map = w.Map;
            for (int i = 0; i < n; i++)
            {
                if (S.Dead[i]) continue;
                ref var m = ref w.Motes[i];
                if (m.Seat >= 2) continue;
                int cap = Rules.Capacity(m.Tier);
                if (Has(w, i, BrainFlags.Mine) && m.Carry < cap && ((w.Tick + (int)m.Id) & 1) == 0)
                {
                    for (int k = 0; k < w.Scrap.Length; k++)
                    {
                        ref var node = ref w.Scrap[k];
                        if (node.Amount <= 0 || Map.Cheb(m.X, m.Y, node.X, node.Y) > 1) continue;
                        node.Amount--; m.Carry++;
                        break;
                    }
                }
                if (m.Carry > 0 && map.HasCore[m.Seat] && map.CoreDist(m.Seat, m.X, m.Y) <= 1)
                {
                    var seat = w.Seats[m.Seat];
                    seat.Scrap += m.Carry; seat.Mined += m.Carry; m.Carry = 0;
                }
            }
        }

        // ================================================================== 2048

        static void MergePairs(World w, int n, StepLog log)
        {
            var S = w.S; var map = w.Map; int W = map.W;
            for (int i = 0; i < n; i++)
            {
                if (S.Dead[i] || S.Used[i]) continue;
                ref var m = ref w.Motes[i];
                if (m.Tier >= Rules.MaxTier || !Has(w, i, BrainFlags.Merge)) continue;
                int best = -1;
                for (int k = 0; k < 8; k++)
                {
                    int nx = m.X + Dir8X[k], ny = m.Y + Dir8Y[k];
                    if (!map.In(nx, ny)) continue;
                    int o = S.Occ[ny * W + nx] - 1;
                    if (o < 0 || o >= n || S.Dead[o] || S.Used[o]) continue;
                    ref var om = ref w.Motes[o];
                    if (om.Seat != m.Seat || om.Tier != m.Tier || !Has(w, o, BrainFlags.Merge)) continue;
                    if (best < 0 || om.Id < w.Motes[best].Id) best = o;
                }
                if (best < 0) continue;
                ref var p = ref w.Motes[best];
                m.Tier++;
                m.Hp = Math.Min(Rules.MaxHp(m.Tier), m.Hp + p.Hp);
                m.Carry = (byte)Math.Min(Rules.Capacity(m.Tier), m.Carry + p.Carry);
                m.Flags &= ~MoteFlags.MergeCmd;
                S.Used[i] = true; S.Used[best] = true; S.Dead[best] = true;
                int pc = p.Y * W + p.X;
                if (S.Occ[pc] == best + 1) S.Occ[pc] = 0;
                log?.Merges.Add((m.Y * W + m.X) | (m.Tier << 12) | (m.Seat << 16));
            }
        }

        static void Splits(World w, int n)
        {
            var S = w.S; var map = w.Map; int W = map.W;
            for (int i = 0; i < n; i++)
            {
                if (S.Dead[i] || S.Used[i]) continue;
                if (w.Motes[i].Tier < 2 || !(Has(w, i, BrainFlags.Split))) continue;
                int start = (int)((w.Motes[i].Id + (uint)w.Tick) & 7);
                for (int kk = 0; kk < 8; kk++)
                {
                    int k = (start + kk) & 7;
                    int x = w.Motes[i].X, y = w.Motes[i].Y;
                    if (!map.CanStep(x, y, Dir8X[k], Dir8Y[k])) continue;
                    int cell = (y + Dir8Y[k]) * W + x + Dir8X[k];
                    if (S.Occ[cell] != 0) continue;
                    var child = w.Motes[i];
                    ref var m = ref w.Motes[i];
                    m.Tier--;
                    child.Tier = m.Tier;
                    int half = m.Hp / 2;
                    child.Hp = Math.Max(1, half);
                    m.Hp = Math.Max(1, m.Hp - half);
                    child.Carry = (byte)(m.Carry / 2);
                    m.Carry = (byte)(m.Carry - child.Carry);
                    m.Flags &= ~MoteFlags.SplitCmd;
                    child.Flags = m.Flags & ~(MoteFlags.Fired | MoteFlags.MergeCmd);
                    child.Id = w.NextId++;
                    child.X = (short)(x + Dir8X[k]); child.Y = (short)(y + Dir8Y[k]);
                    child.Hx = Dir8X[k]; child.Hy = Dir8Y[k];
                    child.Lonely = 0;
                    w.AddMote(child);
                    S.Occ[cell] = w.MoteCount;
                    break;
                }
            }
        }

        // ================================================================== coherence

        static void Coherence(World w, int n, StepLog log)
        {
            if (w.Tick % Rules.CoherenceEvery != 0) return;
            var S = w.S; var map = w.Map;
            for (int i = 0; i < n; i++)
            {
                if (S.Dead[i]) continue;
                ref var m = ref w.Motes[i];
                if (m.Seat >= 2) continue;
                ref var ctx = ref S.Ctx[i];
                bool inBlanket = map.HasCore[m.Seat] && map.CoreDist(m.Seat, m.X, m.Y) <= Rules.BlanketRadius;
                int max = Rules.MaxHp(m.Tier);
                if (ctx.Near == 0 && !inBlanket)
                {
                    m.Lonely = (byte)Math.Min(255, m.Lonely + Rules.CoherenceEvery);
                    if (m.Lonely >= Rules.LonelyGrace)
                    {
                        m.Hp -= Math.Max(1, max / 20);
                        if (m.Hp <= 0) Kill(w, i, 255, log);
                    }
                }
                else
                {
                    m.Lonely = 0;
                    if (ctx.Flock >= Rules.RegenAllies && m.Hp < max) m.Hp = Math.Min(max, m.Hp + Math.Max(1, max / 40));
                }
            }
        }

        // ================================================================== bookkeeping

        static void Compact(World w)
        {
            var S = w.S;
            int j = 0;
            for (int i = 0; i < w.MoteCount; i++)
            {
                if (i < S.Cap && S.Dead[i]) continue;
                if (i != j) w.Motes[j] = w.Motes[i];
                j++;
            }
            w.MoteCount = j;
        }

        static void Produce(World w, StepLog log)
        {
            if (w.Tick % Rules.SpawnEvery != 0) return;
            for (int s = 0; s < 2; s++)
            {
                var seat = w.Seats[s];
                if (!seat.Alive || !seat.Auto) continue;
                TrySpawn(w, s, 1, log);
            }
        }

        public static int TrySpawn(World w, int s, int count, StepLog log)
        {
            var seat = w.Seats[s];
            if (!seat.Alive || !w.Map.HasCore[s]) return 0;
            var prog = w.GetProgram(seat.Loadout[Math.Min(seat.Production, (byte)(Rules.LoadoutSlots - 1))]);
            if (prog == null) return 0;
            int made = 0;
            int have = 0;
            for (int i = 0; i < w.MoteCount; i++) if (w.Motes[i].Seat == s) have++;
            for (int k = 0; k < count; k++)
            {
                if (seat.Scrap < prog.SpawnCost || have >= Rules.MaxMotesPerSeat) break;
                int cell = SpawnMote(w, s, prog.Id, 1);
                if (cell < 0) break;
                seat.Scrap -= prog.SpawnCost;
                have++; made++;
                log?.Spawns.Add(cell | (s << 12));
            }
            return made;
        }

        /// <summary>Place a new mote on a free cell around a seat's core. Returns the cell or -1.</summary>
        internal static int SpawnMote(World w, int s, uint program, int tier)
        {
            var map = w.Map;
            int cx = map.CoreX[s], cy = map.CoreY[s];
            for (int d = 2; d <= 5; d++)
            {
                int ring = 8 * d;
                int start = (w.Tick * 7 + w.MoteCount) % ring;
                for (int kk = 0; kk < ring; kk++)
                {
                    int k = (start + kk) % ring;
                    RingCell(d, k, out int dx, out int dy);
                    int x = cx + dx, y = cy + dy;
                    if (!map.Passable(x, y)) continue;
                    int c = map.Idx(x, y);
                    if (w.S.Occ[c] != 0 || map.Twin[c] >= 0) continue;
                    AddFresh(w, (byte)s, program, tier, x, y);
                    return c;
                }
            }
            return -1;
        }

        /// <summary>Put a mote on a specific free cell (level setup).</summary>
        public static bool PlaceMote(World w, int seat, uint program, int tier, int x, int y)
        {
            if (w.S == null) Prepare(w);
            if (!w.Map.Passable(x, y) || w.S.Occ[y * w.W + x] != 0) return false;
            AddFresh(w, (byte)seat, program, Math.Max(1, Math.Min(Rules.MaxTier, tier)), x, y);
            return true;
        }

        static void AddFresh(World w, byte seat, uint program, int tier, int x, int y)
        {
            var m = new Mote
            {
                Id = w.NextId++, Seat = seat, Tier = (byte)tier, X = (short)x, Y = (short)y,
                Hp = Rules.MaxHp(tier), Program = program,
            };
            w.AddMote(m);
            w.S.Occ[y * w.W + x] = w.MoteCount;
        }

        static void RingCell(int d, int k, out int dx, out int dy)
        {
            int side = 2 * d;
            int s = k / side, t = k % side;
            switch (s)
            {
                case 0: dx = -d + t; dy = -d; break;
                case 1: dx = d; dy = -d + t; break;
                case 2: dx = d - t; dy = d; break;
                default: dx = -d; dy = d - t; break;
            }
        }

        static void Ferals(World w, StepLog log)
        {
            bool on = w.Mode == WorldMode.Duel || (w.Chamber != null && w.Chamber.Ferals);
            int every = w.Chamber != null && w.Chamber.WellEvery > 0 ? w.Chamber.WellEvery : Rules.WellEvery;
            if (!on || w.Tick % every != every / 2 || w.Map.Wells.Count == 0) return;
            int ferals = 0;
            for (int i = 0; i < w.MoteCount; i++) if (w.Motes[i].Seat == Rules.NoiseSeat) ferals++;
            int cap = w.Chamber != null ? w.Chamber.MaxFerals : Rules.MaxFerals;
            foreach (var well in w.Map.Wells)
            {
                if (ferals >= cap) break;
                uint roll = Rng.Roll(w.Seed, w.Tick, (uint)well, 31);
                int tier = 1 + (roll % 4 == 0 ? 1 : 0) + Math.Min(2, w.Tick / 6000);
                if (w.Chamber != null) tier = Math.Max(tier, w.Chamber.FeralTier);
                int wx = well % w.W, wy = well / w.W;
                int cell = -1;
                for (int d = 0; d <= 2 && cell < 0; d++)
                    for (int k = 0; k < Math.Max(1, 8 * d) && cell < 0; k++)
                    {
                        int dx = 0, dy = 0;
                        if (d > 0) RingCell(d, k, out dx, out dy);
                        int x = wx + dx, y = wy + dy;
                        if (w.Map.Passable(x, y) && w.S.Occ[w.Map.Idx(x, y)] == 0) cell = w.Map.Idx(x, y);
                    }
                if (cell < 0) continue;
                uint feral = w.Chamber != null && w.Chamber.FeralProgram != 0 ? w.Chamber.FeralProgram : Rules.FeralProgramBase + roll / 7 % (uint)Rules.FeralPrograms;
                AddFresh(w, Rules.NoiseSeat, feral, tier, cell % w.W, cell / w.W);
                ferals++;
                log?.Spawns.Add(cell | (Rules.NoiseSeat << 12));
            }
        }

        static void Upkeep(World w)
        {
            if (w.Tick % Rules.PassiveIncomeEvery == 0)
                for (int s = 0; s < 2; s++) if (w.Seats[s].Alive) w.Seats[s].Scrap++;
            if (w.Tick % Rules.TrailDecayEvery == 0)
                for (int s = 0; s < 2; s++)
                {
                    if (!w.S.TrailLive[s]) continue;
                    var tr = w.Trails[s];
                    bool live = false;
                    for (int i = 0; i < tr.Length; i++) if (tr[i] > 0) { tr[i]--; live = true; }
                    w.S.TrailLive[s] = live;
                }
            for (int i = 0; i < w.MoteCount; i++) if (w.Motes[i].Override > 0) w.Motes[i].Override--;
        }

        static void CheckWin(World w)
        {
            if (w.Mode == WorldMode.Chamber) { Chambers.Check(w); return; }
            var a = w.Seats[0]; var b = w.Seats[1];
            bool d0 = a.HasCore && a.CoreHp <= 0, d1 = b.HasCore && b.CoreHp <= 0;
            if (d0 && d1) { w.Winner = Rules.Draw; return; }
            if (d0) { w.Winner = 1; return; }
            if (d1) { w.Winner = 0; return; }
            for (int s = 0; s < 2; s++)
            {
                bool sing = false;
                for (int i = 0; i < w.MoteCount; i++) if (w.Motes[i].Seat == s && w.Motes[i].Tier >= Rules.MaxTier) { sing = true; break; }
                w.Seats[s].SingularityTicks = sing ? w.Seats[s].SingularityTicks + 1 : 0;
                if (w.Seats[s].SingularityTicks >= Rules.SingularityHold) { w.Winner = (byte)s; return; }
            }
            if (w.Tick + 1 >= Rules.TimeLimit)
            {
                long v0 = a.Scrap + a.CoreHp / 10, v1 = b.Scrap + b.CoreHp / 10;
                for (int i = 0; i < w.MoteCount; i++)
                {
                    if (w.Motes[i].Seat == 0) v0 += w.Motes[i].Value;
                    else if (w.Motes[i].Seat == 1) v1 += w.Motes[i].Value;
                }
                w.Winner = v0 == v1 ? Rules.Draw : v0 > v1 ? (byte)0 : (byte)1;
            }
        }

        internal static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        // ================================================================== inspection

        public struct Analysis
        {
            public bool Valid;
            public uint Sensors;
            public int[] Weights;
            public BrainFlags Flags;
            public int Temperature;
            public int[] Scores;
            public int[] Permille;
            public int Pick;
            public List<int> FiredRules;
        }

        /// <summary>
        /// Explain one mote's next decision: active sensors, the rules that fired, the weights they built and
        /// the softmax over its 9 candidate tiles. Read-only; safe to call on a client mirror every frame.
        /// </summary>
        public static Analysis Analyze(World w, uint moteId)
        {
            var a = new Analysis();
            var S = Prepare(w);
            int i = w.FindMote(moteId);
            if (i < 0) return a;
            ComputeFields(w);
            S.Sensors[i] = Sense(w, i);
            Think(w, i);
            a.Valid = true;
            a.Sensors = S.Sensors[i];
            a.Weights = new int[C];
            Array.Copy(S.Weights, i * C, a.Weights, 0, C);
            a.Flags = (BrainFlags)S.Brain[i];
            a.Temperature = S.Temp[i];
            a.Scores = new int[Jev.Options];
            a.Permille = new int[Jev.Options];
            a.Pick = Jev.Classify(w, i, S.Weights, i * C, S.Temp[i], S.Pattern[i], a.Scores, a.Permille);
            a.FiredRules = new List<int>();
            var p = w.GetProgram(w.Motes[i].Program);
            if (p != null)
                for (int r = 0; r < p.Chips.Length; r++)
                    if (Argot.Matches(p.Chips[r], a.Sensors)) a.FiredRules.Add(r);
            return a;
        }
    }
}
