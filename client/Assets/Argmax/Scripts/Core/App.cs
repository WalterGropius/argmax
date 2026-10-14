using System.Collections.Generic;
using Argmax.Sim;
using SpacetimeDB.Types;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArgmaxClient
{
    /// <summary>
    /// Composition root. No singletons, no scene setup: open any scene and press Play. Every object is
    /// created and wired here explicitly, and the frame loop is one readable method.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class App : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<App>() != null) return;
            new GameObject("ARGMAX").AddComponent<App>();
        }

        public Settings Settings { get; private set; }
        public ISession Session { get; private set; }
        public CameraRig Rig { get; private set; }
        public WorldView View { get; private set; }
        public Ui Ui { get; private set; }
        public CommandInput Commands { get; private set; }
        public CampaignProgress Progress { get; private set; }
        public CampaignUi Campaign { get; private set; }

        bool inGame, endShown, framed, spectating;
        int maxTierSeen, coreWarned, chamberId;
        string framedFor;
        bool singularityWarned, enemySingularityWarned;
        NetSession records;
        System.Action again;

        void Awake()
        {
#if UNITY_WEBGL
            Application.targetFrameRate = -1; // the browser paces frames
#else
            Application.targetFrameRate = 120;
#endif
            Application.runInBackground = true;
            Settings = Settings.Load();
            Rig = CameraRig.Create();
            View = new GameObject("WorldView").AddComponent<WorldView>();

            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.themeStyleSheet = Resources.Load<ThemeStyleSheet>("Argmax/UI/ArgmaxTheme");
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1600, 900);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            var doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = ps;
            Audio.Init(gameObject);
            Audio.Muted = Settings.Muted;
            Audio.Music = Settings.MusicOn ? 0.45f : 0f;
            Ui = new Ui(this);
            Ui.Build(doc);
            Progress = CampaignProgress.Load();
            Campaign = new CampaignUi(this, Progress);
            Campaign.Build(Ui.Root);
            Ui.BringOverlaysToFront();
            TypeKit.Apply(Ui.Root);
            View.Init(Rig.Cam, Ui.Labels);
            View.Shake = a => Rig.AddShake(a);
            View.Morph = Settings.Morph;
            Ui.RefreshLook();

            Commands = new CommandInput
            {
                PointerOverUi = Ui.PointerOverUi,
                KeyboardCaptured = Ui.Typing,
                Feedback = s => Ui.Toast(s),
                ToggleEditor = () => { if (Campaign.Active) Campaign.ToggleEditor(); else Ui.ToggleEditor(); },
                ToggleInspector = () => Ui.ToggleInspector(),
                Allowed = k => Campaign.Allows(k),
                ToggleLook = () => ToggleLook(),
            };
            Rig.PointerOverUi = Ui.PointerOverUi;
            Rig.KeyboardCaptured = Ui.Typing;
            StartAttract();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Audio.Pump();
            var s = Session;
            s?.Update(dt);
            if (records != null && !ReferenceEquals(records, s)) records.Update(dt);
            if (s is GymSession gym && !Ui.Typing())
            {
                if (Input.GetKeyDown(KeyCode.LeftBracket)) gym.Cycle(-1);
                if (Input.GetKeyDown(KeyCode.RightBracket)) gym.Cycle(1);
            }
            if (!inGame && s is LocalSession attract && (attract.W.Ended || attract.W.Tick > 9000)) StartAttract();
            Campaign.Update(Time.unscaledDeltaTime);
            View.Sound = inGame;
            if (s is LocalSession ls && ls.W.Chamber != null && !Campaign.Active)
            {
                var c = ls.W.Chamber;
                ls.Banner = c.Brief + "   " + Chambers.Progress(ls.W) + " / " + c.Target + "   t " + ls.W.Tick / Rules.TickHz + "s / " + c.TimeLimit / Rules.TickHz + "s";
            }
            Commands.Update(inGame ? s : null, Rig, View);
            if (!framed && s?.World != null) Frame(s);
            View.Render(s);
            Ui.Update(inGame ? s : null, View, Commands);
            if (inGame && s?.World != null) { Narrate(s); CheckEnd(s); }
        }

        void OnDestroy() { Session?.Dispose(); records?.Dispose(); }

        // ================================================================ sessions

        void SetSession(ISession s, bool game)
        {
            Session?.Dispose();
            Session = s;
            inGame = game;
            framed = false;
            endShown = false;
            maxTierSeen = 0; coreWarned = 0; singularityWarned = false; enemySingularityWarned = false;
            spectating = false;
            View.Selected.Clear();
            Ui.HideEnd();
            if (game) Ui.ShowGame();
            Settings.Save();
        }

        void Frame(ISession s)
        {
            var w = s.World;
            framed = true;
            framedFor = FrameKey(w);
            int seat = s.LocalSeat;
            if (!inGame || spectating)
            {
                Rig.Frame(w.W, w.H, new Vector3(w.W * 0.5f, 0, w.H * 0.42f), spectating ? 58f : 64f);
                return;
            }
            if (w.Mode == WorldMode.Chamber && Campaign.Active)
            {
                // the editor covers the left of the screen and the briefing the top: frame the arena to the right
                // visible ground width is about 1.44 x camera height; fit the map into the uncovered part
                bool editor = Campaign.Spec.HasEditor;
                float hgt = Mathf.Max(20f, Mathf.Max(w.W * (editor ? 1.15f : 0.95f), w.H * 1.9f));
                Rig.Frame(w.W, w.H, new Vector3(w.W * 0.5f - hgt * (editor ? 0.245f : 0.1f), 0, w.H * 0.5f + hgt * 0.08f), hgt);
                return;
            }
            if (w.Mode == WorldMode.Chamber) { Rig.Frame(w.W, w.H, new Vector3(w.W * 0.5f, 0, w.H * 0.5f), Mathf.Max(18f, w.W * 0.75f)); return; }
            var core = seat < 2 && w.Map.HasCore[seat] ? new Vector3(w.Map.CoreX[seat] + 0.5f, 0, w.Map.CoreY[seat] + 0.5f) : new Vector3(w.W * 0.5f, 0, w.H * 0.5f);
            Rig.Frame(w.W, w.H, core + new Vector3(4, 0, 6), 34f);
        }

        /// <summary>A live AI-vs-AI battle behind the menu. The game is its own screensaver.</summary>
        void StartAttract()
        {
            var w = Sim.NewDuel((ulong)Random.Range(1, int.MaxValue), SeatKind.Ai, SeatKind.Ai, Presets.Turtle, Presets.Overmind);
            var ls = new LocalSession(w, "attract") { CanCommand = false, Speed = 2f };
            SetSession(ls, false);
        }

        IPilot MakePilot(ICommanderBody body) =>
            Settings.Pilot == PilotKind.Jev ? new JevPilot(body, Settings.JevKey) :
            Settings.Pilot == PilotKind.Ml ? (IPilot)new MlPilot(body) : null;

        public void StartSandbox()
        {
            var meta = Settings.AiMeta;
            var w = Sim.NewDuel((ulong)Random.Range(1, int.MaxValue), SeatKind.Human, SeatKind.Ai, 0, meta);
            var ls = new LocalSession(w, "SANDBOX vs " + Presets.Get(meta, 0).Name);
            var pilot = MakePilot(ls);
            if (pilot != null) { w.Seats[1].Meta = 0; ls.Pilot = pilot; }
            SetSession(ls, true);
            again = StartSandbox;
            Ui.Log("PROCTOR: Offline sandbox. The simulation runs on your machine, bit for bit the one the server runs.", "amber");
            Ui.Log("enemy brain: " + (pilot == null ? Presets.Get(meta, 0).Name + " metaprogram" : Settings.Pilot.ToString()), "dim");
            Intro();
        }

        /// <summary>Two metaprograms fight; you watch, inspect, and read their code. AI Warehouse, but deterministic.</summary>
        public void StartSpectate() =>
            StartSpectate((ulong)Random.Range(1, int.MaxValue), Presets.MetaIds[Random.Range(0, Presets.MetaIds.Length)], Settings.AiMeta, 4f);

        public void StartSpectate(ulong seed, uint a, uint b, float speed)
        {
            var w = Sim.NewDuel(seed, SeatKind.Ai, SeatKind.Ai, a, b);
            var ls = new LocalSession(w, "SPECTATE  " + Presets.Get(a, 0).Name + " vs " + Presets.Get(b, 0).Name) { CanCommand = false, Speed = speed };
            SetSession(ls, true);
            again = StartSpectate;
            spectating = true;
            Ui.Log("PROCTOR: Two metaprograms, no humans. Press Tab to read their source. +/- changes speed.", "amber");
        }

        public void StartSkirmish()
        {
            var ns = new NetSession(Settings, n => n.CreateVsAi(Settings.AiMeta));
            Hook(ns);
            SetSession(ns, true);
            again = StartSkirmish;
            Ui.Log("connecting to " + Settings.Uri + " / " + Settings.Database + " ...", "dim");
            Intro();
        }

        public void HostDuel()
        {
            var ns = new NetSession(Settings, n => n.HostDuel());
            Hook(ns);
            SetSession(ns, true);
            again = HostDuel;
            Ui.Log("opening a lobby on " + Settings.Uri + " ...", "dim");
        }

        public void JoinDuel(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) { Ui.Toast("enter the host's 4-letter code"); return; }
            var ns = new NetSession(Settings, n => n.Join(code));
            Hook(ns);
            SetSession(ns, true);
            again = null;
            Ui.Log("joining " + code.ToUpperInvariant() + " ...", "dim");
        }

        void Hook(NetSession ns) { ns.OnMessage = m => { Ui.Log(m, "hot"); Ui.Toast(m, 4f); }; }

        public void StartGym(int arenas)
        {
            SetSession(new GymSession(arenas), true);
            again = () => StartGym(arenas);
            Ui.Log("GYM: " + arenas + " arenas of commander self-play on the shared simulation.", "amber");
            Ui.Log("train: run  mlagents-learn ml/config/commander.yaml --run-id=argmax  then start the gym. See ml/README.md", "dim");
        }

        public void BackToMenu()
        {
            Campaign.Exit();
            Campaign.HideAll();
            if (Session is NetSession ns) ns.Leave();
            Ui.ShowMenu();
            StartAttract();
        }

        /// <summary>G: MORPH (raymarched matter) or GRID (flat Bauhaus shapes).</summary>
        public void ToggleLook()
        {
            if (!View.MorphAvailable) { Ui.Toast("MORPH needs a GPU that can raymarch; staying in GRID"); return; }
            Settings.Morph = View.Morph = !View.Morph;
            Settings.Save();
            Ui.Toast(View.Morph ? "MORPH: the swarm as matter" : "GRID: the swarm as shapes");
            Ui.RefreshLook();
        }

        // ================================================================ campaign

        public void StartCampaign() => Campaign.ShowMap();

        public void ContinueCampaign() => Campaign.Enter(Progress.NextLevel());

        /// <summary>The campaign swaps worlds on every RUN/RESET; keep the camera where the player put it.</summary>
        static string FrameKey(World w) => (w.Chamber != null ? w.Chamber.Id : -1) + ":" + w.W + "x" + w.H;

        public void RunCampaignSession(LocalSession ls, bool frame)
        {
            bool keepCamera = !frame && framedFor == FrameKey(ls.W);
            SetSession(ls, true);
            again = null;
            if (keepCamera) framed = true;
        }

        void Intro()
        {
            Ui.Log("PROCTOR: Connection established. Your swarm is listening. It will not listen well.", "amber");
            Ui.Log("Your motes run Argot programs (Tab). Right-click moves them by hand. Everything else, they decide.", "dim");
        }

        // ================================================================ commands from the UI

        Cmd Mine(CmdKind kind) => new Cmd { Seat = (byte)(Session?.LocalSeat ?? 0), Kind = kind };

        public void SetProduction(int slot)
        {
            if (Session == null || !Session.CanCommand) return;
            var c = Mine(CmdKind.Produce); c.Arg = (uint)slot; Session.Issue(c);
        }

        public void Reprogram(Program p)
        {
            if (p.Kind != ProgramKind.Unit) { Ui.Toast("that is a metaprogram; use RUN AS METAPROGRAM"); return; }
            if (View.Selected.Count == 0) { Ui.Toast("select motes first"); return; }
            var ids = new uint[View.Selected.Count]; View.Selected.CopyTo(ids);
            var c = Mine(CmdKind.Reprogram); c.Arg = p.Id; c.Ids = ids; Session.Issue(c);
            Ui.Log("reprogrammed " + ids.Length + " motes to " + p.Name, "good");
        }

        public void SetMeta(Program p)
        {
            if (p.Kind != ProgramKind.Meta) { Ui.Toast("add @meta at the top: metaprograms steer the seat, not units"); return; }
            var c = Mine(CmdKind.SetMeta); c.Arg = p.Id; Session.Issue(c);
            Ui.Log("metaprogram " + p.Name + " now runs your macro, once per second. You are the self-metaprogrammer.", "good");
        }

        public void SetSlot(int slot, Program p)
        {
            if (p.Kind != ProgramKind.Unit) { Ui.Toast("slots hold unit programs"); return; }
            var c = Mine(CmdKind.SetSlot); c.X = (short)slot; c.Arg = p.Id; Session.Issue(c);
            Ui.Log("slot " + (slot + 1) + " now produces " + p.Name + " (cost " + p.SpawnCost + ")", "good");
        }

        // ================================================================ chambers

        public string ChamberDraft(int id) => PlayerPrefs.GetString("argmax.chamber.draft." + id, Chambers.Get(id).Starter);
        public void SaveChamberDraft(int id, string src) => PlayerPrefs.SetString("argmax.chamber.draft." + id, src);
        public string BestLocal(int id) { var s = PlayerPrefs.GetString("argmax.chamber.best." + id, ""); return s.Length > 0 ? s : null; }

        public void RecordLocal(int id, ChamberResult r)
        {
            int best = PlayerPrefs.GetInt("argmax.chamber.bestTicks." + id, int.MaxValue);
            if (r.Ticks >= best) return;
            PlayerPrefs.SetInt("argmax.chamber.bestTicks." + id, r.Ticks);
            PlayerPrefs.SetString("argmax.chamber.best." + id, r.Ticks + " ticks / " + r.Symbols + " symbols / " + r.Lost + " lost");
            PlayerPrefs.Save();
        }

        public void RunChamber(int id, string source, float speed)
        {
            var spec = Chambers.Get(id);
            var c = Argot.Compile(source, "SOLUTION");
            if (!c.Ok) { Ui.Toast(string.Join("  ", c.Errors)); return; }
            if (c.Program.Kind != ProgramKind.Unit) { Ui.Toast("chambers take a unit program"); return; }
            SaveChamberDraft(id, source);
            c.Program.Id = Rules.ChamberProgramId;
            var w = Chambers.Create(spec, c.Program);
            var ls = new LocalSession(w, "CHAMBER " + id.ToString("00") + "  " + spec.Title) { CanCommand = false, Speed = speed };
            SetSession(ls, true);
            chamberId = id;
            again = () => RunChamber(id, source, speed);
            foreach (var line in spec.Proctor) Ui.Log("PROCTOR: " + line, "amber");
        }

        public IEnumerable<ChamberRecordRow> Records(int chamber)
        {
            var ns = Session as NetSession ?? records;
            return ns != null && ns.Connected ? ns.Records(chamber) : null;
        }

        public void SubmitChamber(int id, string source)
        {
            var r = Chambers.Run(Chambers.Get(id), source);
            if (r.Error != null || !r.Success) { Ui.Toast("PROCTOR only records passing solutions. Run it first."); return; }
            RecordLocal(id, r);
            if (Session is NetSession live && live.Connected) { live.SubmitChamber(id, source); return; }
            records?.Dispose();
            records = new NetSession(Settings, n => { n.SubmitChamber(id, source); });
            records.OnMessage = m => { Ui.Toast(m, 4f); Ui.RefreshRecords(); };
            Ui.Toast("submitting to " + Settings.Uri + " ...");
        }

        // ================================================================ narration & endings

        void Narrate(ISession s)
        {
            var w = s.World;
            int seat = s.LocalSeat < 2 ? s.LocalSeat : 0;
            var t = Sim.Tally(w, seat);
            if (t.MaxTier > maxTierSeen)
            {
                string line = null;
                switch (t.MaxTier)
                {
                    case 3: line = "An eight. Adorable."; break;
                    case 5: line = "Thirty-two. The old minds started like this. Then they kept going."; break;
                    case 7: line = "One hundred and twenty-eight. Please do not name it."; break;
                    case 9: line = "Five hundred and twelve. I am obligated to remind you how this ended last time."; break;
                    case 10: line = "One more fusion and it is a Singularity. I will start the paperwork."; break;
                }
                if (line != null && maxTierSeen > 0) Ui.Log("PROCTOR: " + line, "amber");
                maxTierSeen = t.MaxTier;
            }
            var me = w.Seats[seat];
            if (me.HasCore && w.Mode == WorldMode.Duel)
            {
                int pct = 100 * me.CoreHp / Rules.CoreHp;
                if (pct < 50 && coreWarned < 1) { coreWarned = 1; Ui.Log("PROCTOR: Your core is at half. That is the bad half.", "hot"); }
                if (pct < 20 && coreWarned < 2) { coreWarned = 2; Ui.Log("PROCTOR: Twenty percent. I have prepared a short eulogy. It is mostly numbers.", "hot"); }
            }
            if (me.SingularityTicks > 0 && !singularityWarned) { singularityWarned = true; Ui.Log("PROCTOR: Singularity forming. Hold it for fifteen seconds and the Deadnet is yours.", "good"); }
            var foe = w.Seats[1 - seat];
            if (foe.SingularityTicks > 0 && !enemySingularityWarned) { enemySingularityWarned = true; Ui.Log("PROCTOR: Their singularity is forming. This would be an excellent time to panic, productively.", "hot"); }
        }

        void CheckEnd(ISession s)
        {
            var w = s.World;
            if (endShown || !w.Ended) return;
            endShown = true;
            if (Campaign.Active && w.Mode == WorldMode.Chamber) { Campaign.OnEnd(w); return; }
            int seat = s.LocalSeat;
            if (w.Mode == WorldMode.Chamber)
            {
                var spec = w.Chamber;
                bool pass = w.Winner == 0;
                var src = ChamberDraft(chamberId);
                var c = Argot.Compile(src);
                if (pass) RecordLocal(chamberId, new ChamberResult { Success = true, Ticks = w.Tick, Symbols = c.Ok ? c.Program.Symbols : 0, Lost = w.Seats[0].Lost });
                Ui.Log(pass ? "PROCTOR: Test passed. I will add it to your file. Your file is very small." : "PROCTOR: Test failed. The swarm did exactly what you wrote. That is the problem.", pass ? "good" : "hot");
                int id = chamberId;
                Ui.ShowEnd(pass ? "PASSED" : "FAILED", pass ? Palette.Noise : Palette.Foe,
                    spec.Title + "\nprogress " + Chambers.Progress(w) + " / " + spec.Target + "    ticks " + w.Tick + "    symbols " + (c.Ok ? c.Program.Symbols : 0) + "    lost " + w.Seats[0].Lost,
                    ("EDIT", () => Ui.ShowChambers(id)),
                    ("RETRY", () => again?.Invoke()),
                    ("SUBMIT RECORD", () => SubmitChamber(id, src)),
                    ("MENU", BackToMenu));
                return;
            }
            if (s is GymSession) return;
            if (spectating)
            {
                Ui.ShowEnd(w.Winner < 2 ? (w.Winner == 0 ? "LEFT WINS" : "RIGHT WINS") : "DRAW", Palette.Scrap, s.Title + "\n" + (w.Tick / Rules.TickHz) + " s",
                    ("AGAIN", () => again?.Invoke()), ("MENU", BackToMenu));
                return;
            }
            string title; Color col;
            if (w.Winner == seat) { title = "VICTORY"; col = Palette.Me; Ui.Log("PROCTOR: Victory. The swarm did what you wrote. Remember that it always will.", "good"); }
            else if (w.Winner == Rules.Draw || w.Winner >= Rules.SeatCount) { title = "DRAW"; col = Palette.Scrap; Ui.Log("PROCTOR: A draw. Nothing was learned. Statistically.", "amber"); }
            else { title = "DEFEAT"; col = Palette.Foe; Ui.Log("PROCTOR: Defeat. Somewhere a gradient descended without you.", "hot"); }
            var me = w.Seats[seat < 2 ? seat : 0];
            var foe = w.Seats[seat < 2 ? 1 - seat : 1];
            int secs = w.Tick / Rules.TickHz;
            Ui.ShowEnd(title, col,
                $"{secs / 60}:{secs % 60:00}   |   you: mined {me.Mined}  killed {me.Killed}  lost {me.Lost}   |   them: mined {foe.Mined}  killed {foe.Killed}  lost {foe.Lost}",
                ("AGAIN", () => again?.Invoke()),
                ("MENU", BackToMenu));
        }
    }
}
