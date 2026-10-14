using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Argmax.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArgmaxClient
{
    /// <summary>
    /// The whole interface in UI Toolkit, built in code and styled by Resources/Argmax/UI/argmax.uss.
    /// A thin view: it reads the session and calls back into <see cref="App"/>.
    /// </summary>
    public sealed class Ui
    {
        readonly App app;
        public VisualElement Root, Labels;
        VisualElement menu, hud, editor, chambers, endgame, selectRect, topbar, bottombar, terminal, inspector;
        Label menuStatus, scrapV, motesV, valueV, timeV, statusV, banner, toast, cursorHint, selectionL, pilotL, hintL, myHpL, foeHpL;
        Label singularityL, prodL, endTitle, endStats, compileL, chamberTitle, chamberBrief, chamberResult, inspectTitle, sensorsL, weightsL;
        VisualElement myHp, foeHp, terminalLines, slotsRow, jevGrid, rulesBox, libList, proctorBox, chamberList, recordsBox, endButtons;
        Label[] jevCells = new Label[9];
        TextField nameField, codeField, jevField, code, chamberCode, dbField;
        Button serverBtn, aiBtn, pilotBtn, soundBtn, musicBtn, campaignBtn, lookBtn;
        Label campaignL;
        VisualElement uriRow;
        TextField uriField;
        readonly List<(VisualElement el, Label name, Label cost)> slots = new List<(VisualElement, Label, Label)>();
        float toastUntil, nextHud;
        int chamberSel = 1;
        bool inspectorOn = true;
        string lastCompiled;
        Argot.Result compiled;

        public Ui(App app) { this.app = app; }

        public void Build(UIDocument doc)
        {
            Root = doc.rootVisualElement;
            Root.AddToClassList("root");
            Root.pickingMode = PickingMode.Ignore;
            var uss = Resources.Load<StyleSheet>("Argmax/UI/argmax");
            if (uss != null) Root.styleSheets.Add(uss);

            Labels = Screen("labels"); Labels.pickingMode = PickingMode.Ignore;
            BuildHud();
            BuildEditor();
            BuildMenu();
            BuildChambers();
            BuildEndgame();
            selectRect = new VisualElement(); selectRect.AddToClassList("select-rect"); selectRect.pickingMode = PickingMode.Ignore; Root.Add(selectRect);
            cursorHint = L("", "cursor-hint"); cursorHint.pickingMode = PickingMode.Ignore; Root.Add(cursorHint);
            toast = L("", "toast"); toast.pickingMode = PickingMode.Ignore; Root.Add(toast);
            ShowMenu();
        }

        // ================================================================ helpers

        VisualElement Screen(string name)
        {
            var v = new VisualElement { name = name };
            v.AddToClassList("screen");
            v.pickingMode = PickingMode.Ignore;
            Root.Add(v);
            return v;
        }

        static Label L(string text, string cls = null)
        {
            var l = new Label(text);
            if (cls != null) foreach (var c in cls.Split(' ')) l.AddToClassList(c);
            return l;
        }

        static Button B(string text, Action onClick, string cls = "btn")
        {
            var b = new Button(() => { Audio.Play(Sfx.Click); onClick(); }) { text = text };
            foreach (var c in cls.Split(' ')) b.AddToClassList(c);
            return b;
        }

        static VisualElement Row(params VisualElement[] kids)
        {
            var r = new VisualElement();
            r.AddToClassList("row");
            foreach (var k in kids) r.Add(k);
            return r;
        }

        static VisualElement Panel(string title, string cls = null)
        {
            var p = new VisualElement();
            p.AddToClassList("panel");
            if (cls != null) foreach (var c in cls.Split(' ')) p.AddToClassList(c);
            if (title != null) p.Add(L(title, "panel-title"));
            return p;
        }

        static TextField Field(string value, bool multiline = false)
        {
            var t = new TextField { value = value, multiline = multiline };
            t.AddToClassList("grow");
            return t;
        }

        static void Show(VisualElement v, bool on) { if (v != null) v.EnableInClassList("hidden", !on); }

        // ================================================================ menu

        void BuildMenu()
        {
            menu = Screen("menu");
            menu.AddToClassList("menu");
            menu.pickingMode = PickingMode.Position;
            var box = new VisualElement(); box.AddToClassList("menu-box"); menu.Add(box);
            var hero = new VisualElement(); hero.AddToClassList("menu-hero");
            var comp = new Composition(); comp.style.position = Position.Absolute; comp.style.left = 0; comp.style.right = 0; comp.style.top = 0; comp.style.bottom = 0;
            hero.Add(comp);
            var logo = new Logotype(); logo.style.position = Position.Absolute; logo.style.left = 0; logo.style.top = 0; logo.style.bottom = 0; logo.style.width = Length.Percent(66);
            hero.Add(logo);
            box.Add(hero);
            box.Add(L("you don't command the swarm. you write the function it maximizes.", "tagline voice"));

            var s = app.Settings;
            var camp = Panel("BOOT SEQUENCE  //  THE CAMPAIGN", "menu-campaign");
            campaignL = L("", "dim");
            camp.Add(L("New here? Start with this. 28 tests that teach the game, a little computer science, and why PROCTOR is the way it is.", "menu-campaign-text body"));
            campaignBtn = B("BEGIN", () => app.ContinueCampaign(), "btn btn-hot");
            camp.Add(Row(campaignBtn, B("CURRICULUM MAP", () => app.StartCampaign()), campaignL));
            box.Add(camp);

            var conn = Panel("CONNECTION  (self-hosted: run your own SpacetimeDB, see the README)");
            serverBtn = B("", () =>
            {
                var choices = Settings.ServerChoices;
                s.Server = choices[(Array.IndexOf(choices, s.Server) + 1) % choices.Length];
                RefreshMenu();
            }, "btn");
            dbField = Field(s.Database);
            dbField.RegisterValueChangedCallback(e => { if (s.Server == "maincloud") s.MaincloudDb = e.newValue; else if (s.Server == "custom") s.CustomDb = e.newValue; else s.LocalDb = e.newValue; });
            nameField = Field(s.Name);
            nameField.RegisterValueChangedCallback(e => s.Name = e.newValue);
            conn.Add(Row(L("server", "field-label"), serverBtn, L("db", "dim"), dbField));
            uriField = Field(s.CustomUri);
            uriField.RegisterValueChangedCallback(e => s.CustomUri = e.newValue.Trim());
            uriRow = Row(L("uri", "field-label"), uriField);
            conn.Add(uriRow);
            conn.Add(Row(L("name", "field-label"), nameField));
            jevField = new TextField { value = s.JevKeyEntered, isPasswordField = true };
            jevField.AddToClassList("grow");
            jevField.RegisterValueChangedCallback(e => s.JevKeyEntered = e.newValue);
            conn.Add(Row(L("jev key", "field-label"), jevField));
            soundBtn = B("", () => { s.Muted = !s.Muted; Audio.Muted = s.Muted; RefreshMenu(); }, "btn btn-small");
            musicBtn = B("", () => { s.MusicOn = !s.MusicOn; Audio.Music = s.MusicOn ? 0.45f : 0f; RefreshMenu(); }, "btn btn-small");
            lookBtn = B("", () => app.ToggleLook(), "btn btn-small");
            conn.Add(Row(L("sound", "field-label"), soundBtn, musicBtn, L("   look", "field-label"), lookBtn));
            box.Add(conn);

            var grid = new VisualElement(); grid.AddToClassList("menu-grid"); box.Add(grid);
            var sk = Panel("SKIRMISH", "menu-card");
            sk.Add(L("You vs an AI on SpacetimeDB. The server runs its metaprogram, or you host a brain.", "dim"));
            aiBtn = B("", () => { s.AiMeta = Next(Presets.MetaIds, s.AiMeta); RefreshMenu(); }, "btn btn-small");
            pilotBtn = B("", () => { s.Pilot = (PilotKind)(((int)s.Pilot + 1) % 3); RefreshMenu(); }, "btn btn-small");
            sk.Add(Row(L("enemy", "dim"), aiBtn, L("brain", "dim"), pilotBtn));
            sk.Add(B("START SKIRMISH", () => app.StartSkirmish(), "btn btn-hot"));
            grid.Add(sk);

            var du = Panel("DUEL", "menu-card");
            du.Add(L("Two humans, one server. Host and share the code, or join one.", "dim"));
            codeField = Field("");
            du.Add(Row(B("HOST", () => app.HostDuel()), L("code", "dim"), codeField, B("JOIN", () => app.JoinDuel(codeField.value))));
            grid.Add(du);

            var ch = Panel("CHAMBERS", "menu-card");
            ch.Add(L("PROCTOR's test chambers. One program, no hands. Scored on ticks and symbols; the server verifies records.", "dim"));
            ch.Add(B("ENTER", () => ShowChambers()));
            grid.Add(ch);

            var of = Panel("OFFLINE", "menu-card");
            of.Add(L("Sandbox vs the AI with the simulation running in this client, or the ML-Agents gym.", "dim"));
            of.Add(Row(B("SANDBOX", () => app.StartSandbox()), B("WATCH AI vs AI", () => app.StartSpectate()), B("GYM x8", () => app.StartGym(8))));
            grid.Add(of);

            menuStatus = L("", "dim");
            box.Add(menuStatus);
            box.Add(L("controls: drag select | right-click MOVE | A+click ATTACK | M merge | X split | S stop | H hold | B beacon | Q..Y production | Shift+Q..Y reprogram | Tab code | F fields | I inspector | Space home", "hint"));
            RefreshMenu();
        }

        static uint Next(uint[] ids, uint cur)
        {
            int i = Array.IndexOf(ids, cur);
            return ids[(i + 1) % ids.Length];
        }

        void RefreshMenu()
        {
            var s = app.Settings;
            serverBtn.text = s.Server == "local" ? "LOCAL  127.0.0.1:3000" : s.Server == "custom" ? "CUSTOM SERVER" : "MAINCLOUD (dev)";
            dbField.SetValueWithoutNotify(s.Database);
            Show(uriRow, s.Server == "custom");
            soundBtn.text = s.Muted ? "SOUND OFF" : "SOUND ON";
            RefreshLook();
            musicBtn.text = s.MusicOn ? "MUSIC ON" : "MUSIC OFF";
            var p = app.Progress;
            if (p != null)
            {
                int solved = p.SolvedCount;
                campaignBtn.text = solved == 0 ? "BEGIN" : solved >= Campaign.Levels.Length ? "REPLAY THE FINALE" : "CONTINUE  " + Campaign.Get(p.NextLevel()).Code;
                campaignL.text = solved == 0 ? "" : "   " + solved + " / " + Campaign.Levels.Length + " tests   " + p.StarCount + " stars";
            }
            aiBtn.text = Presets.Get(s.AiMeta, 0)?.Name ?? "OVERMIND";
            pilotBtn.text = s.Pilot == PilotKind.Server ? "server metaprogram" : s.Pilot == PilotKind.Ml ? "ML-Agents (local)" : "JEV System 1 (local)";
        }

        public void ShowMenu()
        {
            app.Campaign?.HideAll();
            CampaignMode(false);
            Show(menu, true); Show(hud, false); Show(editor, false); Show(chambers, false); Show(endgame, false);
            RefreshMenu();
        }

        public void MenuStatus(string s) { if (menuStatus != null) menuStatus.text = s; }

        public void RefreshLook() { if (lookBtn != null) lookBtn.text = app.View != null && app.View.Morph ? "MORPH  (G)" : "GRID  (G)"; }

        /// <summary>Hide every full-screen page (the campaign shows its own).</summary>
        public void HideScreens()
        {
            Show(menu, false); Show(hud, false); Show(editor, false); Show(chambers, false); Show(endgame, false);
        }

        /// <summary>In a campaign test the briefing and editor replace the terminal and the sandbox editor.</summary>
        public void CampaignMode(bool on)
        {
            Root.EnableInClassList("campaign", on);
            Show(terminal, !on);
            if (on) Show(editor, false);
        }

        public void BringOverlaysToFront()
        {
            selectRect.BringToFront(); cursorHint.BringToFront(); toast.BringToFront();
        }

        // ================================================================ hud

        void BuildHud()
        {
            hud = Screen("hud");
            topbar = Panel(null, "topbar"); topbar.pickingMode = PickingMode.Position;
            VisualElement Stat(string k, out Label v) { var c = new VisualElement(); c.AddToClassList("stat"); c.Add(L(k, "stat-k")); v = L("-", "stat-v"); c.Add(v); return c; }
            topbar.Add(Stat("SCRAP", out scrapV));
            topbar.Add(Stat("MOTES", out motesV));
            topbar.Add(Stat("VALUE", out valueV));
            topbar.Add(Stat("PRODUCING", out prodL));
            var hpBox = new VisualElement(); hpBox.AddToClassList("stat");
            myHpL = L("CORE", "stat-k"); hpBox.Add(myHpL);
            var bar = new VisualElement(); bar.AddToClassList("bar"); myHp = new VisualElement(); myHp.AddToClassList("bar-fill"); bar.Add(myHp); hpBox.Add(bar);
            foeHpL = L("ENEMY CORE", "stat-k"); hpBox.Add(foeHpL);
            var bar2 = new VisualElement(); bar2.AddToClassList("bar"); bar2.AddToClassList("bar-foe"); foeHp = new VisualElement(); foeHp.AddToClassList("bar-fill"); bar2.Add(foeHp); hpBox.Add(bar2);
            topbar.Add(hpBox);
            topbar.Add(Stat("TIME", out timeV));
            singularityL = L("", "hot"); topbar.Add(singularityL);
            var spacer = new VisualElement(); spacer.AddToClassList("grow"); topbar.Add(spacer);
            statusV = L("", "dim"); topbar.Add(statusV);
            topbar.Add(B("CODE", () => ToggleEditor(), "btn btn-small"));
            topbar.Add(B("MENU", () => app.BackToMenu(), "btn btn-small"));
            hud.Add(topbar);

            banner = L("", "banner"); banner.pickingMode = PickingMode.Ignore; hud.Add(banner);

            terminal = Panel("TERMINAL", "terminal"); terminal.pickingMode = PickingMode.Position;
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            terminalLines = scroll.contentContainer;
            terminal.Add(scroll);
            pilotL = L("", "terminal-line amber"); terminal.Add(pilotL);
            hud.Add(terminal);

            inspector = Panel("JEV VIEW", "inspector"); inspector.pickingMode = PickingMode.Position;
            inspectTitle = L("select one mote to see its next decision", "dim");
            inspectTitle.style.whiteSpace = WhiteSpace.Normal;
            inspector.Add(inspectTitle);
            var irow = new VisualElement(); irow.AddToClassList("row");
            jevGrid = new VisualElement(); jevGrid.AddToClassList("jev-grid");
            // grid shown north-up: rows dy=+1..-1
            int[] order = { 6, 7, 8, 3, 4, 5, 0, 1, 2 };
            var cells = new Label[9];
            for (int i = 0; i < 9; i++) { var c = L("", "jev-cell"); cells[order[i]] = c; jevGrid.Add(c); }
            jevCells = cells;
            irow.Add(jevGrid);
            var right = new VisualElement(); right.AddToClassList("grow");
            sensorsL = L("", "rule-line"); weightsL = L("", "rule-line dim");
            rulesBox = new VisualElement();
            right.Add(sensorsL); right.Add(rulesBox); right.Add(weightsL);
            irow.Add(right);
            inspector.Add(irow);
            hud.Add(inspector);

            bottombar = Panel(null, "bottombar"); bottombar.pickingMode = PickingMode.Position;
            slotsRow = new VisualElement(); slotsRow.AddToClassList("row");
            string keys = "QWERTY";
            for (int i = 0; i < Rules.LoadoutSlots; i++)
            {
                int slot = i;
                var el = new VisualElement(); el.AddToClassList("slot");
                el.Add(L(keys[i] + "  slot " + (i + 1), "slot-key"));
                var n = L("-", "slot-name"); var c = L("", "slot-cost");
                el.Add(n); el.Add(c);
                el.RegisterCallback<ClickEvent>(e => app.SetProduction(slot));
                slotsRow.Add(el);
                slots.Add((el, n, c));
            }
            bottombar.Add(slotsRow);
            selectionL = L("", "dim"); bottombar.Add(selectionL);
            hintL = L("", "hint"); bottombar.Add(hintL);
            hud.Add(bottombar);
        }

        public void ShowGame()
        {
            Show(menu, false); Show(hud, true); Show(chambers, false); Show(endgame, false);
            terminalLines.Clear();
        }

        public void Log(string line, string cls = null)
        {
            if (terminalLines == null) return;
            var l = L(line, "terminal-line" + (cls != null ? " " + cls : ""));
            terminalLines.Add(l);
            while (terminalLines.childCount > 80) terminalLines.RemoveAt(0);
            terminalLines.parent?.parent?.schedule.Execute(() => { if (terminalLines.parent?.parent is ScrollView sv) sv.scrollOffset = new Vector2(0, float.MaxValue); });
        }

        public void Toast(string s, float seconds = 2.5f) { toast.text = s; toastUntil = Time.unscaledTime + seconds; }

        public void Update(ISession s, WorldView view, CommandInput input)
        {
            if (Time.unscaledTime > toastUntil && toast.text.Length > 0) toast.text = "";
            Show(selectRect, input.Dragging);
            if (input.Dragging)
            {
                var a = ToPanel(input.DragFrom); var b = ToPanel(input.DragTo);
                selectRect.style.left = Mathf.Min(a.x, b.x); selectRect.style.top = Mathf.Min(a.y, b.y);
                selectRect.style.width = Mathf.Abs(a.x - b.x); selectRect.style.height = Mathf.Abs(a.y - b.y);
            }
            var cm = input.Mode;
            cursorHint.text = cm == CommandInput.Cursor.Attack ? "ATTACK: click a target cell" : cm == CommandInput.Cursor.Beacon ? "BEACON: click to place" : "";
            if (cursorHint.text.Length > 0)
            {
                var p = ToPanel(Input.mousePosition);
                cursorHint.style.left = p.x + 16; cursorHint.style.top = p.y + 8;
            }
            if (s == null || hud.ClassListContains("hidden")) return;
            if (Time.unscaledTime < nextHud) return;
            nextHud = Time.unscaledTime + 0.1f;
            Hud(s, view);
        }

        void Hud(ISession s, WorldView view)
        {
            var w = s.World;
            statusV.text = s.Status;
            banner.text = s.Banner ?? "";
            pilotL.text = s.PilotReadout ?? "";
            if (w == null) return;
            int seat = s.LocalSeat < 2 ? s.LocalSeat : 0;
            var me = w.Seats[seat];
            var foe = w.Seats[1 - seat];
            var t = Sim.Tally(w, seat);
            scrapV.text = me.Scrap.ToString();
            motesV.text = t.Count.ToString();
            valueV.text = t.Value.ToString() + (t.MaxTier > 0 ? "  max " + Rules.Value(t.MaxTier) : "");
            var prog = w.GetProgram(me.Loadout[Math.Min(me.Production, (byte)5)]);
            prodL.text = (prog != null ? prog.Name : "-") + (me.Auto ? "" : " (paused)");
            int secs = w.Tick / Rules.TickHz;
            timeV.text = (secs / 60) + ":" + (secs % 60).ToString("00");
            myHp.style.width = Length.Percent(me.HasCore ? 100f * me.CoreHp / Rules.CoreHp : 0);
            foeHp.style.width = Length.Percent(foe.HasCore ? 100f * Mathf.Clamp01(foe.CoreHp / (float)Mathf.Max(1, w.Chamber != null && w.Chamber.Goal == ChamberGoal.Siege ? w.Chamber.Target : Rules.CoreHp)) : 0);
            myHpL.text = "CORE " + me.CoreHp;
            foeHpL.text = foe.HasCore ? "ENEMY CORE " + foe.CoreHp : "";
            singularityL.text = me.SingularityTicks > 0 ? "SINGULARITY " + (100 * me.SingularityTicks / Rules.SingularityHold) + "%" :
                foe.SingularityTicks > 0 ? "ENEMY SINGULARITY " + (100 * foe.SingularityTicks / Rules.SingularityHold) + "%" : "";
            Show(slotsRow, s.CanCommand);
            for (int i = 0; i < slots.Count; i++)
            {
                var p = w.GetProgram(me.Loadout[i]);
                slots[i].name.text = p != null ? p.Name : "-";
                slots[i].cost.text = p != null ? "cost " + p.SpawnCost + "  rules " + p.RuleCount : "";
                slots[i].el.EnableInClassList("slot-active", me.Production == i);
            }
            // selection summary by value, like a 2048 board
            if (view.Selected.Count > 0)
            {
                var byTier = new int[Rules.MaxTier + 1];
                foreach (var id in view.Selected) { int j = w.FindMote(id); if (j >= 0) byTier[w.Motes[j].Tier]++; }
                var sb = new StringBuilder("selected " + view.Selected.Count + ":  ");
                for (int k = 1; k <= Rules.MaxTier; k++) if (byTier[k] > 0) sb.Append(Rules.Value(k)).Append('x').Append(byTier[k]).Append("  ");
                selectionL.text = sb.ToString();
            }
            else selectionL.text = s.CanCommand ? "drag to select your motes" : app.Campaign.Active ? "click or drag motes to watch them decide (JEV VIEW)" : "spectating";
            hintL.text = s.CanCommand
                ? "RMB move | A attack | M merge | X split | S stop | H hold | B beacon | Z spawn | U auto | Tab code | G look | F field: " + WorldView.FieldNames[view.FieldMode]
                : app.Campaign.Active ? "F field: " + WorldView.FieldNames[view.FieldMode] + " | G look | +/- speed | P pause | I inspector | arrows / wheel: camera"
                : "F field: " + WorldView.FieldNames[view.FieldMode] + " | +/- speed | P pause | [ ] arena | I inspector";
            // in the campaign the inspector only appears when there is something to inspect
            bool something = view.Selected.Count == 1 || view.Hovered != 0;
            Show(inspector, inspectorOn && (something || !app.Campaign.Active));
            if (inspectorOn) Inspect(w, view, s.LocalSeat);
        }

        void Inspect(World w, WorldView view, int seat)
        {
            uint id = view.Selected.Count == 1 ? view.Selected.First() : view.Hovered;
            if (id == 0) { inspectTitle.text = "select or hover a mote: see the softmax over its 9 tiles"; foreach (var c in jevCells) c.text = ""; sensorsL.text = ""; weightsL.text = ""; rulesBox.Clear(); return; }
            int i = w.FindMote(id);
            if (i < 0) return;
            var m = w.Motes[i];
            var a = Sim.Analyze(w, id);
            if (!a.Valid) return;
            var p = w.GetProgram(m.Program);
            inspectTitle.text = (m.Seat == Rules.NoiseSeat ? "feral " : m.Seat == seat ? "" : "enemy ") + Rules.Value(m.Tier) + "  #" + m.Id + "  " + (p != null ? p.Name : "no program") +
                "  hp " + m.Hp + "/" + Rules.MaxHp(m.Tier) + (m.Override > 0 ? "  [MANUAL]" : "") + "  T=" + a.Temperature;
            for (int k = 0; k < 9; k++)
            {
                var c = jevCells[k];
                c.text = a.Scores[k] == int.MinValue ? "x" : (a.Permille[k] / 10f).ToString("0") + "%";
                float pr = a.Permille[k] / 1000f;
                c.style.backgroundColor = new Color(0f, 0.9f, 1f, 0.05f + pr * 0.6f);
                c.EnableInClassList("jev-pick", k == a.Pick);
            }
            var sb = new StringBuilder("sensing: ");
            for (int b = 0; b < Argot.UnitSensors.Length; b++) if ((a.Sensors & (1u << b)) != 0) sb.Append(Argot.UnitSensors[b]).Append(' ');
            sensorsL.text = sb.ToString();
            rulesBox.Clear();
            if (p != null)
                for (int r = 0; r < p.Chips.Length; r++)
                    rulesBox.Add(L((a.FiredRules.Contains(r) ? "> " : "  ") + Argot.Rule(p.Chips[r], false), "rule-line " + (a.FiredRules.Contains(r) ? "rule-fired" : "rule-idle")));
            var ws = new StringBuilder("weights ");
            for (int c = 0; c < (int)Channel.Count; c++) if (a.Weights[c] != 0) ws.Append(((Channel)c).ToString().ToLowerInvariant()).Append(' ').Append(a.Weights[c]).Append("  ");
            weightsL.text = ws.ToString();
        }

        public void ToggleInspector() { inspectorOn = !inspectorOn; }

        Vector2 ToPanel(Vector2 screen) => Root.panel == null ? screen : RuntimePanelUtils.ScreenToPanel(Root.panel, new Vector2(screen.x, UnityEngine.Screen.height - screen.y));

        public bool PointerOverUi()
        {
            if (Root?.panel == null) return false;
            var mp = Input.mousePosition;
            var picked = Root.panel.Pick(ToPanel(mp));
            return picked != null;
        }

        public bool Typing()
        {
            var fe = Root?.panel?.focusController?.focusedElement as VisualElement;
            if (fe == null) return false;
            return fe is TextField || fe.GetFirstAncestorOfType<TextField>() != null;
        }

        // ================================================================ code editor

        void BuildEditor()
        {
            editor = Panel("ARGOT  //  program editor (Tab)", "editor");
            editor.pickingMode = PickingMode.Position;
            code = Field(Presets.Get(Presets.Swarm, 0).Source, true);
            code.AddToClassList("code");
            code.RegisterValueChangedCallback(e => Recompile(e.newValue));
            editor.Add(code);
            compileL = L("", "compile-ok");
            editor.Add(compileL);
            var actions = Row(
                B("SAVE", () => Save(null)),
                B("SAVE + REPROGRAM SELECTION", () => Save(p => app.Reprogram(p))),
                B("SAVE + RUN AS METAPROGRAM", () => Save(p => app.SetMeta(p))));
            editor.Add(actions);
            var slotRow = Row(L("save into slot:", "dim"));
            for (int i = 0; i < Rules.LoadoutSlots; i++) { int slot = i; slotRow.Add(B((i + 1).ToString(), () => Save(p => app.SetSlot(slot, p)), "btn btn-small")); }
            editor.Add(slotRow);
            editor.Add(L("LIBRARY  (click to load)", "panel-title"));
            var libScroll = new ScrollView(); libScroll.style.maxHeight = 130; libList = libScroll.contentContainer; editor.Add(libScroll);
            var refText = new StringBuilder("SENSORS  ");
            for (int i = 0; i < Argot.UnitSensors.Length; i++) refText.Append(Argot.UnitSensors[i]).Append(i % 5 == 4 ? "\n" : "  ");
            refText.Append("\nACTIONS  seek|avoid <order enemy scrap home beacon core allies trail> [1-9]  flock  spread  form <checker rows cols grid diag>  merge  split  mine  hold  wander  infect  mark  holdfire");
            refText.Append("\n@meta  produce <slot|name>  spawn <n>  beacon <home front enemy scrap wild clear>  army <attack retreat merge split hold free>");
            editor.Add(L(refText.ToString(), "ref"));
            Show(editor, false);
            Root.Add(editor);
            Recompile(code.value);
        }

        void Recompile(string src)
        {
            if (src == lastCompiled) return;
            lastCompiled = src;
            compiled = Argot.Compile(src);
            if (compiled.Ok)
            {
                var p = compiled.Program;
                compileL.text = "OK  " + (p.Kind == ProgramKind.Meta ? "@meta  " : "") + p.Name + "   rules " + p.RuleCount + "   symbols " + p.Symbols +
                                (p.Kind == ProgramKind.Unit ? "   cost per mote " + p.SpawnCost + (p.RuleCount > Rules.FreeRules ? " (instruction tax)" : "") : "");
                compileL.RemoveFromClassList("compile-err"); compileL.AddToClassList("compile-ok");
            }
            else
            {
                compileL.text = string.Join("\n", compiled.Errors.Take(4));
                compileL.RemoveFromClassList("compile-ok"); compileL.AddToClassList("compile-err");
            }
        }

        void Save(Action<Program> then)
        {
            if (compiled == null || !compiled.Ok) { Toast("fix the errors first"); return; }
            var s = app.Session;
            if (s == null) { Toast("start a game to save programs into it"); return; }
            s.SaveProgram(code.value, (p, err) =>
            {
                if (err != null) { Toast("not saved: " + err); return; }
                Log("saved program " + p.Name + " (#" + p.Id + ")", "good");
                then?.Invoke(p);
                RefreshLibrary();
            });
        }

        public void ToggleEditor()
        {
            bool on = editor.ClassListContains("hidden");
            Show(editor, on);
            if (on) RefreshLibrary();
        }

        public void RefreshLibrary()
        {
            libList.Clear();
            var s = app.Session;
            var progs = s != null ? s.Library : Presets.All;
            foreach (var p in progs.OrderBy(p => p.Id >= 1000).ThenBy(p => p.Id).Take(80))
            {
                var item = L((p.Kind == ProgramKind.Meta ? "@meta " : "") + p.Name + "   " + p.RuleCount + " rules / " + p.Symbols + " symbols" + (p.Id < 1000 ? "   [preset]" : "   #" + p.Id), "lib-item");
                var prog = p;
                item.RegisterCallback<ClickEvent>(e => { code.value = prog.Source; });
                libList.Add(item);
            }
        }

        // ================================================================ chambers

        void BuildChambers()
        {
            chambers = Screen("chambers");
            chambers.AddToClassList("menu");
            chambers.pickingMode = PickingMode.Position;
            var box = new VisualElement(); box.AddToClassList("row"); box.style.width = Length.Percent(92); box.style.height = Length.Percent(86);
            chambers.Add(box);
            var left = Panel("TEST CHAMBERS", "chamber-list");
            chamberList = new VisualElement(); left.Add(chamberList);
            left.Add(B("BACK", () => ShowMenu()));
            box.Add(left);
            var right = Panel(null, "grow");
            chamberTitle = L("", "panel-title"); right.Add(chamberTitle);
            proctorBox = new VisualElement(); right.Add(proctorBox);
            chamberBrief = L("", "amber"); right.Add(chamberBrief);
            chamberCode = Field("", true); chamberCode.AddToClassList("code"); chamberCode.style.minHeight = 180;
            chamberCode.RegisterValueChangedCallback(e => ChamberCompile());
            right.Add(chamberCode);
            chamberResult = L("", "compile-ok"); right.Add(chamberResult);
            right.Add(Row(
                B("RUN x1", () => app.RunChamber(chamberSel, chamberCode.value, 1f), "btn btn-hot"),
                B("RUN x8", () => app.RunChamber(chamberSel, chamberCode.value, 8f)),
                B("RESULT NOW", () => ChamberInstant()),
                B("SUBMIT RECORD", () => app.SubmitChamber(chamberSel, chamberCode.value))));
            right.Add(L("LEADERBOARD (verified by the server)", "panel-title"));
            recordsBox = new VisualElement(); right.Add(recordsBox);
            box.Add(right);
            Show(chambers, false);
        }

        public void ShowChambers(int select = 0)
        {
            Show(menu, false); Show(hud, false); Show(endgame, false); Show(editor, false); Show(chambers, true);
            if (select > 0) chamberSel = select;
            chamberList.Clear();
            foreach (var c in Chambers.All)
            {
                var spec = c;
                var item = new VisualElement(); item.AddToClassList("chamber-item");
                item.EnableInClassList("chamber-on", c.Id == chamberSel);
                item.Add(L(c.Id.ToString("00") + "  " + c.Title, "slot-name"));
                item.Add(L(c.Brief, "dim"));
                var best = app.BestLocal(c.Id);
                if (best != null) item.Add(L("best: " + best, "good"));
                item.RegisterCallback<ClickEvent>(e => { chamberSel = spec.Id; ShowChambers(); });
                chamberList.Add(item);
            }
            var cs = Chambers.Get(chamberSel);
            chamberTitle.text = cs.Id.ToString("00") + "  //  " + cs.Title;
            proctorBox.Clear();
            foreach (var line in cs.Proctor) proctorBox.Add(L("PROCTOR: " + line, "proctor"));
            chamberBrief.text = "OBJECTIVE  " + cs.Brief + "   (" + cs.TimeLimit / Rules.TickHz + " s)";
            chamberCode.SetValueWithoutNotify(app.ChamberDraft(cs.Id));
            ChamberCompile();
            RefreshRecords();
        }

        void ChamberCompile()
        {
            app.SaveChamberDraft(chamberSel, chamberCode.value);
            var r = Argot.Compile(chamberCode.value);
            chamberResult.text = r.Ok ? "OK  rules " + r.Program.RuleCount + "  symbols " + r.Program.Symbols : string.Join("\n", r.Errors.Take(3));
            chamberResult.EnableInClassList("compile-err", !r.Ok);
        }

        void ChamberInstant()
        {
            var r = Chambers.Run(Chambers.Get(chamberSel), chamberCode.value);
            if (r.Error != null) { chamberResult.text = r.Error; return; }
            chamberResult.text = (r.Success ? "PASS" : "FAIL") + "   ticks " + r.Ticks + "   symbols " + r.Symbols + "   lost " + r.Lost + "   progress " + r.Progress + "/" + Chambers.Get(chamberSel).Target;
            if (r.Success) app.RecordLocal(chamberSel, r);
        }

        public void RefreshRecords()
        {
            if (recordsBox == null) return;
            recordsBox.Clear();
            var recs = app.Records(chamberSel);
            if (recs == null) { recordsBox.Add(L("connect (submit once) to see the global board", "dim")); return; }
            int n = 0;
            foreach (var r in recs.OrderBy(r => r.Ticks).ThenBy(r => r.Symbols).Take(10))
                recordsBox.Add(L((++n) + ".  " + r.PlayerName + "   " + r.Ticks + " ticks   " + r.Symbols + " symbols   " + r.Lost + " lost", "dim"));
            if (n == 0) recordsBox.Add(L("no records yet. be the first.", "dim"));
        }

        // ================================================================ endgame

        void BuildEndgame()
        {
            endgame = Screen("endgame");
            endgame.AddToClassList("endgame");
            endgame.pickingMode = PickingMode.Position;
            var box = Panel(null); box.style.width = 640;
            endTitle = L("", "endgame-title display"); box.Add(endTitle);
            endStats = L("", "dim"); endStats.style.whiteSpace = WhiteSpace.Normal; box.Add(endStats);
            endButtons = new VisualElement(); endButtons.AddToClassList("row"); box.Add(endButtons);
            endgame.Add(box);
            Show(endgame, false);
        }

        public void ShowEnd(string title, Color color, string stats, params (string label, Action act)[] buttons)
        {
            Show(endgame, true);
            endTitle.text = title;
            endTitle.style.color = color;
            endStats.text = stats;
            endButtons.Clear();
            foreach (var b in buttons) endButtons.Add(B(b.label, b.act));
        }

        public void HideEnd() => Show(endgame, false);

        /// <summary>Only the world labels stay: for trailers and captures.</summary>
        public void Cinematic()
        {
            Show(menu, false); Show(hud, false); Show(editor, false); Show(chambers, false); Show(endgame, false);
        }
    }
}
