using SpacetimeDB;
using Argmax.Sim;
using SimProgram = Argmax.Sim.Program;

/// <summary>
/// ARGMAX authoritative server. A thin shell around Argmax.Sim: every tick each running match is
/// loaded from rows into a World, stepped by the shared deterministic simulation, and diffed back.
/// Clients subscribe to the rows and interpolate. Nothing here decides game rules.
/// </summary>
public static partial class Module
{
    public const string Version = "0.1.0";
    const byte Lobby = 0, Running = 1, Ended = 2;

    // =================================================================== tables

    [SpacetimeDB.Table(Accessor = "Config", Public = true)]
    public partial struct ConfigRow
    {
        [PrimaryKey] public uint Id;
        public uint NextProgramId;
        public uint TickMs;
        public string Version;
    }

    [SpacetimeDB.Table(Accessor = "Player", Public = true)]
    public partial struct PlayerRow
    {
        [PrimaryKey] public Identity Identity;
        public string Name;
        public bool Online;
        public int Wins;
        public int Losses;
        public Timestamp LastSeen;
    }

    [SpacetimeDB.Table(Accessor = "Match", Public = true)]
    public partial struct MatchRow
    {
        [PrimaryKey, AutoInc] public ulong Id;
        [Unique] public string Code;
        public Identity Host;
        public byte State;
        public bool VsAi;
        public bool Manual;
        public ulong Seed;
        public int Tick;
        public uint NextMoteId;
        public byte Winner;
        public ulong StateHash;
        public uint Revision; // bumped by any reducer that edits match rows outside the tick
        public Timestamp CreatedAt;
    }

    [SpacetimeDB.Table(Accessor = "Seat", Public = true)]
    public partial struct SeatRow
    {
        [PrimaryKey] public ulong Key; // matchId * 4 + index
        [SpacetimeDB.Index.BTree] public ulong MatchId;
        public byte Index;
        public byte Kind;
        public Identity Occupant;
        public string Name;
        public bool Pilot; // a client-side brain (ML-Agents / Jev) drives this AI seat
        public int Scrap;
        public int CoreHp;
        public byte Production;
        public bool Auto;
        public List<uint> Loadout;
        public uint Meta;
        public bool BeaconOn;
        public short BeaconX;
        public short BeaconY;
        public int SingularityTicks;
        public int Mined;
        public int Killed;
        public int Lost;
        public byte CoreCooldown;
    }

    [SpacetimeDB.Table(Accessor = "Mote", Public = true)]
    public partial struct MoteRow
    {
        [PrimaryKey] public ulong Key; // matchId << 32 | id
        [SpacetimeDB.Index.BTree] public ulong MatchId;
        public uint Id;
        public byte Seat;
        public byte Tier;
        public short X;
        public short Y;
        public int Hp;
        public byte Carry;
        public byte Cooldown;
        public sbyte Hx;
        public sbyte Hy;
        public uint Program;
        public short Ox;
        public short Oy;
        public byte Flags;
        public byte Override;
        public byte Lonely;
    }

    [SpacetimeDB.Table(Accessor = "ScrapNode", Public = true)]
    public partial struct ScrapRow
    {
        [PrimaryKey] public ulong Key; // matchId * 64 + index
        [SpacetimeDB.Index.BTree] public ulong MatchId;
        public byte Index;
        public short X;
        public short Y;
        public int Amount;
    }

    [SpacetimeDB.Table(Accessor = "Trail", Public = true)]
    public partial struct TrailRow
    {
        [PrimaryKey] public ulong Key; // matchId * 2 + seat
        [SpacetimeDB.Index.BTree] public ulong MatchId;
        public byte Seat;
        public List<int> Cells; // sparse: cell << 8 | intensity, nonzero cells only
    }

    [SpacetimeDB.Table(Accessor = "Program", Public = true)]
    public partial struct ProgramRow
    {
        [PrimaryKey] public uint Id;
        [SpacetimeDB.Index.BTree] public Identity Owner;
        public string Name;
        public string Source;
        public byte Kind;
        public List<ulong> Chips;
        public int Rules;
        public int Symbols;
        public bool Worker;
        public bool Preset;
        public Timestamp CreatedAt;
    }

    [SpacetimeDB.Table(Accessor = "PendingCmd")]
    public partial struct PendingCmdRow
    {
        [PrimaryKey, AutoInc] public ulong Id;
        [SpacetimeDB.Index.BTree] public ulong MatchId;
        public byte Seat;
        public byte Kind;
        public short X;
        public short Y;
        public uint Arg;
        public List<uint> Ids;
    }

    /// <summary>Every applied command, by tick. Seed + this log = a perfect replay (and imitation-learning data).</summary>
    [SpacetimeDB.Table(Accessor = "CmdLog")]
    public partial struct CmdLogRow
    {
        [PrimaryKey, AutoInc] public ulong Id;
        [SpacetimeDB.Index.BTree] public ulong MatchId;
        public int Tick;
        public byte Seat;
        public byte Kind;
        public short X;
        public short Y;
        public uint Arg;
        public List<uint> Ids;
    }

    /// <summary>Per-tick effects (shots, deaths, merges...). Event table: broadcast, never cached.</summary>
    [SpacetimeDB.Table(Accessor = "Fx", Public = true, Event = true)]
    public partial struct FxRow
    {
        [SpacetimeDB.Index.BTree] public ulong MatchId;
        public int Tick;
        public List<int> Shots;
        public List<int> Deaths;
        public List<int> Merges;
        public List<int> Converts;
        public List<int> Teleports;
        public List<int> Spawns;
    }

    [SpacetimeDB.Table(Accessor = "ChamberRecord", Public = true)]
    public partial struct ChamberRecordRow
    {
        [PrimaryKey, AutoInc] public ulong Id;
        [SpacetimeDB.Index.BTree] public uint Chamber;
        public Identity Owner;
        public string PlayerName;
        public int Ticks;
        public int Symbols;
        public int Lost;
        public string Source;
        public Timestamp CreatedAt;
    }

    [SpacetimeDB.Table(Accessor = "TickTimer", Scheduled = nameof(ServerTick), ScheduledAt = nameof(ScheduledAt))]
    public partial struct TickTimer
    {
        [PrimaryKey, AutoInc] public ulong ScheduledId;
        public ScheduleAt ScheduledAt;
    }

    // =================================================================== lifecycle

    [SpacetimeDB.Reducer(ReducerKind.Init)]
    public static void Init(ReducerContext ctx)
    {
        ctx.Db.Config.Insert(new ConfigRow { Id = 0, NextProgramId = 1000, TickMs = Rules.TickMs, Version = Version });
        foreach (var p in Presets.All)
            ctx.Db.Program.Insert(ToRow(p, ctx.Sender, true, ctx.Timestamp));
        Log.Info($"ARGMAX {Version} online. {Presets.All.Count()} preset programs. Tick {Rules.TickMs} ms, armed only while a match runs.");
    }

    [SpacetimeDB.Reducer(ReducerKind.ClientConnected)]
    public static void ClientConnected(ReducerContext ctx)
    {
        if (ctx.Db.Player.Identity.Find(ctx.Sender) is { } p)
            ctx.Db.Player.Identity.Update(p with { Online = true, LastSeen = ctx.Timestamp });
        else
            ctx.Db.Player.Insert(new PlayerRow
            {
                Identity = ctx.Sender, Name = "proc-" + ctx.Sender.ToString().Substring(0, 6), Online = true, LastSeen = ctx.Timestamp,
            });
    }

    [SpacetimeDB.Reducer(ReducerKind.ClientDisconnected)]
    public static void ClientDisconnected(ReducerContext ctx)
    {
        if (ctx.Db.Player.Identity.Find(ctx.Sender) is { } p)
            ctx.Db.Player.Identity.Update(p with { Online = false, LastSeen = ctx.Timestamp });
    }

    // =================================================================== player reducers

    [SpacetimeDB.Reducer]
    public static void SetName(ReducerContext ctx, string name)
    {
        name = (name ?? "").Trim();
        if (name.Length < 2 || name.Length > 20) throw new Exception("name must be 2-20 characters");
        if (ctx.Db.Player.Identity.Find(ctx.Sender) is { } p) ctx.Db.Player.Identity.Update(p with { Name = name });
    }

    /// <summary>Compile Argot server-side (same compiler as the client) and store it as a new immutable program.</summary>
    [SpacetimeDB.Reducer]
    public static void SaveProgram(ReducerContext ctx, string source)
    {
        if (source == null || source.Length > 4000) throw new Exception("program too long");
        var r = Argot.Compile(source);
        if (!r.Ok) throw new Exception(string.Join("\n", r.Errors));
        var cfg = ctx.Db.Config.Id.Find(0) ?? throw new Exception("not initialised");
        r.Program.Id = cfg.NextProgramId;
        ctx.Db.Config.Id.Update(cfg with { NextProgramId = cfg.NextProgramId + 1 });
        ctx.Db.Program.Insert(ToRow(r.Program, ctx.Sender, false, ctx.Timestamp));
    }

    [SpacetimeDB.Reducer]
    public static void CreateMatch(ReducerContext ctx, bool vsAi, bool manual, ulong seed, uint aiMeta)
    {
        foreach (var m in ctx.Db.Match.Iter())
            if (m.Host == ctx.Sender && m.State != Ended) EndMatch(ctx, m, Rules.Draw);
        if (seed == 0) seed = ((ulong)(uint)ctx.Rng.Next() << 32) | (uint)ctx.Rng.Next();
        var meta = aiMeta == 0 ? Presets.Overmind : aiMeta;
        var w = Sim.NewDuel(seed, SeatKind.Human, vsAi ? SeatKind.Ai : SeatKind.Empty, 0, vsAi ? meta : 0);
        var match = ctx.Db.Match.Insert(new MatchRow
        {
            Code = NewCode(ctx), Host = ctx.Sender, State = vsAi ? Running : Lobby, VsAi = vsAi, Manual = manual,
            Seed = seed, Tick = 0, NextMoteId = w.NextId, Winner = Rules.NoWinner, CreatedAt = ctx.Timestamp,
        });
        string hostName = ctx.Db.Player.Identity.Find(ctx.Sender)?.Name ?? "host";
        for (int s = 0; s < Rules.SeatCount; s++)
        {
            var row = SeatToRow(match.Id, s, w.Seats[s], default);
            if (s == 0) { row.Occupant = ctx.Sender; row.Name = hostName; }
            if (s == 1 && vsAi) row.Name = Presets.Get(meta, 0)?.Name ?? "AI";
            if (s == 2) row.Name = "NOISE";
            ctx.Db.Seat.Insert(row);
        }
        for (int i = 0; i < w.MoteCount; i++) ctx.Db.Mote.Insert(ToRow(match.Id, w.Motes[i]));
        for (int i = 0; i < w.Scrap.Length; i++)
            ctx.Db.ScrapNode.Insert(new ScrapRow { Key = match.Id * 64 + (ulong)i, MatchId = match.Id, Index = (byte)i, X = w.Scrap[i].X, Y = w.Scrap[i].Y, Amount = w.Scrap[i].Amount });
        for (int s = 0; s < 2; s++)
            ctx.Db.Trail.Insert(new TrailRow { Key = match.Id * 2 + (ulong)s, MatchId = match.Id, Seat = (byte)s, Cells = Sparse(w.Trails[s]) });
        if (vsAi && !manual) EnsureTicker(ctx);
        Log.Info($"match {match.Id} [{match.Code}] seed {seed} {(vsAi ? "vs " + Presets.Get(meta, 0)?.Name : "lobby")}{(manual ? " manual" : "")}");
    }

    [SpacetimeDB.Reducer]
    public static void JoinMatch(ReducerContext ctx, string code)
    {
        var m = ctx.Db.Match.Code.Find((code ?? "").Trim().ToUpperInvariant()) ?? throw new Exception("no match with code " + code);
        if (m.State != Lobby) throw new Exception("match already started");
        if (m.Host == ctx.Sender) throw new Exception("you are the host; share the code");
        var seat = ctx.Db.Seat.Key.Find(m.Id * 4 + 1) ?? throw new Exception("seat missing");
        // the guest's core and starting motes were laid out at creation; claiming the seat brings them online
        ctx.Db.Seat.Key.Update(seat with
        {
            Kind = (byte)SeatKind.Human, Occupant = ctx.Sender,
            Name = ctx.Db.Player.Identity.Find(ctx.Sender)?.Name ?? "guest",
        });
        ctx.Db.Match.Id.Update(m with { State = Running, Revision = m.Revision + 1 });
        if (!m.Manual) EnsureTicker(ctx);
        Log.Info($"match {m.Id} [{m.Code}] joined");
    }

    /// <summary>Queue a command for the next tick. Humans command their own seat; a host may pilot its AI seat.</summary>
    [SpacetimeDB.Reducer]
    public static void Issue(ReducerContext ctx, ulong matchId, byte seat, byte kind, short x, short y, uint arg, List<uint> ids)
    {
        var m = ctx.Db.Match.Id.Find(matchId) ?? throw new Exception("no such match");
        if (m.State != Running) throw new Exception("match not running");
        var s = ctx.Db.Seat.Key.Find(matchId * 4 + seat) ?? throw new Exception("no such seat");
        bool mine = s.Kind == (byte)SeatKind.Human && s.Occupant == ctx.Sender;
        bool piloting = s.Kind == (byte)SeatKind.Ai && m.Host == ctx.Sender;
        if (!mine && !piloting) throw new Exception("not your seat");
        if (ids != null && ids.Count > 512) throw new Exception("too many units in one command");
        if ((CmdKind)kind == CmdKind.Reprogram || (CmdKind)kind == CmdKind.SetSlot || (CmdKind)kind == CmdKind.SetMeta)
            if (arg != 0 && Presets.Get(arg, m.Seed) == null && ctx.Db.Program.Id.Find(arg) == null) throw new Exception("unknown program " + arg);
        ctx.Db.PendingCmd.Insert(new PendingCmdRow { MatchId = matchId, Seat = seat, Kind = kind, X = x, Y = y, Arg = arg, Ids = ids ?? new List<uint>() });
    }

    /// <summary>Hand an AI seat to (or take it back from) a client-side brain: ML-Agents policy or Jev.</summary>
    [SpacetimeDB.Reducer]
    public static void SetPilot(ReducerContext ctx, ulong matchId, byte seat, bool on)
    {
        var m = ctx.Db.Match.Id.Find(matchId) ?? throw new Exception("no such match");
        if (m.Host != ctx.Sender) throw new Exception("only the host can pilot the AI seat");
        var s = ctx.Db.Seat.Key.Find(matchId * 4 + seat) ?? throw new Exception("no such seat");
        if (s.Kind != (byte)SeatKind.Ai) throw new Exception("not an AI seat");
        ctx.Db.Seat.Key.Update(s with { Pilot = on });
        ctx.Db.Match.Id.Update(m with { Revision = m.Revision + 1 });
    }

    /// <summary>Deterministic stepping for tests and headless tools: manual matches only advance here.</summary>
    [SpacetimeDB.Reducer]
    public static void StepMatch(ReducerContext ctx, ulong matchId, uint ticks)
    {
        var m = ctx.Db.Match.Id.Find(matchId) ?? throw new Exception("no such match");
        if (!m.Manual) throw new Exception("only manual matches can be stepped");
        if (m.Host != ctx.Sender) throw new Exception("only the host can step");
        if (ticks > 600) throw new Exception("at most 600 ticks per call");
        for (uint i = 0; i < ticks; i++)
        {
            m = ctx.Db.Match.Id.Find(matchId)!.Value;
            if (m.State != Running) break;
            StepOnce(ctx, m);
        }
    }

    [SpacetimeDB.Reducer]
    public static void Leave(ReducerContext ctx, ulong matchId)
    {
        var m = ctx.Db.Match.Id.Find(matchId) ?? throw new Exception("no such match");
        if (m.State == Ended) return;
        int seat = -1;
        foreach (var s in ctx.Db.Seat.MatchId.Filter(matchId))
            if (s.Kind == (byte)SeatKind.Human && s.Occupant == ctx.Sender) seat = s.Index;
        if (seat < 0) throw new Exception("you are not in this match");
        EndMatch(ctx, m, m.State == Lobby ? Rules.Draw : (byte)(1 - seat));
    }

    /// <summary>Re-run a chamber solution with the shared sim. Records are proofs: the server ran them.</summary>
    [SpacetimeDB.Reducer]
    public static void SubmitChamber(ReducerContext ctx, uint chamber, string source)
    {
        var spec = Chambers.Get((int)chamber) ?? throw new Exception("no such chamber");
        if (source == null || source.Length > 4000) throw new Exception("program too long");
        var r = Chambers.Run(spec, source);
        if (r.Error != null) throw new Exception(r.Error);
        if (!r.Success) throw new Exception($"chamber failed: {r.Progress}/{spec.Target} after {r.Ticks} ticks");
        ctx.Db.ChamberRecord.Insert(new ChamberRecordRow
        {
            Chamber = chamber, Owner = ctx.Sender, PlayerName = ctx.Db.Player.Identity.Find(ctx.Sender)?.Name ?? "?",
            Ticks = r.Ticks, Symbols = r.Symbols, Lost = r.Lost, Source = source, CreatedAt = ctx.Timestamp,
        });
        Log.Info($"chamber {chamber} solved in {r.Ticks} ticks / {r.Symbols} symbols / {r.Lost} lost");
    }

    // =================================================================== the tick

    /// <summary>The 10 Hz heartbeat exists only while a live match does, so an idle database costs nothing.</summary>
    static void EnsureTicker(ReducerContext ctx)
    {
        if (ctx.Db.TickTimer.Count > 0) return;
        ctx.Db.TickTimer.Insert(new TickTimer { ScheduledId = 0, ScheduledAt = new ScheduleAt.Interval(TimeSpan.FromMilliseconds(Rules.TickMs)) });
    }

    [SpacetimeDB.Reducer]
    public static void ServerTick(ReducerContext ctx, TickTimer timer)
    {
        if (ctx.Sender != ctx.Identity) throw new Exception("the tick is not yours to call");
        var running = new List<MatchRow>();
        foreach (var m in ctx.Db.Match.Iter()) if (m.State == Running && !m.Manual) running.Add(m);
        if (running.Count == 0)
        {
            foreach (var t in ctx.Db.TickTimer.Iter().ToList()) ctx.Db.TickTimer.ScheduledId.Delete(t.ScheduledId);
            Log.Info("no live matches: heartbeat parked");
            return;
        }
        foreach (var m in running)
        {
            if (m.Tick % 100 == 99 && Abandoned(ctx, m)) { EndMatch(ctx, m, Rules.Draw); continue; }
            StepOnce(ctx, m);
        }
    }

    static bool Abandoned(ReducerContext ctx, MatchRow m)
    {
        foreach (var s in ctx.Db.Seat.MatchId.Filter(m.Id))
            if (s.Kind == (byte)SeatKind.Human && ctx.Db.Player.Identity.Find(s.Occupant) is { Online: true }) return false;
        return true;
    }

    static void StepOnce(ReducerContext ctx, MatchRow m)
    {
        var pending = ctx.Db.PendingCmd.MatchId.Filter(m.Id).ToList();
        pending.Sort((a, b) => a.Id.CompareTo(b.Id));
        var h = Warm(ctx, m);
        var w = h.World;
        var cmds = new List<Cmd>(pending.Count);
        foreach (var p in pending)
        {
            var kind = (CmdKind)p.Kind;
            if ((kind == CmdKind.Reprogram || kind == CmdKind.SetSlot || kind == CmdKind.SetMeta) && p.Arg != 0 && w.GetProgram(p.Arg) == null)
                if (ctx.Db.Program.Id.Find(p.Arg) is { } pr) w.AddProgram(FromRow(pr));
            cmds.Add(new Cmd { Seat = p.Seat, Kind = kind, X = p.X, Y = p.Y, Arg = p.Arg, Ids = p.Ids.ToArray() });
            ctx.Db.CmdLog.Insert(new CmdLogRow { MatchId = m.Id, Tick = m.Tick, Seat = p.Seat, Kind = p.Kind, X = p.X, Y = p.Y, Arg = p.Arg, Ids = p.Ids });
            ctx.Db.PendingCmd.Id.Delete(p.Id);
        }
        h.Log.Clear();
        Sim.Step(w, cmds, h.Log);
        var log = h.Log;
        Flush(ctx, m, h);
        if (log.Shots.Count + log.Deaths.Count + log.Merges.Count + log.Converts.Count + log.Teleports.Count + log.Spawns.Count > 0)
            ctx.Db.Fx.Insert(new FxRow
            {
                MatchId = m.Id, Tick = w.Tick, Shots = new List<int>(log.Shots), Deaths = new List<int>(log.Deaths), Merges = new List<int>(log.Merges),
                Converts = new List<int>(log.Converts), Teleports = new List<int>(log.Teleports), Spawns = new List<int>(log.Spawns),
            });
        if (w.Ended) EndMatch(ctx, ctx.Db.Match.Id.Find(m.Id)!.Value, w.Winner);
    }

    static void EndMatch(ReducerContext ctx, MatchRow m, byte winner)
    {
        ctx.Db.Match.Id.Update(m with { State = Ended, Winner = winner });
        foreach (var s in ctx.Db.Seat.MatchId.Filter(m.Id).ToList())
        {
            if (s.Kind != (byte)SeatKind.Human || winner >= Rules.SeatCount || m.State == Lobby) continue;
            if (ctx.Db.Player.Identity.Find(s.Occupant) is { } p)
                ctx.Db.Player.Identity.Update(s.Index == winner ? p with { Wins = p.Wins + 1 } : p with { Losses = p.Losses + 1 });
        }
        // the final state lives on in Match + Seat + CmdLog (a replay); bulk rows go
        foreach (var r in ctx.Db.Mote.MatchId.Filter(m.Id).ToList()) ctx.Db.Mote.Key.Delete(r.Key);
        foreach (var r in ctx.Db.ScrapNode.MatchId.Filter(m.Id).ToList()) ctx.Db.ScrapNode.Key.Delete(r.Key);
        foreach (var r in ctx.Db.Trail.MatchId.Filter(m.Id).ToList()) ctx.Db.Trail.Key.Delete(r.Key);
        foreach (var r in ctx.Db.PendingCmd.MatchId.Filter(m.Id).ToList()) ctx.Db.PendingCmd.Id.Delete(r.Id);
        hot.Remove(m.Id);
        Log.Info($"match {m.Id} ended at tick {m.Tick}: winner {winner}");
    }

    // =================================================================== rows <-> world

    /// <summary>
    /// A match's live World kept in module memory between ticks. Rows stay the source of truth: the cache
    /// is used only while (tick, state hash, revision) still match the Match row, otherwise it is rebuilt
    /// from rows. On the .NET wasm interpreter this turns a full reload per tick into a diff-only write.
    /// </summary>
    sealed class Hot
    {
        public World World = null!;
        public int Tick;
        public ulong Hash;
        public uint Revision;
        public Dictionary<uint, MoteRow> Motes = new();
        public Dictionary<uint, MoteRow> Spare = new();
        public SeatRow[] Seats = new SeatRow[Rules.SeatCount];
        public ScrapRow[] Scrap = Array.Empty<ScrapRow>();
        public TrailRow[] Trails = new TrailRow[2];
        public StepLog Log = new();
    }

    static readonly Dictionary<ulong, Hot> hot = new();

    static Hot Warm(ReducerContext ctx, MatchRow m)
    {
        if (hot.TryGetValue(m.Id, out var h) && h.Tick == m.Tick && h.Hash == m.StateHash && h.Revision == m.Revision) return h;
        h = Load(ctx, m);
        hot[m.Id] = h;
        return h;
    }

    static Hot Load(ReducerContext ctx, MatchRow m)
    {
        var w = new World(Map.Duel(m.Seed)) { Seed = m.Seed, Mode = WorldMode.Duel, Tick = m.Tick, NextId = m.NextMoteId, Winner = m.Winner };
        var h = new Hot { World = w, Tick = m.Tick, Hash = m.StateHash, Revision = m.Revision };
        var needed = new HashSet<uint>();
        foreach (var s in ctx.Db.Seat.MatchId.Filter(m.Id))
        {
            h.Seats[s.Index] = s;
            var seat = w.Seats[s.Index];
            seat.Kind = (SeatKind)s.Kind;
            seat.Scrap = s.Scrap; seat.CoreHp = s.CoreHp; seat.Production = s.Production; seat.Auto = s.Auto;
            for (int i = 0; i < Rules.LoadoutSlots && i < s.Loadout.Count; i++) { seat.Loadout[i] = s.Loadout[i]; needed.Add(s.Loadout[i]); }
            seat.Meta = s.Pilot ? 0 : s.Meta;
            needed.Add(s.Meta);
            seat.BeaconOn = s.BeaconOn; seat.BeaconX = s.BeaconX; seat.BeaconY = s.BeaconY;
            seat.SingularityTicks = s.SingularityTicks; seat.Mined = s.Mined; seat.Killed = s.Killed; seat.Lost = s.Lost;
            seat.CoreCooldown = s.CoreCooldown;
            if (s.Index < 2) { seat.CoreX = w.Map.CoreX[s.Index]; seat.CoreY = w.Map.CoreY[s.Index]; }
        }
        foreach (var r in ctx.Db.Mote.MatchId.Filter(m.Id))
        {
            h.Motes[r.Id] = r;
            w.AddMote(new Mote
            {
                Id = r.Id, Seat = r.Seat, Tier = r.Tier, X = r.X, Y = r.Y, Hp = r.Hp, Carry = r.Carry, Cooldown = r.Cooldown,
                Hx = r.Hx, Hy = r.Hy, Program = r.Program, Ox = r.Ox, Oy = r.Oy, Flags = (MoteFlags)r.Flags, Override = r.Override, Lonely = r.Lonely,
            });
            needed.Add(r.Program);
        }
        w.SortMotes();
        var scrap = ctx.Db.ScrapNode.MatchId.Filter(m.Id).ToList();
        scrap.Sort((a, b) => a.Index.CompareTo(b.Index));
        h.Scrap = scrap.ToArray();
        w.Scrap = new ScrapNode[scrap.Count];
        for (int i = 0; i < scrap.Count; i++) w.Scrap[i] = new ScrapNode { X = scrap[i].X, Y = scrap[i].Y, Amount = scrap[i].Amount };
        foreach (var t in ctx.Db.Trail.MatchId.Filter(m.Id))
        {
            h.Trails[t.Seat] = t;
            Dense(t.Cells, w.Trails[t.Seat]);
        }
        foreach (var id in needed)
        {
            if (id == 0 || Presets.Get(id, m.Seed) != null) continue;
            if (ctx.Db.Program.Id.Find(id) is { } pr) w.AddProgram(FromRow(pr));
        }
        return h;
    }

    /// <summary>Write only what the step changed, then remember it as the new baseline.</summary>
    static void Flush(ReducerContext ctx, MatchRow m, Hot h)
    {
        var w = h.World;
        var next = h.Spare;
        next.Clear();
        for (int i = 0; i < w.MoteCount; i++)
        {
            var row = ToRow(m.Id, w.Motes[i]);
            if (h.Motes.TryGetValue(row.Id, out var prev))
            {
                if (!Same(prev, row)) ctx.Db.Mote.Key.Update(row);
                h.Motes.Remove(row.Id);
            }
            else ctx.Db.Mote.Insert(row);
            next[row.Id] = row;
        }
        foreach (var gone in h.Motes.Values) ctx.Db.Mote.Key.Delete(gone.Key);
        h.Spare = h.Motes;
        h.Motes = next;

        for (int s = 0; s < Rules.SeatCount; s++)
        {
            var prev = h.Seats[s];
            if (prev.MatchId == 0) continue;
            var row = SeatToRow(m.Id, s, w.Seats[s], prev);
            if (!Same(prev, row)) { ctx.Db.Seat.Key.Update(row); h.Seats[s] = row; }
        }
        for (int i = 0; i < h.Scrap.Length && i < w.Scrap.Length; i++)
            if (h.Scrap[i].Amount != w.Scrap[i].Amount)
            {
                h.Scrap[i] = h.Scrap[i] with { Amount = w.Scrap[i].Amount };
                ctx.Db.ScrapNode.Key.Update(h.Scrap[i]);
            }
        for (int s = 0; s < 2; s++)
        {
            var prev = h.Trails[s];
            if (prev.Cells == null) continue;
            var cells = Sparse(w.Trails[s]);
            if (cells.Count == prev.Cells.Count && cells.SequenceEqual(prev.Cells)) continue;
            h.Trails[s] = prev with { Cells = cells };
            ctx.Db.Trail.Key.Update(h.Trails[s]);
        }
        h.Tick = w.Tick;
        h.Hash = w.StateHash();
        ctx.Db.Match.Id.Update(m with { Tick = w.Tick, NextMoteId = w.NextId, Winner = w.Winner, StateHash = h.Hash });
    }

    static List<int> Sparse(byte[] dense)
    {
        var list = new List<int>();
        for (int i = 0; i < dense.Length; i++) if (dense[i] != 0) list.Add(i << 8 | dense[i]);
        return list;
    }

    static void Dense(List<int>? cells, byte[] dense)
    {
        Array.Clear(dense);
        if (cells == null) return;
        foreach (var c in cells) { int i = c >> 8; if (i >= 0 && i < dense.Length) dense[i] = (byte)(c & 0xFF); }
    }

    static MoteRow ToRow(ulong matchId, in Mote x) => new MoteRow
    {
        Key = (matchId << 32) | x.Id, MatchId = matchId, Id = x.Id, Seat = x.Seat, Tier = x.Tier, X = x.X, Y = x.Y, Hp = x.Hp,
        Carry = x.Carry, Cooldown = x.Cooldown, Hx = x.Hx, Hy = x.Hy, Program = x.Program, Ox = x.Ox, Oy = x.Oy,
        Flags = (byte)x.Flags, Override = x.Override, Lonely = x.Lonely,
    };

    static bool Same(in MoteRow a, in MoteRow b) =>
        a.X == b.X && a.Y == b.Y && a.Hp == b.Hp && a.Tier == b.Tier && a.Seat == b.Seat && a.Carry == b.Carry &&
        a.Cooldown == b.Cooldown && a.Hx == b.Hx && a.Hy == b.Hy && a.Program == b.Program && a.Ox == b.Ox && a.Oy == b.Oy &&
        a.Flags == b.Flags && a.Override == b.Override && a.Lonely == b.Lonely;

    static SeatRow SeatToRow(ulong matchId, int index, Seat s, SeatRow prev) => new SeatRow
    {
        Key = matchId * 4 + (ulong)index, MatchId = matchId, Index = (byte)index, Kind = (byte)s.Kind,
        Occupant = prev.Occupant, Name = prev.Name ?? "", Pilot = prev.Pilot,
        Scrap = s.Scrap, CoreHp = s.CoreHp, Production = s.Production, Auto = s.Auto, Loadout = new List<uint>(s.Loadout),
        Meta = prev.Pilot ? prev.Meta : s.Meta,
        BeaconOn = s.BeaconOn, BeaconX = s.BeaconX, BeaconY = s.BeaconY, SingularityTicks = s.SingularityTicks,
        Mined = s.Mined, Killed = s.Killed, Lost = s.Lost, CoreCooldown = s.CoreCooldown,
    };

    static bool Same(in SeatRow a, in SeatRow b)
    {
        if (a.Scrap != b.Scrap || a.CoreHp != b.CoreHp || a.Production != b.Production || a.Auto != b.Auto || a.Meta != b.Meta ||
            a.BeaconOn != b.BeaconOn || a.BeaconX != b.BeaconX || a.BeaconY != b.BeaconY || a.SingularityTicks != b.SingularityTicks ||
            a.Mined != b.Mined || a.Killed != b.Killed || a.Lost != b.Lost || a.CoreCooldown != b.CoreCooldown || a.Kind != b.Kind) return false;
        if (a.Loadout == null || b.Loadout == null || a.Loadout.Count != b.Loadout.Count) return false;
        for (int i = 0; i < a.Loadout.Count; i++) if (a.Loadout[i] != b.Loadout[i]) return false;
        return true;
    }

    static ProgramRow ToRow(SimProgram p, Identity owner, bool preset, Timestamp at) => new ProgramRow
    {
        Id = p.Id, Owner = owner, Name = p.Name, Source = p.Source, Kind = (byte)p.Kind, Chips = new List<ulong>(p.Chips),
        Rules = p.RuleCount, Symbols = p.Symbols, Worker = p.Worker, Preset = preset, CreatedAt = at,
    };

    static SimProgram FromRow(ProgramRow r) => new SimProgram
    {
        Id = r.Id, Name = r.Name, Source = r.Source, Kind = (ProgramKind)r.Kind, Chips = r.Chips.ToArray(),
        RuleCount = r.Rules, Symbols = r.Symbols, Worker = r.Worker,
    };

    static string NewCode(ReducerContext ctx)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (int attempt = 0; attempt < 32; attempt++)
        {
            var chars = new char[4];
            for (int i = 0; i < 4; i++) chars[i] = alphabet[ctx.Rng.Next(0, alphabet.Length)];
            var code = new string(chars);
            if (ctx.Db.Match.Code.Find(code) == null) return code;
        }
        throw new Exception("could not allocate a match code");
    }
}
