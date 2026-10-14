#nullable disable
using System;
using System.Collections.Generic;

namespace Argmax.Sim
{
    /// <summary>
    /// One macro decision for a seat. Produced by an Argot metaprogram, by the ML-Agents commander,
    /// or by a human clicking buttons: three interchangeable authors behind one interface.
    /// Each field is also one discrete action branch of the commander policy.
    /// </summary>
    public struct MetaDecision
    {
        public int Produce; // 0 keep, 1..6 loadout slot
        public int Spawn;   // 0 none, 1 one, 2 four, 3 ten
        public int Beacon;  // 0 keep, 1 home, 2 front, 3 enemy, 4 scrap, 5 wild, 6 clear
        public int Army;    // 0 keep, 1 attack, 2 retreat, 3 merge, 4 split, 5 hold, 6 free

        public override string ToString() =>
            "produce " + Produce + " spawn " + Spawn + " beacon " + Argot.Places[Beacon] + " army " + Argot.ArmyOrders[Army];
    }

    public static class Meta
    {
        public static readonly int[] Branches = { 7, 4, 7, 7 };
        static readonly int[] SpawnCounts = { 0, 1, 4, 10 };

        public static uint Sensors(World w, int seat)
        {
            if (w.S == null) Sim.Prepare(w);
            var T = w.S.Tally;
            var me = w.Seats[seat];
            int foe = w.Other(seat);
            ref var t = ref T[seat];
            ref var e = ref T[foe];
            uint b = 0;
            if (me.Scrap >= 40) b |= 1u << 0;
            if (me.Scrap < 8) b |= 1u << 1;
            if (e.ArmyValue * 4 > t.ArmyValue * 5 + 8) b |= 1u << 2;
            if (t.Value * 2 > e.Value * 3 + 8) b |= 1u << 3;
            if (t.EnemiesNearCore >= 3) b |= 1u << 4;
            if (t.Workers >= 6) b |= 1u << 5;
            if (e.MaxTier >= 5) b |= 1u << 6;
            if (t.MaxTier >= 5) b |= 1u << 7;
            if (((w.Tick >> 6) & 1) != 0) b |= 1u << 8;
            if (w.Tick < 1200) b |= 1u << 9;
            if (w.Tick > 6000) b |= 1u << 10;
            if (t.MaxTier >= 10) b |= 1u << 11;
            if (e.MaxTier >= 10) b |= 1u << 12;
            if (me.HasCore && me.CoreHp * 2 < Rules.CoreHp) b |= 1u << 13;
            if (t.FeralsNearCore >= 2) b |= 1u << 14;
            if (t.Count >= 60) b |= 1u << 15;
            if (t.SiegeMotes >= 5) b |= 1u << 16;
            if (t.ArmyValue >= 40) b |= 1u << 17;
            var fs = w.Seats[foe];
            if (fs.HasCore && fs.CoreHp * 2 < Rules.CoreHp) b |= 1u << 18;
            if (me.BeaconOn) b |= 1u << 19;
            return b;
        }

        /// <summary>Evaluate a metaprogram. Later matching rules override earlier ones, like a cascade.</summary>
        public static MetaDecision Decide(World w, int seat, Program p)
        {
            var d = new MetaDecision();
            if (p == null) return d;
            uint sensors = Sensors(w, seat);
            var me = w.Seats[seat];
            foreach (var chip in p.Chips)
            {
                if (!Argot.Matches(chip, sensors)) continue;
                Argot.Unpack(chip, out _, out _, out Op op, out byte arg, out _);
                switch (op)
                {
                    case Op.Produce:
                        if (arg >= 1 && arg <= Rules.LoadoutSlots) d.Produce = arg;
                        else if (arg >= 16)
                        {
                            uint pid = (uint)(arg - 16);
                            for (int s = 0; s < Rules.LoadoutSlots; s++) if (me.Loadout[s] == pid) { d.Produce = s + 1; break; }
                        }
                        break;
                    case Op.Spawn: d.Spawn = arg; break;
                    case Op.Beacon: d.Beacon = arg; break;
                    case Op.Army: d.Army = arg; break;
                }
            }
            return d;
        }

        /// <summary>Turn a decision into commands and execute them immediately.</summary>
        public static void Apply(World w, int seat, MetaDecision d)
        {
            var cmds = ToCommands(w, seat, d);
            foreach (var c in cmds) Sim.Apply(w, c);
        }

        /// <summary>Same decision as wire commands (what a networked AI pilot sends through reducers).</summary>
        public static List<Cmd> ToCommands(World w, int seat, MetaDecision d)
        {
            var list = new List<Cmd>(4);
            var me = w.Seats[seat];
            var map = w.Map;
            byte s = (byte)seat;
            if (d.Produce >= 1 && d.Produce <= Rules.LoadoutSlots && me.Production != d.Produce - 1)
                list.Add(new Cmd { Seat = s, Kind = CmdKind.Produce, Arg = (uint)(d.Produce - 1) });
            if (d.Spawn > 0) list.Add(new Cmd { Seat = s, Kind = CmdKind.Spawn, Arg = (uint)SpawnCounts[Math.Min(d.Spawn, 3)] });
            if (d.Beacon == 6) { if (me.BeaconOn) list.Add(new Cmd { Seat = s, Kind = CmdKind.ClearBeacon }); }
            else if (d.Beacon > 0)
            {
                int cell = Place(w, seat, d.Beacon);
                if (cell >= 0 && (!me.BeaconOn || me.BeaconX != cell % map.W || me.BeaconY != cell / map.W))
                    list.Add(new Cmd { Seat = s, Kind = CmdKind.Beacon, X = (short)(cell % map.W), Y = (short)(cell / map.W) });
            }
            if (d.Army > 0)
            {
                var ids = ArmyIds(w, seat);
                if (ids.Length > 0)
                {
                    var c = new Cmd { Seat = s, Ids = ids };
                    switch (d.Army)
                    {
                        case 1:
                            c.Kind = CmdKind.Attack;
                            int target = -1;
                            // the beacon wins if it is (or is about to be) placed
                            for (int k = 0; k < list.Count; k++) if (list[k].Kind == CmdKind.Beacon) target = map.Idx(list[k].X, list[k].Y);
                            if (target < 0 && me.BeaconOn && d.Beacon != 6) target = map.Idx(me.BeaconX, me.BeaconY);
                            if (target < 0) target = Place(w, seat, 3);
                            if (target < 0) target = Place(w, seat, 1);
                            if (target < 0) { c.Kind = CmdKind.None; break; }
                            c.X = (short)(target % map.W); c.Y = (short)(target / map.W);
                            break;
                        case 2:
                            c.Kind = CmdKind.Move;
                            int home = Place(w, seat, 1);
                            if (home < 0) { c.Kind = CmdKind.None; break; }
                            c.X = (short)(home % map.W); c.Y = (short)(home / map.W);
                            break;
                        case 3: c.Kind = CmdKind.Merge; break;
                        case 4: c.Kind = CmdKind.Split; break;
                        case 5: c.Kind = CmdKind.Hold; break;
                        case 6: c.Kind = CmdKind.Stop; break;
                    }
                    if (c.Kind != CmdKind.None) list.Add(c);
                }
            }
            return list;
        }

        public static uint[] ArmyIds(World w, int seat)
        {
            var ids = new List<uint>();
            for (int i = 0; i < w.MoteCount; i++)
            {
                ref var m = ref w.Motes[i];
                if (m.Seat != seat) continue;
                var p = w.GetProgram(m.Program);
                if (p != null && p.Worker) continue;
                ids.Add(m.Id);
            }
            return ids.ToArray();
        }

        /// <summary>Semantic places: what "beacon front" or "beacon scrap" means on this map, right now.</summary>
        public static int Place(World w, int seat, int place)
        {
            var map = w.Map;
            if (!map.HasCore[seat]) return -1;
            int cx = map.CoreX[seat], cy = map.CoreY[seat];
            switch (place)
            {
                case 1:
                {
                    int hx = cx + Math.Sign(map.W / 2 - cx) * 5, hy = cy + Math.Sign(map.H / 2 - cy) * 5;
                    return map.NearestPassable(hx, hy, 4);
                }
                case 2:
                {
                    long sx = 0, sy = 0, sv = 0;
                    for (int i = 0; i < w.MoteCount; i++)
                    {
                        ref var m = ref w.Motes[i];
                        if (m.Seat != seat) continue;
                        var p = w.GetProgram(m.Program);
                        if (p != null && p.Worker) continue;
                        sx += (long)m.X * m.Value; sy += (long)m.Y * m.Value; sv += m.Value;
                    }
                    if (sv == 0) return Place(w, seat, 1);
                    return map.NearestPassable((int)(sx / sv), (int)(sy / sv), 4);
                }
                case 3:
                {
                    int foe = w.Other(seat);
                    if (!map.HasCore[foe]) return -1;
                    int ex = map.CoreX[foe], ey = map.CoreY[foe];
                    // a ring cell of their core, on our side
                    int bx = ex + Math.Sign(cx - ex) * 2, by = ey + Math.Sign(cy - ey) * 2;
                    return map.NearestPassable(bx, by, 3);
                }
                case 4:
                {
                    var home = map.Home(seat);
                    int best = -1, bd = int.MaxValue;
                    foreach (var n in w.Scrap)
                    {
                        if (n.Amount <= 0) continue;
                        int c = map.NearestPassable(n.X, n.Y, 1);
                        if (c < 0) continue;
                        int d = home[c] == Fields.Unreachable ? 9999 : home[c];
                        d -= Math.Min(n.Amount, 600) / 60; // prefer rich nodes a little
                        if (d < bd) { bd = d; best = c; }
                    }
                    return best;
                }
                case 5:
                {
                    int best = -1, bd = int.MaxValue;
                    foreach (var c in map.Wells)
                    {
                        int d = Map.Cheb(c % map.W, c / map.W, cx, cy);
                        if (d < bd) { bd = d; best = c; }
                    }
                    return best < 0 ? -1 : map.NearestPassable(best % map.W, best / map.W, 2);
                }
            }
            return -1;
        }
    }

    /// <summary>
    /// The ML-Agents commander's view of the world: a fixed-size float vector, mirrored for seat 1 so
    /// both sides of self-play see "me at the bottom-left". Lives in the shared sim so the gym and the
    /// networked AI pilot observe identically.
    /// </summary>
    public static class CommanderObs
    {
        public const int Grid = 8;
        public const int Size = 16 + 6 + 2 + 3 + 20 + Grid * Grid * 4;

        public static void Write(World w, int seat, float[] o)
        {
            if (w.S == null) Sim.Prepare(w);
            var T = w.S.Tally;
            var me = w.Seats[seat];
            int foe = w.Other(seat);
            var en = w.Seats[foe];
            int k = 0;
            ref var t = ref T[seat];
            ref var e = ref T[foe];
            ref var nz = ref T[Rules.NoiseSeat];
            o[k++] = Math.Min(me.Scrap, 400) / 400f;
            o[k++] = me.HasCore ? me.CoreHp / (float)Rules.CoreHp : 0f;
            o[k++] = Math.Min(t.Count, 200) / 200f;
            o[k++] = Math.Min(t.Value, 2048) / 2048f;
            o[k++] = Math.Min(t.ArmyValue, 2048) / 2048f;
            o[k++] = Math.Min(t.Workers, 30) / 30f;
            o[k++] = t.MaxTier / (float)Rules.MaxTier;
            o[k++] = me.SingularityTicks / (float)Rules.SingularityHold;
            o[k++] = me.BeaconOn ? 1f : 0f;
            o[k++] = me.Auto ? 1f : 0f;
            for (int s = 0; s < 6; s++) o[k++] = me.Production == s ? 1f : 0f;
            o[k++] = en.HasCore ? en.CoreHp / (float)Rules.CoreHp : 0f;
            o[k++] = Math.Min(e.Count, 200) / 200f;
            o[k++] = Math.Min(e.Value, 2048) / 2048f;
            o[k++] = Math.Min(e.ArmyValue, 2048) / 2048f;
            o[k++] = e.MaxTier / (float)Rules.MaxTier;
            o[k++] = en.SingularityTicks / (float)Rules.SingularityHold;
            o[k++] = Math.Min(nz.Count, Rules.MaxFerals) / (float)Rules.MaxFerals;
            o[k++] = Math.Min(nz.Value, 512) / 512f;
            o[k++] = w.Tick / (float)Rules.TimeLimit;
            o[k++] = w.Tick < 1200 ? 1f : 0f;
            o[k++] = w.Tick > 6000 ? 1f : 0f;
            uint ms = Meta.Sensors(w, seat);
            for (int b = 0; b < 20; b++) o[k++] = (ms & (1u << b)) != 0 ? 1f : 0f;

            int g0 = k;
            for (int i = 0; i < Grid * Grid * 4; i++) o[g0 + i] = 0f;
            var map = w.Map;
            bool flip = seat == 1;
            for (int i = 0; i < w.MoteCount; i++)
            {
                ref var m = ref w.Motes[i];
                int ch = m.Seat == seat ? 0 : m.Seat == Rules.NoiseSeat ? 2 : 1;
                o[g0 + Cell(map, m.X, m.Y, flip) * 4 + ch] += m.Value;
            }
            foreach (var n in w.Scrap) if (n.Amount > 0) o[g0 + Cell(map, n.X, n.Y, flip) * 4 + 3] += n.Amount;
            for (int i = 0; i < Grid * Grid * 4; i++)
            {
                float v = o[g0 + i];
                o[g0 + i] = v <= 0 ? 0f : (float)Math.Log(1 + v) / 7f; // log-scaled, ~1 at 1000 value
            }
            k += Grid * Grid * 4;
            if (k != Size) throw new InvalidOperationException("CommanderObs size mismatch: " + k);
        }

        static int Cell(Map map, int x, int y, bool flip)
        {
            if (flip) { x = map.W - 1 - x; y = map.H - 1 - y; }
            int gx = Math.Min(Grid - 1, x * Grid / map.W), gy = Math.Min(Grid - 1, y * Grid / map.H);
            return gy * Grid + gx;
        }

        /// <summary>Which actions make sense right now (ML-Agents discrete action masking).</summary>
        public static bool Allowed(World w, int seat, int branch, int action)
        {
            var me = w.Seats[seat];
            switch (branch)
            {
                case 0: return action == 0 || w.GetProgram(me.Loadout[action - 1]) != null;
                case 1:
                    if (action == 0) return true;
                    var p = w.GetProgram(me.Loadout[Math.Min(me.Production, (byte)5)]);
                    return p != null && me.Scrap >= p.SpawnCost;
                case 2: return action != 5 || w.Map.Wells.Count > 0;
                default: return true;
            }
        }
    }
}
