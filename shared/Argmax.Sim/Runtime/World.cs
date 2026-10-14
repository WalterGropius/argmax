#nullable disable
using System;
using System.Collections.Generic;

namespace Argmax.Sim
{
    /// <summary>Feature channels the Jev classifier scores every candidate tile on.</summary>
    public enum Channel : byte
    {
        Order, Enemy, Scrap, Home, Beacon, Core, Cohesion, Align, Separation, Momentum, Pattern, Trail, Noise,
        Count
    }

    [Flags]
    public enum MoteFlags : byte
    {
        None = 0,
        HasOrder = 1,
        MergeCmd = 2,
        SplitCmd = 4,
        HoldCmd = 8,
        Fired = 16,
    }

    /// <summary>One swarm unit. Plain data; the whole persistent state of a mote.</summary>
    public struct Mote
    {
        public uint Id;
        public byte Seat;
        public byte Tier;
        public short X, Y;
        public int Hp;
        public byte Carry;
        public byte Cooldown;
        public sbyte Hx, Hy;     // heading: last step taken
        public uint Program;
        public short Ox, Oy;     // order target
        public MoteFlags Flags;
        public byte Override;    // ticks of manual override left (MOVE command)
        public byte Lonely;      // decoherence counter

        public int Value => Rules.Value(Tier);
        public bool Has(MoteFlags f) => (Flags & f) != 0;
    }

    public enum SeatKind : byte { Empty, Human, Ai, Noise }

    public sealed class Seat
    {
        public SeatKind Kind;
        public int Scrap;
        public int CoreHp;
        public short CoreX, CoreY;
        public byte Production = 1;      // loadout slot auto-production draws from
        public bool Auto = true;
        public uint[] Loadout = new uint[Rules.LoadoutSlots];
        public uint Meta;                // metaprogram id, 0 = none
        public bool BeaconOn;
        public short BeaconX, BeaconY;
        public int SingularityTicks;
        public int Mined, Killed, Lost;
        public byte CoreCooldown;

        public bool HasCore => Kind == SeatKind.Human || Kind == SeatKind.Ai;
        public bool Alive => HasCore && CoreHp > 0;

        public Seat Clone()
        {
            var s = (Seat)MemberwiseClone();
            s.Loadout = (uint[])Loadout.Clone();
            return s;
        }
    }

    public struct ScrapNode
    {
        public short X, Y;
        public int Amount;
    }

    public enum ProgramKind : byte { Unit, Meta }

    /// <summary>A compiled Argot program. Immutable once created: edits make new programs.</summary>
    public sealed class Program
    {
        public uint Id;
        public string Name;
        public string Source;
        public ProgramKind Kind;
        public ulong[] Chips;
        public int RuleCount;
        public int Symbols;
        public bool Worker; // contains a `mine` rule: counts as economy, not army

        public int SpawnCost => Rules.MoteCost + Math.Max(0, RuleCount - Rules.FreeRules);
    }

    public enum CmdKind : byte
    {
        None = 0,
        Move = 1,        // manual override: walk there, ignore the program
        Attack = 2,      // set the order target, let the program decide how to get there
        Stop = 3,
        Hold = 4,
        Merge = 5,
        Split = 6,
        Reprogram = 7,   // Arg = program id
        Beacon = 8,      // X, Y
        ClearBeacon = 9,
        Produce = 10,    // Arg = loadout slot
        Auto = 11,       // Arg = 0/1
        Spawn = 12,      // Arg = count
        SetSlot = 13,    // X = slot, Arg = program id
        SetMeta = 14,    // Arg = metaprogram id (0 clears)
        Surrender = 15,
    }

    public struct Cmd
    {
        public byte Seat;
        public CmdKind Kind;
        public short X, Y;
        public uint Arg;
        public uint[] Ids;
    }

    public enum WorldMode : byte { Duel, Chamber }

    /// <summary>
    /// The complete simulation state of one match. Everything outside the "scratch" region
    /// is persistent and round-trips through SpacetimeDB rows; scratch is rebuilt every tick.
    /// </summary>
    public sealed class World
    {
        public ulong Seed;
        public WorldMode Mode;
        public int Tick;
        public uint NextId = 1;
        public byte Winner = Rules.NoWinner;
        public Map Map;
        public Mote[] Motes = new Mote[256];
        public int MoteCount;
        public Seat[] Seats = new Seat[Rules.SeatCount];
        public ScrapNode[] Scrap = new ScrapNode[0];
        public byte[][] Trails = new byte[2][];
        public readonly Dictionary<uint, Program> Programs = new Dictionary<uint, Program>();
        public ChamberSpec Chamber;
        public int ChamberProgress;

        public bool Ended => Winner != Rules.NoWinner;
        public int W => Map.W;
        public int H => Map.H;

        // ---- scratch (derived each tick, never persisted) ----
        internal Scratch S;

        public World(Map map)
        {
            Map = map;
            for (int i = 0; i < Seats.Length; i++) Seats[i] = new Seat();
            Trails[0] = new byte[map.W * map.H];
            Trails[1] = new byte[map.W * map.H];
        }

        public Program GetProgram(uint id)
        {
            if (id == 0) return null;
            Program p;
            if (Programs.TryGetValue(id, out p)) return p;
            p = Presets.Get(id, Seed);
            if (p != null) Programs[id] = p;
            return p;
        }

        public void AddProgram(Program p) { Programs[p.Id] = p; }

        public int FindMote(uint id)
        {
            int lo = 0, hi = MoteCount - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                uint v = Motes[mid].Id;
                if (v == id) return mid;
                if (v < id) lo = mid + 1; else hi = mid - 1;
            }
            return -1;
        }

        /// <summary>Append keeps the array sorted because ids only grow.</summary>
        public ref Mote AddMote(Mote m)
        {
            if (MoteCount == Motes.Length) Array.Resize(ref Motes, Motes.Length * 2);
            Motes[MoteCount] = m;
            return ref Motes[MoteCount++];
        }

        /// <summary>Restore sort order after bulk loading rows in arbitrary order.</summary>
        public void SortMotes()
        {
            Array.Sort(Motes, 0, MoteCount, MoteIdComparer.Instance);
            uint max = 0;
            for (int i = 0; i < MoteCount; i++) if (Motes[i].Id > max) max = Motes[i].Id;
            if (NextId <= max) NextId = max + 1;
        }

        sealed class MoteIdComparer : IComparer<Mote>
        {
            public static readonly MoteIdComparer Instance = new MoteIdComparer();
            public int Compare(Mote a, Mote b) => a.Id.CompareTo(b.Id);
        }

        public int Other(int seat) => seat == 0 ? 1 : 0;

        public World Clone()
        {
            var w = new World(Map)
            {
                Seed = Seed, Mode = Mode, Tick = Tick, NextId = NextId, Winner = Winner,
                MoteCount = MoteCount, Chamber = Chamber, ChamberProgress = ChamberProgress,
            };
            w.Motes = (Mote[])Motes.Clone();
            for (int i = 0; i < Seats.Length; i++) w.Seats[i] = Seats[i].Clone();
            w.Scrap = (ScrapNode[])Scrap.Clone();
            w.Trails[0] = (byte[])Trails[0].Clone();
            w.Trails[1] = (byte[])Trails[1].Clone();
            foreach (var kv in Programs) w.Programs[kv.Key] = kv.Value;
            return w;
        }

        /// <summary>FNV-1a over the persistent state. Equal hashes on server and client = no desync.</summary>
        public ulong StateHash()
        {
            ulong h = 14695981039346656037UL;
            void Mix(long v) { unchecked { h ^= (ulong)v; h *= 1099511628211UL; } }
            Mix(Tick); Mix(NextId); Mix(Winner);
            for (int i = 0; i < MoteCount; i++)
            {
                ref var m = ref Motes[i];
                Mix(m.Id); Mix(m.Seat); Mix(m.Tier); Mix(m.X); Mix(m.Y); Mix(m.Hp); Mix(m.Carry);
                Mix(m.Cooldown); Mix(m.Hx); Mix(m.Hy); Mix(m.Program); Mix(m.Ox); Mix(m.Oy);
                Mix((byte)m.Flags); Mix(m.Override); Mix(m.Lonely);
            }
            foreach (var s in Seats)
            {
                Mix((byte)s.Kind); Mix(s.Scrap); Mix(s.CoreHp); Mix(s.Production); Mix(s.Auto ? 1 : 0);
                Mix(s.Meta); Mix(s.BeaconOn ? 1 : 0); Mix(s.BeaconX); Mix(s.BeaconY); Mix(s.SingularityTicks);
                Mix(s.Mined); Mix(s.Killed); Mix(s.Lost); Mix(s.CoreCooldown);
                foreach (var l in s.Loadout) Mix(l);
            }
            foreach (var n in Scrap) Mix(n.Amount);
            for (int t = 0; t < 2; t++) { long sum = 0; var tr = Trails[t]; for (int i = 0; i < tr.Length; i++) sum = sum * 31 + tr[i]; Mix(sum); }
            return h;
        }
    }

    /// <summary>Per-tick working memory. Kept on the World so steps do not allocate.</summary>
    internal sealed class Scratch
    {
        public int[] Occ;
        public ushort[] ScrapField;
        public ushort[][] Beacon = new ushort[2][];
        public int[] Queue;
        public int Cap;
        public uint[] Sensors;
        public int[] Weights; // Cap * Channel.Count
        public byte[] Brain;  // BrainFlags
        public byte[] Pattern;
        public byte[] Temp;
        public MoteCtx[] Ctx;
        public int[] Dmg;
        public byte[] InfectSeat;
        public uint[] InfectProgram;
        public byte[] LastHit;
        public bool[] Dead;
        public bool[] Used;
        public SeatTally[] Tally = new SeatTally[Rules.SeatCount];
        // field caches: recomputed only when their sources change
        public ulong ScrapMask = ulong.MaxValue;
        public int[] BeaconAt = { -2, -2 };
        public bool[] TrailLive = { true, true };

        public void Ensure(int cells, int motes)
        {
            if (Occ == null || Occ.Length != cells)
            {
                Occ = new int[cells];
                ScrapField = new ushort[cells];
                Beacon[0] = new ushort[cells];
                Beacon[1] = new ushort[cells];
                Queue = new int[cells * 2];
            }
            if (Cap < motes)
            {
                int c = Math.Max(64, motes * 2);
                Cap = c;
                Sensors = new uint[c];
                Weights = new int[c * (int)Channel.Count];
                Brain = new byte[c];
                Pattern = new byte[c];
                Temp = new byte[c];
                Ctx = new MoteCtx[c];
                Dmg = new int[c];
                InfectSeat = new byte[c];
                InfectProgram = new uint[c];
                LastHit = new byte[c];
                Dead = new bool[c];
                Used = new bool[c];
            }
        }
    }

    internal struct MoteCtx
    {
        public short Ex, Ey;       // nearest visible enemy
        public bool HasEnemy;
        public short SumDx, SumDy; // allies in the 5x5: centre-of-mass offset
        public short SumHx, SumHy; // allies in the 5x5: summed heading
        public byte Flock;         // allies in the 5x5
        public byte Near;          // allies within sense radius
        public uint Allies25;      // ally occupancy of the 5x5 as bits: (dy+2)*5 + (dx+2)
    }

    public struct SeatTally
    {
        public int Count, Value, ArmyValue, Workers, MaxTier, Ferals;
        public int EnemiesNearCore, FeralsNearCore, SiegeMotes;
    }

    [Flags]
    public enum BrainFlags : byte
    {
        None = 0, Merge = 1, Split = 2, Mine = 4, Hold = 8, Infect = 16, Mark = 32, Holdfire = 64,
    }
}
