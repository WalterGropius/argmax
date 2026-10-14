using System;
using System.Collections.Generic;
using Argmax.Sim;
using SpacetimeDB;
using SpacetimeDB.Types;
using UnityEngine;

namespace ArgmaxClient
{
    /// <summary>
    /// A match hosted by SpacetimeDB (local `spacetime start` or maincloud). The client never simulates
    /// authoritatively: it mirrors subscribed rows into a read-only World every server tick, interpolates,
    /// and sends commands through the Issue reducer. Hosts of an AI match may pilot the AI seat with a
    /// client-side brain (ML-Agents or Jev); otherwise the server runs the AI's metaprogram itself.
    /// </summary>
    public sealed class NetSession : ISession
    {
        public enum Phase { Connecting, Ready, Lobby, Playing, Ended, Failed }

        readonly Settings settings;
        DbConnection conn;
        Identity me;
        bool haveIdentity;
        public Phase State { get; private set; } = Phase.Connecting;
        string status = "connecting";
        public ulong MatchId { get; private set; }
        public MatchRow Match { get; private set; }
        public bool HasMatch { get; private set; }
        World mirror;
        int mirrorTick = -1;
        float lastTickAt;
        float tickSeconds = Rules.TickMs / 1000f;
        int localSeat;
        readonly StepLog fx = new StepLog();
        Action<NetSession> onReady;
        string pendingJoin;
        bool pendingCreate;
        IPilot pilot;
        bool pilotClaimed;
        public Action<string> OnMessage;
        readonly Dictionary<uint, Program> library = new Dictionary<uint, Program>();
        readonly List<(string source, Action<Program, string> done)> saving = new List<(string, Action<Program, string>)>();

        public NetSession(Settings settings, Action<NetSession> onReady)
        {
            this.settings = settings;
            this.onReady = onReady;
            foreach (var p in Presets.All) library[p.Id] = p;
            status = "connecting to " + settings.Uri + " / " + settings.Database;
            conn = DbConnection.Builder()
                .WithUri(settings.Uri)
                .WithDatabaseName(settings.Database)
                .WithToken(LoadToken())
                .OnConnect(HandleConnected)
                .OnConnectError(e => Fail("could not connect: " + e.Message))
                .OnDisconnect((c, e) => { if (State != Phase.Failed) Fail("disconnected" + (e != null ? ": " + e.Message : "")); })
                .Build();
            conn.OnUnhandledReducerError += (ctx, ex) => Say("server refused: " + ex.Message);
            conn.Reducers.OnSaveProgram += (ctx, source) =>
            {
                if (ctx.Event.Status is Status.Failed(var reason))
                {
                    for (int i = saving.Count - 1; i >= 0; i--)
                        if (saving[i].source == source) { saving[i].done(null, reason); saving.RemoveAt(i); }
                }
            };
            conn.Reducers.OnSubmitChamber += (ctx, chamber, source) =>
            {
                if (ctx.Event.Status is Status.Failed(var reason)) Say("PROCTOR rejects the submission: " + reason);
                else if (ctx.Event.Status is Status.Committed) Say("PROCTOR has recorded your result. It will be remembered. Briefly.");
            };
            conn.Db.Fx.OnInsert += (ctx, row) =>
            {
                if (row.MatchId != MatchId) return;
                fx.Shots.AddRange(row.Shots); fx.Deaths.AddRange(row.Deaths); fx.Merges.AddRange(row.Merges);
                fx.Converts.AddRange(row.Converts); fx.Teleports.AddRange(row.Teleports); fx.Spawns.AddRange(row.Spawns);
            };
        }

        string TokenKey => "argmax.token." + settings.Uri + "|" + settings.Database;
        string LoadToken() { var t = PlayerPrefs.GetString(TokenKey, ""); return string.IsNullOrEmpty(t) ? null : t; }

        void HandleConnected(DbConnection c, Identity identity, string token)
        {
            me = identity;
            haveIdentity = true;
            PlayerPrefs.SetString(TokenKey, token);
            PlayerPrefs.Save();
            status = "connected as " + identity.ToString().Substring(0, 8);
            if (!string.IsNullOrWhiteSpace(settings.Name)) conn.Reducers.SetName(settings.Name);
            conn.SubscriptionBuilder()
                .OnApplied(ctx =>
                {
                    State = Phase.Ready;
                    foreach (var row in conn.Db.Program.Iter()) Learn(row);
                    onReady?.Invoke(this);
                    onReady = null;
                })
                .OnError((ctx, ex) => Fail("subscription failed: " + ex.Message))
                .AddQuery(q => q.From.Player())
                .AddQuery(q => q.From.Program())
                .AddQuery(q => q.From.Config())
                .AddQuery(q => q.From.ChamberRecord())
                .AddQuery(q => q.From.Match().Where(m => m.State.Lt((byte)2)))
                .Subscribe();
            conn.Db.Program.OnInsert += (ctx, row) => Learn(row);
        }

        void Learn(ProgramRow row)
        {
            if (library.ContainsKey(row.Id)) return;
            var p = FromRow(row);
            library[p.Id] = p;
            if (mirror != null) mirror.AddProgram(p);
            for (int i = saving.Count - 1; i >= 0; i--)
                if (saving[i].source == row.Source && haveIdentity && row.Owner.Equals(me)) { saving[i].done(p, null); saving.RemoveAt(i); }
        }

        static Program FromRow(ProgramRow r) => new Program
        {
            Id = r.Id, Name = r.Name, Source = r.Source, Kind = (ProgramKind)r.Kind, Chips = r.Chips.ToArray(),
            RuleCount = r.Rules, Symbols = r.Symbols, Worker = r.Worker,
        };

        void Fail(string why) { State = Phase.Failed; status = why; Say(why); }
        void Say(string s) { OnMessage?.Invoke(s); }

        // ------------------------------------------------------------------ match lifecycle

        public void CreateVsAi(uint meta) { pendingCreate = true; conn.Reducers.CreateMatch(true, false, 0, meta); status = "creating match"; }
        public void HostDuel() { pendingCreate = true; conn.Reducers.CreateMatch(false, false, 0, 0); status = "opening lobby"; }
        public void Join(string code) { pendingJoin = code.Trim().ToUpperInvariant(); conn.Reducers.JoinMatch(pendingJoin); status = "joining " + pendingJoin; }
        public void Leave() { if (HasMatch && Match.State != 2) conn.Reducers.Leave(MatchId); }
        public void SubmitChamber(int chamber, string source) { conn.Reducers.SubmitChamber((uint)chamber, source); }

        public IEnumerable<ChamberRecordRow> Records(int chamber)
        {
            if (conn == null || State == Phase.Connecting || State == Phase.Failed) yield break;
            foreach (var r in conn.Db.ChamberRecord.Chamber.Filter((uint)chamber)) yield return r;
        }

        void FindMatch()
        {
            if (MatchId != 0 || (!pendingCreate && pendingJoin == null) || !haveIdentity) return;
            ulong best = 0;
            foreach (var m in conn.Db.Match.Iter())
            {
                if (m.State == 2) continue;
                if (pendingJoin != null ? m.Code == pendingJoin : m.Host.Equals(me))
                    if (m.Id > best) best = m.Id;
            }
            if (best == 0) return;
            MatchId = best;
            pendingCreate = false; pendingJoin = null;
            ulong id = best;
            conn.SubscriptionBuilder()
                .OnError((ctx, ex) => Fail("match subscription failed: " + ex.Message))
                .AddQuery(q => q.From.Match().Where(m => m.Id.Eq(id)))
                .AddQuery(q => q.From.Seat().Where(s => s.MatchId.Eq(id)))
                .AddQuery(q => q.From.Mote().Where(s => s.MatchId.Eq(id)))
                .AddQuery(q => q.From.ScrapNode().Where(s => s.MatchId.Eq(id)))
                .AddQuery(q => q.From.Trail().Where(s => s.MatchId.Eq(id)))
                .Subscribe();
            conn.SubscriptionBuilder()
                .OnError((ctx, ex) => Say("effects feed unavailable: " + ex.Message))
                .AddQuery(q => q.From.Fx().Where(s => s.MatchId.Eq(id)))
                .Subscribe();
        }

        public void Update(float dt)
        {
            conn?.FrameTick();
            if (State == Phase.Failed) return;
            FindMatch();
            if (MatchId == 0) return;
            var row = conn.Db.Match.Id.Find(MatchId);
            if (row == null) return;
            Match = row;
            HasMatch = true;
            State = row.State == 0 ? Phase.Lobby : row.State == 1 ? Phase.Playing : Phase.Ended;
            status = State == Phase.Lobby ? "waiting for an opponent: share code " + row.Code
                : State == Phase.Playing ? "live on " + (settings.Server == "local" ? "local stdb" : settings.Server) + " | tick " + row.Tick
                : "match over";
            if (row.Tick != mirrorTick) Mirror(row);
            HostPilot(row);
            pilot?.Tick();
        }

        void Mirror(MatchRow m)
        {
            if (mirror == null || mirror.Seed != m.Seed)
            {
                mirror = new World(Map.Duel(m.Seed)) { Seed = m.Seed, Mode = WorldMode.Duel };
                foreach (var p in library.Values) if (p.Id >= 1000) mirror.AddProgram(p);
            }
            int advanced = mirrorTick < 0 ? 1 : Math.Max(1, m.Tick - mirrorTick);
            float now = Time.time;
            if (mirrorTick >= 0) tickSeconds = Mathf.Lerp(tickSeconds, Mathf.Clamp((now - lastTickAt) / advanced, 0.03f, 0.5f), 0.3f);
            lastTickAt = now;
            mirror.Tick = m.Tick;
            mirror.Winner = m.Winner;
            mirror.NextId = m.NextMoteId;
            foreach (var s in conn.Db.Seat.MatchId.Filter(m.Id))
            {
                if (s.Index >= Rules.SeatCount) continue;
                var seat = mirror.Seats[s.Index];
                seat.Kind = (SeatKind)s.Kind;
                seat.Scrap = s.Scrap; seat.CoreHp = s.CoreHp; seat.Production = s.Production; seat.Auto = s.Auto;
                for (int i = 0; i < Rules.LoadoutSlots && i < s.Loadout.Count; i++) seat.Loadout[i] = s.Loadout[i];
                seat.Meta = s.Meta;
                seat.BeaconOn = s.BeaconOn; seat.BeaconX = s.BeaconX; seat.BeaconY = s.BeaconY;
                seat.SingularityTicks = s.SingularityTicks; seat.Mined = s.Mined; seat.Killed = s.Killed; seat.Lost = s.Lost;
                if (s.Index < 2) { seat.CoreX = mirror.Map.CoreX[s.Index]; seat.CoreY = mirror.Map.CoreY[s.Index]; }
                if (haveIdentity && s.Kind == (byte)SeatKind.Human && s.Occupant.Equals(me)) localSeat = s.Index;
            }
            mirror.MoteCount = 0;
            foreach (var r in conn.Db.Mote.MatchId.Filter(m.Id))
                mirror.AddMote(new Mote
                {
                    Id = r.Id, Seat = r.Seat, Tier = r.Tier, X = r.X, Y = r.Y, Hp = r.Hp, Carry = r.Carry, Cooldown = r.Cooldown,
                    Hx = r.Hx, Hy = r.Hy, Program = r.Program, Ox = r.Ox, Oy = r.Oy, Flags = (MoteFlags)r.Flags, Override = r.Override, Lonely = r.Lonely,
                });
            mirror.SortMotes();
            var scrap = new List<ScrapRow>(conn.Db.ScrapNode.MatchId.Filter(m.Id));
            scrap.Sort((a, b) => a.Index.CompareTo(b.Index));
            if (mirror.Scrap.Length != scrap.Count) mirror.Scrap = new ScrapNode[scrap.Count];
            for (int i = 0; i < scrap.Count; i++) mirror.Scrap[i] = new ScrapNode { X = scrap[i].X, Y = scrap[i].Y, Amount = scrap[i].Amount };
            foreach (var t in conn.Db.Trail.MatchId.Filter(m.Id))
            {
                if (t.Seat > 1) continue;
                var dense = mirror.Trails[t.Seat];
                Array.Clear(dense, 0, dense.Length);
                foreach (var c in t.Cells) { int i = c >> 8; if (i >= 0 && i < dense.Length) dense[i] = (byte)(c & 0xFF); }
            }
            Sim.Refresh(mirror);
            mirrorTick = m.Tick;
        }

        void HostPilot(MatchRow m)
        {
            if (pilotClaimed || m.State != 1 || !m.VsAi || !haveIdentity || !m.Host.Equals(me) || settings.Pilot == PilotKind.Server || mirror == null) return;
            pilotClaimed = true;
            conn.Reducers.SetPilot(m.Id, 1, true);
            var body = new NetBody(this);
            pilot = settings.Pilot == PilotKind.Jev ? (IPilot)new JevPilot(body, settings.JevKey) : new MlPilot(body);
            Say("you host the AI seat: " + (settings.Pilot == PilotKind.Jev ? "JEV (System 1) is commanding it" : "the ML-Agents commander is in control"));
        }

        sealed class NetBody : ICommanderBody
        {
            readonly NetSession s;
            public NetBody(NetSession s) { this.s = s; }
            public World World => s.mirror;
            public int Seat => 1;
            public void Apply(MetaDecision d) { foreach (var c in Meta.ToCommands(s.mirror, 1, d)) s.Send(c); }
        }

        void Send(Cmd c)
        {
            if (!HasMatch || Match.State != 1) return;
            var ids = c.Ids != null ? new List<uint>(c.Ids) : new List<uint>();
            conn.Reducers.Issue(MatchId, c.Seat, (byte)c.Kind, c.X, c.Y, c.Arg, ids);
        }

        // ------------------------------------------------------------------ ISession

        public string Title => HasMatch ? (Match.VsAi ? "SKIRMISH" : "DUEL") + " [" + Match.Code + "]" : "ONLINE";
        public string Status => status;
        public string Banner => State == Phase.Lobby ? "CODE  " + Match.Code + "  (send this to your opponent)" : "";
        public World World => State == Phase.Playing || State == Phase.Ended ? mirror : null;
        public int LocalSeat => localSeat;
        public float TickSeconds => tickSeconds;
        public bool CanCommand => State == Phase.Playing;
        public float Speed { get => 1f; set { } }
        public bool Paused { get => false; set { } }
        public string PilotReadout => pilot?.Readout;

        public void Issue(Cmd cmd)
        {
            if (!CanCommand) return;
            cmd.Seat = (byte)localSeat;
            Send(cmd);
        }

        public bool TakeFx(StepLog into)
        {
            if (!FxUtil.Any(fx)) return false;
            FxUtil.Append(into, fx);
            fx.Clear();
            return true;
        }

        public IEnumerable<Program> Library => library.Values;

        public void SaveProgram(string source, Action<Program, string> done)
        {
            var r = Argot.Compile(source);
            if (!r.Ok) { done(null, string.Join("\n", r.Errors)); return; }
            if (State == Phase.Connecting || State == Phase.Failed) { done(null, "not connected"); return; }
            saving.Add((source, done));
            conn.Reducers.SaveProgram(source);
        }

        public bool Connected => State != Phase.Connecting && State != Phase.Failed;

        public void Dispose()
        {
            pilot?.Dispose();
            pilot = null;
            if (pilotClaimed && HasMatch && Match.State == 1) conn.Reducers.SetPilot(MatchId, 1, false);
            if (conn != null && conn.IsActive) conn.Disconnect();
            conn = null;
        }
    }
}
