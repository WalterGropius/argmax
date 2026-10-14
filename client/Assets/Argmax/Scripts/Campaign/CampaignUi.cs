using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Argmax.Sim;
using UnityEngine;
using UnityEngine.UIElements;

namespace ArgmaxClient
{
    /// <summary>
    /// BOOT SEQUENCE, the campaign: a curriculum map, then each test as a small loop of read, write, run,
    /// watch, fix. Program levels give you an editor that only accepts the words you've been taught; hands
    /// levels give you only the commands they teach. Passing a test hands you a concept card and the next word.
    /// </summary>
    public sealed class CampaignUi
    {
        readonly App app;
        readonly CampaignProgress progress;

        // screens
        VisualElement map, level, result, epilogue;
        // map
        Label mapProgress, detailTitle, detailBrief, detailStory, detailStats, detailConcept;
        VisualElement chaptersRow, detailWords, vocabBox, componentsBox;
        Button playBtn, unlockBtn, resetBtn;
        int selected;
        // level
        VisualElement objectivePanel, proctorPanel, editorPanel, evolvePanel, handsPanel, chipsBox, timeFill, heldFill;
        Label objTitle, objLine, objTime, proctorText, proctorCount, editorStatus, parLine, handsList, runState, newWords;
        Button proctorNext, hintBtn, runBtn, pauseBtn, stepBtn, resetBtnL, solutionBtn, componentsBtn;
        TextField codeField;
        Typewriter proctorTw;
        readonly List<string> lines = new List<string>();
        int lineAt, hintAt;
        readonly Dictionary<float, Button> speedBtns = new Dictionary<float, Button>();
        float speed = 1f;
        // evolve
        Chart evoChart;
        Label evoStatus;
        SourceView evoSource;
        SliderInt[] evoSliders;
        Button evoBtn;
        Evolver evolver;
        bool evolving;
        // result
        Label resTitle, resStats, resReason, resOutro, conceptTitle, conceptBody, conceptInterview, resUnlock;
        VisualElement starsRow, conceptCard, resButtons;
        StarIcon[] stars;
        ShapeGlyph conceptGlyph;
        Typewriter outroTw;
        readonly List<string> outro = new List<string>();
        int outroAt;
        float starAnimAt = -1;
        int starsWon;
        // epilogue
        Label epiText;
        Chart epiChart;
        Typewriter epiTw;
        readonly List<string> epiLines = new List<string>();
        int epiAt;

        ChamberSpec spec;
        string runningSource;
        bool running, ended;

        public bool Active => spec != null;
        public ChamberSpec Spec => spec;

        public CampaignUi(App app, CampaignProgress progress)
        {
            this.app = app;
            this.progress = progress;
        }

        // ================================================================ helpers

        static Label L(string text, string cls = null)
        {
            var l = new Label(text);
            if (cls != null) foreach (var c in cls.Split(' ')) l.AddToClassList(c);
            return l;
        }

        static Button B(string text, Action onClick, string cls = "btn")
        {
            var b = new Button(onClick) { text = text };
            foreach (var c in cls.Split(' ')) b.AddToClassList(c);
            return b;
        }

        static VisualElement Box(string cls)
        {
            var v = new VisualElement();
            foreach (var c in cls.Split(' ')) v.AddToClassList(c);
            return v;
        }

        static VisualElement Row(params VisualElement[] kids)
        {
            var r = Box("row");
            foreach (var k in kids) r.Add(k);
            return r;
        }

        static void Show(VisualElement v, bool on) { if (v != null) v.EnableInClassList("hidden", !on); }

        VisualElement Screen(VisualElement root, string name, string cls)
        {
            var v = Box("screen " + cls);
            v.name = name;
            v.pickingMode = PickingMode.Position;
            root.Add(v);
            Show(v, false);
            return v;
        }

        // ================================================================ build

        public void Build(VisualElement root)
        {
            BuildMap(root);
            BuildLevel(root);
            BuildResult(root);
            BuildEpilogue(root);
        }

        public void HideAll()
        {
            Show(map, false); Show(level, false); Show(result, false); Show(epilogue, false);
        }

        // ---------------------------------------------------------------- map

        void BuildMap(VisualElement root)
        {
            map = Screen(root, "campaign-map", "cmp-map");
            var head = Row(L("BOOT SEQUENCE", "cmp-logo display"), Box("grow"));
            mapProgress = L("", "cmp-progress");
            head.Add(mapProgress);
            head.Add(B("MENU", () => app.BackToMenu(), "btn btn-small"));
            map.Add(head);
            map.Add(L("a curriculum in 28 tests. every word of Argot is earned. every test is a real simulation.", "dim cmp-sub voice"));

            var body = Row();
            body.AddToClassList("cmp-body");
            chaptersRow = Box("cmp-chapters");
            body.Add(chaptersRow);

            var detail = Box("panel cmp-detail");
            detailTitle = L("", "cmp-detail-title display");
            detailStory = L("", "cmp-story");
            detailBrief = L("", "amber cmp-wrap");
            detailWords = Box("cmp-chips");
            detailStats = L("", "dim cmp-wrap");
            detailConcept = L("", "cmp-wrap cmp-concept-mini");
            playBtn = B("PLAY", () => Enter(selected), "btn btn-hot cmp-play");
            detail.Add(detailTitle); detail.Add(detailStory); detail.Add(detailBrief);
            detail.Add(L("WORDS IN THIS TEST", "panel-title cmp-gap")); detail.Add(detailWords);
            detail.Add(detailStats); detail.Add(detailConcept);
            detail.Add(Box("grow"));
            detail.Add(playBtn);
            body.Add(detail);
            map.Add(body);

            var foot = Row();
            foot.AddToClassList("cmp-foot");
            var vocab = Box("panel grow");
            vocab.Add(L("YOUR VOCABULARY", "panel-title"));
            vocabBox = Box("cmp-chips");
            vocab.Add(vocabBox);
            foot.Add(vocab);
            var comps = Box("panel cmp-components");
            comps.Add(L("COMPONENTS", "panel-title"));
            componentsBox = Box("");
            comps.Add(componentsBox);
            foot.Add(comps);
            var tools = Box("panel cmp-tools");
            unlockBtn = B("UNLOCK ALL", () => { progress.UnlockAll = !progress.UnlockAll; ShowMap(selected); }, "btn btn-small");
            resetBtn = B("RESET PROGRESS", ConfirmReset, "btn btn-small");
            tools.Add(unlockBtn); tools.Add(resetBtn);
            foot.Add(tools);
            map.Add(foot);
        }

        float resetArmedUntil;
        void ConfirmReset()
        {
            if (Time.unscaledTime > resetArmedUntil) { resetArmedUntil = Time.unscaledTime + 3f; resetBtn.text = "CLICK AGAIN TO ERASE"; return; }
            progress.Reset();
            resetBtn.text = "RESET PROGRESS";
            ShowMap();
        }

        public void ShowMap(int focus = 0)
        {
            Exit();
            app.Ui.HideScreens();
            Show(map, true);
            selected = focus != 0 ? focus : progress.NextLevel();
            mapProgress.text = progress.SolvedCount + " / " + Campaign.Levels.Length + " tests    " + progress.StarCount + " / " + Campaign.Levels.Length * 3 + " stars";
            unlockBtn.text = progress.UnlockAll ? "LOCK (SYLLABUS ORDER)" : "UNLOCK ALL";
            chaptersRow.Clear();
            foreach (var ch in Campaign.Chapters)
            {
                var col = Box("cmp-chapter cmp-ch-" + ch.Number);
                var chHead = Row(new ShapeGlyph(Design.ChapterGlyph(ch.Number), Design.ChapterColor(ch.Number), 12f), L("  " + ch.Number + "  " + ch.Title, "cmp-chapter-title"));
                col.Add(chHead);
                col.Add(L(ch.Tagline, "dim cmp-chapter-tag"));
                foreach (var l in Campaign.Levels)
                {
                    if (l.Chapter != ch.Number) continue;
                    var lv = l;
                    bool open = progress.Unlocked(l.Id), done = progress.Solved(l.Id);
                    var node = Box("cmp-node");
                    node.EnableInClassList("cmp-node-locked", !open);
                    node.EnableInClassList("cmp-node-done", done);
                    node.EnableInClassList("cmp-node-next", open && !done);
                    node.EnableInClassList("cmp-node-sel", l.Id == selected);
                    var top = Row(L(l.Code, "cmp-node-code"), L(open ? l.Title : "LOCKED", "cmp-node-title"));
                    node.Add(top);
                    var st = Row(L(KindTag(l), "cmp-node-kind"), Box("grow"));
                    for (int s = 0; s < 3; s++) { var star = new StarIcon(11f) { On = progress.Stars(l.Id) > s }; st.Add(star); }
                    node.Add(st);
                    node.RegisterCallback<ClickEvent>(e =>
                    {
                        if (e.clickCount >= 2 && progress.Unlocked(lv.Id)) { Enter(lv.Id); return; }
                        selected = lv.Id; ShowMap(lv.Id);
                    });
                    col.Add(node);
                }
                chaptersRow.Add(col);
            }
            ShowDetail();
            vocabBox.Clear();
            foreach (var l in Campaign.Levels)
            {
                if (!progress.Solved(l.Id) || l.Unlocks == null) continue;
                foreach (var w in l.Unlocks) vocabBox.Add(Chip(w, "cmp-chip"));
            }
            if (vocabBox.childCount == 0) vocabBox.Add(L("nothing yet. you will learn your first words in test 2-1.", "dim"));
            componentsBox.Clear();
            foreach (var c in progress.Components) componentsBox.Add(L(c.name + "   (" + Campaign.Get(c.fromLevel)?.Code + ")", "good"));
            if (progress.Components.Count == 0) componentsBox.Add(L("programs you build in some tests are kept here.", "dim cmp-wrap"));
        }

        static string KindTag(ChamberSpec l)
        {
            switch (l.Kind)
            {
                case LevelKind.Hands: return "HANDS";
                case LevelKind.Hybrid: return "CODE + HANDS";
                case LevelKind.Evolve: return "EVOLVE";
                case LevelKind.Duel: return l.Hands == Hands.None ? "METAPROGRAM" : "DUEL";
                default: return "CODE";
            }
        }

        void ShowDetail()
        {
            var l = Campaign.Get(selected);
            if (l == null) return;
            bool open = progress.Unlocked(l.Id);
            var ch = Campaign.Chapters[l.Chapter - 1];
            detailTitle.text = l.Code + "  //  " + (open ? l.Title : "LOCKED");
            detailStory.text = "CHAPTER " + ch.Number + "  " + ch.Title + ".  " + ch.Story;
            detailBrief.text = open ? "OBJECTIVE  " + l.Brief : "Pass the test before this one to open it.";
            detailWords.Clear();
            if (l.Unlocks != null && l.Unlocks.Length > 0) foreach (var w in l.Unlocks) detailWords.Add(Chip(w, "cmp-chip cmp-chip-new"));
            else detailWords.Add(L(l.HasEditor ? "no new words: use what you know" : "no code: hands only", "dim"));
            var rec = progress.Get(l.Id);
            detailStats.text = rec.solved
                ? "PASSED  " + rec.stars + "/3 stars   best " + rec.bestTicks + " ticks" + (rec.bestSymbols < int.MaxValue ? "  " + rec.bestSymbols + " symbols" : "") + "   par " + l.ParTicks + (l.ParSymbols > 0 ? " / " + l.ParSymbols : "") + "   runs " + rec.runs
                : rec.runs > 0 ? "runs " + rec.runs + "   fails " + rec.fails : "";
            detailConcept.text = rec.solved && l.ConceptTitle != null ? "CONCEPT  " + l.ConceptTitle : "";
            playBtn.SetEnabled(open);
            playBtn.text = rec.solved ? "REPLAY " + l.Code : "PLAY " + l.Code;
        }

        Label Chip(string word, string cls)
        {
            var c = L(word, cls);
            var at = Campaign.UnlockedAt(word);
            if (at != null) c.tooltip = "taught in " + at;
            return c;
        }

        // ---------------------------------------------------------------- level

        void BuildLevel(VisualElement root)
        {
            level = Box("screen");
            level.name = "campaign-level";
            level.pickingMode = PickingMode.Ignore;
            root.Add(level);
            Show(level, false);

            objectivePanel = Box("panel cmp-objective");
            objectivePanel.pickingMode = PickingMode.Position;
            var orow = Row();
            objTitle = L("", "cmp-obj-title");
            orow.Add(objTitle); orow.Add(Box("grow"));
            orow.Add(B("MAP", () => ShowMap(spec?.Id ?? 0), "btn btn-small"));
            objectivePanel.Add(orow);
            objLine = L("", "cmp-obj-line");
            objectivePanel.Add(objLine);
            var tbar = Box("bar cmp-timebar"); timeFill = Box("bar-fill"); tbar.Add(timeFill);
            objTime = L("", "dim cmp-obj-time");
            objectivePanel.Add(Row(tbar, objTime));
            var hbar = Box("bar cmp-heldbar"); heldFill = Box("bar-fill cmp-held-fill"); hbar.Add(heldFill);
            objectivePanel.Add(hbar);
            level.Add(objectivePanel);

            var rightCol = Box("cmp-rightcol");
            rightCol.pickingMode = PickingMode.Ignore;
            level.Add(rightCol);
            proctorPanel = Box("panel cmp-proctor");
            proctorPanel.pickingMode = PickingMode.Position;
            var prow = Row(L("PROCTOR", "cmp-proctor-name"), Box("grow"));
            proctorCount = L("", "dim");
            prow.Add(proctorCount);
            proctorPanel.Add(prow);
            proctorText = L("", "cmp-proctor-text");
            proctorTw = new Typewriter(proctorText) { OnChar = () => Audio.Play(Sfx.Type, 0.25f) };
            proctorPanel.Add(proctorText);
            newWords = L("", "cmp-newwords");
            proctorPanel.Add(newWords);
            var pbtns = Row(Box("grow"));
            hintBtn = B("HINT", Hint, "btn btn-small");
            solutionBtn = B("SHOW SOLUTION", ShowSolution, "btn btn-small");
            proctorNext = B("NEXT", NextLine, "btn btn-small btn-hot");
            pbtns.Add(solutionBtn); pbtns.Add(hintBtn); pbtns.Add(proctorNext);
            proctorPanel.Add(pbtns);
            proctorPanel.RegisterCallback<ClickEvent>(e => { if (!proctorTw.Done) proctorTw.Finish(); });
            rightCol.Add(proctorPanel);

            editorPanel = Box("panel cmp-editor");
            editorPanel.pickingMode = PickingMode.Position;
            var erow = Row(L("ARGOT", "panel-title"), Box("grow"));
            componentsBtn = B("COMPONENTS", ToggleComponents, "btn btn-small");
            erow.Add(componentsBtn);
            erow.Add(B("STARTER", () => { if (spec != null) SetCode(spec.Starter ?? ""); }, "btn btn-small"));
            editorPanel.Add(erow);
            codeField = new TextField { multiline = true, selectAllOnFocus = false, selectAllOnMouseUp = false };
            codeField.RegisterCallback<FocusOutEvent>(e => { try { lastCursor = codeField.textSelection.cursorIndex; } catch { lastCursor = -1; } });
            codeField.AddToClassList("code");
            codeField.AddToClassList("cmp-code");
            codeField.RegisterValueChangedCallback(e => OnCode(e.newValue));
            editorPanel.Add(codeField);
            editorStatus = L("", "compile-ok cmp-wrap");
            editorPanel.Add(editorStatus);
            parLine = L("", "dim");
            editorPanel.Add(parLine);
            editorPanel.Add(L("WORDS  (click to insert; dim words are locked)", "panel-title cmp-gap"));
            var chipScroll = new ScrollView(ScrollViewMode.Vertical);
            chipScroll.AddToClassList("cmp-chipscroll");
            chipsBox = chipScroll.contentContainer;
            editorPanel.Add(chipScroll);
            level.Add(editorPanel);

            handsPanel = Box("panel cmp-hands");
            handsPanel.pickingMode = PickingMode.Position;
            handsPanel.Add(L("YOUR HANDS", "panel-title"));
            handsList = L("", "cmp-wrap");
            handsPanel.Add(handsList);
            rightCol.Add(handsPanel);

            var controls = Box("panel cmp-controls");
            controls.pickingMode = PickingMode.Position;
            runBtn = B("RUN", Run, "btn btn-hot cmp-run");
            pauseBtn = B("PAUSE", TogglePause, "btn");
            stepBtn = B("STEP", Step, "btn");
            resetBtnL = B("RESET", () => Rebuild(false), "btn");
            controls.Add(runBtn); controls.Add(pauseBtn); controls.Add(stepBtn); controls.Add(resetBtnL);
            foreach (var sp in new[] { 1f, 4f, 16f })
            {
                float s = sp;
                var b = B("x" + sp, () => SetSpeed(s), "btn btn-small");
                speedBtns[sp] = b;
                controls.Add(b);
            }
            controls.Add(B("SKIP", Skip, "btn btn-small"));
            runState = L("", "dim cmp-runstate");
            controls.Add(runState);
            level.Add(controls);

            BuildEvolve();
        }

        void BuildEvolve()
        {
            evolvePanel = Box("panel cmp-evolve");
            evolvePanel.pickingMode = PickingMode.Position;
            evolvePanel.Add(L("EVOLUTION  //  16 programs per generation", "panel-title"));
            evolvePanel.Add(L("fitness = what you reward. drag the weights, then evolve.", "dim cmp-wrap"));
            string[] names = { "PROGRESS", "SURVIVAL", "SPEED" };
            evoSliders = new SliderInt[3];
            for (int i = 0; i < 3; i++)
            {
                var s = new SliderInt(names[i], 0, 9) { value = 1, showInputField = false };
                s.AddToClassList("cmp-slider");
                s.RegisterValueChangedCallback(e => { if (evolving) StartEvolve(); });
                evoSliders[i] = s;
                evolvePanel.Add(s);
            }
            evoBtn = B("EVOLVE", () => { if (evolving) StopEvolve(); else StartEvolve(); }, "btn btn-hot");
            evolvePanel.Add(Row(evoBtn, B("USE BEST", UseBest, "btn")));
            var chartBox = Box("cmp-chart");
            evoChart = new Chart { Min = 0f, Max = 1f };
            chartBox.Add(evoChart);
            evolvePanel.Add(chartBox);
            evolvePanel.Add(L("cobalt: best fitness    bone: generation mean", "dim cmp-legend"));
            evoStatus = L("", "cmp-wrap");
            evolvePanel.Add(evoStatus);
            evoSource = new SourceView();
            evolvePanel.Add(evoSource.Root);
            level.Add(evolvePanel);
        }

        // ---------------------------------------------------------------- enter / exit

        public void Enter(int id)
        {
            var l = Campaign.Get(id);
            if (l == null || !progress.Unlocked(id)) return;
            StopEvolve();
            spec = l;
            progress.LastLevel = id;
            Show(map, false); Show(result, false); Show(epilogue, false);
            Show(level, true);
            app.Ui.CampaignMode(true);
            hintAt = 0;
            SetCode(progress.Draft(l));
            Show(editorPanel, l.HasEditor);
            Show(handsPanel, l.Hands != Hands.None);
            Show(evolvePanel, l.Kind == LevelKind.Evolve);
            level.EnableInClassList("cmp-evolve-mode", l.Kind == LevelKind.Evolve);
            level.EnableInClassList("cmp-noeditor", !l.HasEditor);
            if (l.Kind == LevelKind.Evolve)
            {
                for (int i = 0; i < 3; i++) evoSliders[i].SetValueWithoutNotify(l.EvolveWeights[i]);
                evolver = null; evoChart.A.Clear(); evoChart.B.Clear(); evoChart.Refresh();
                evoStatus.text = "press EVOLVE"; evoSource.Set("");
            }
            handsList.text = HandsText(l);
            BuildChips();
            parLine.text = "par  " + l.ParTicks + " ticks" + (l.ParSymbols > 0 ? "  /  " + l.ParSymbols + " symbols" : "") + "   (stars: pass, time, " + (l.HasEditor ? "symbols)" : "no losses)");
            // the briefing: chapter story the first time you enter a chapter, then PROCTOR's lines
            lines.Clear();
            var ch = Campaign.Chapters[l.Chapter - 1];
            bool firstOfChapter = Campaign.Levels.First(x => x.Chapter == l.Chapter).Id == l.Id;
            if (firstOfChapter && !progress.Solved(l.Id)) lines.Add("CHAPTER " + ch.Number + ": " + ch.Title + ". " + ch.Story);
            lines.AddRange(l.Proctor);
            lineAt = 0;
            newWords.text = l.Unlocks != null && l.Unlocks.Length > 0 ? "NEW WORDS:  " + string.Join("   ", l.Unlocks) : "";
            Show(proctorPanel, true);
            SayLine();
            UpdateSolutionButton();
            speed = 1f;
            Rebuild(false, frame: true);
        }

        /// <summary>For smoke runs and captures: enter a test, load its reference solution, and run it.</summary>
        public void Autoplay(int id, float runSpeed, bool solution = true)
        {
            Enter(id);
            if (spec == null) return;
            if (solution && spec.HasEditor && spec.Solution != null) SetCode(spec.Solution);
            lineAt = lines.Count; Show(proctorNext, false);
            speed = runSpeed;
            Run();
            if (spec.Script != null && app.Session != null)
                foreach (var t in spec.Script) ScheduleScripted(t);
        }

        void ScheduleScripted(TimedCmd t)
        {
            // replay the level's reference hands, the same way the tests do
            if (app.Session is LocalSession ls)
            {
                if (t.Tick == 0) { Issue(ls, ls.W, t); return; }
                var prev = ls.AfterStep;
                ls.AfterStep = w =>
                {
                    prev?.Invoke(w);
                    if (w.Tick == t.Tick) Issue(ls, w, t);
                };
            }
        }

        static void Issue(LocalSession ls, World w, TimedCmd t)
        {
            var c = t.Cmd;
            if (c.Ids == null && Chambers.IsUnitCommand(c.Kind))
            {
                var ids = t.Tier > 0 ? Chambers.MyIds(w, t.Tier) : Chambers.MyIds(w);
                if (t.Count > 0 && ids.Length > t.Count) Array.Resize(ref ids, t.Count);
                c.Ids = ids;
            }
            if (c.X < 0) { int cell = Chambers.Target(w, c.X); c.X = (short)(cell % w.W); c.Y = (short)(cell / w.W); }
            ls.Issue(c);
        }

        /// <summary>Leave the level (to the map or the menu). Safe to call when not in a level.</summary>
        public void Exit()
        {
            StopEvolve();
            if (spec != null && codeField != null) { progress.SetDraft(spec.Id, codeField.value); progress.Save(); }
            spec = null;
            running = false;
            Show(level, false); Show(result, false); Show(epilogue, false);
            app.Ui.CampaignMode(false);
        }

        static string HandsText(ChamberSpec l)
        {
            var h = l.Hands;
            if (h == Hands.None) return "";
            var sb = new StringBuilder();
            void Add(Hands f, string s) { if ((h & f) != 0) sb.AppendLine(s); }
            sb.AppendLine("drag: select   double-click: select same program");
            Add(Hands.Move, "right-click: MOVE (walk, ignore the program)");
            Add(Hands.Attack, "A + click: ATTACK (the program fights its way there)");
            Add(Hands.Stop, "S: stop");
            Add(Hands.Hold, "H: hold");
            Add(Hands.Merge, "M: merge the selection (equal pairs fuse)");
            Add(Hands.Split, "X: split the selection");
            Add(Hands.Beacon, "B + click: place the beacon   C: clear it");
            Add(Hands.Spawn, "Z: build one mote   Shift+Z: five");
            Add(Hands.Produce, "Q..Y: choose what the core builds");
            Add(Hands.Auto, "U: auto-build on/off");
            Add(Hands.Reprogram, "Shift+Q..Y: rewrite the selection with that slot");
            Add(Hands.Slot, "editor: save into a production slot");
            return sb.ToString().TrimEnd();
        }

        public bool Allows(CmdKind k) => spec == null || (spec.Hands & Chambers.Requires(k)) != 0;

        // ---------------------------------------------------------------- briefing

        void SayLine()
        {
            if (lineAt < lines.Count)
            {
                proctorTw.Say(lines[lineAt]);
                proctorCount.text = (lineAt + 1) + " / " + lines.Count;
                proctorNext.text = lineAt + 1 < lines.Count ? "NEXT" : spec != null && spec.HasEditor ? "WRITE" : "BEGIN";
                Show(proctorNext, true);
            }
        }

        void NextLine()
        {
            if (!proctorTw.Done) { proctorTw.Finish(); return; }
            Audio.Play(Sfx.Click);
            lineAt++;
            if (lineAt < lines.Count) { SayLine(); return; }
            Show(proctorNext, false);
            proctorCount.text = "";
            if (spec != null && !spec.HasEditor && !running) Run();
            else if (spec != null && spec.HasEditor) codeField.Focus();
        }

        void Say(string text)
        {
            Show(proctorPanel, true);
            proctorTw.Say(text);
            proctorCount.text = "";
            Show(proctorNext, false);
        }

        void Hint()
        {
            if (spec == null || spec.Hints == null || spec.Hints.Length == 0) return;
            Audio.Play(Sfx.Click);
            progress.RecordHint(spec.Id);
            var h = spec.Hints[Math.Min(hintAt, spec.Hints.Length - 1)];
            Say("HINT " + (Math.Min(hintAt, spec.Hints.Length - 1) + 1) + "/" + spec.Hints.Length + ":  " + h);
            hintAt++;
            UpdateSolutionButton();
        }

        void UpdateSolutionButton()
        {
            var rec = spec != null ? progress.Get(spec.Id) : null;
            Show(solutionBtn, spec != null && spec.HasEditor && !string.IsNullOrEmpty(spec.Solution) && (rec.solved || (hintAt >= spec.Hints.Length && rec.fails >= 3)));
        }

        void ShowSolution()
        {
            if (spec == null) return;
            SetCode(spec.Solution);
            Say("A reference solution, not the best one. Your stars depend on beating its symbol count.");
        }

        // ---------------------------------------------------------------- editor

        int lastCursor = -1;

        void SetCode(string src)
        {
            lastCursor = -1;
            codeField.SetValueWithoutNotify(src ?? "");
            OnCode(codeField.value);
        }

        void OnCode(string src)
        {
            if (spec == null) return;
            progress.SetDraft(spec.Id, src);
            if (!spec.HasEditor) return;
            var c = Argot.Compile(src, "SOLUTION", spec.Vocab);
            if (c.Ok)
            {
                var p = c.Program;
                bool wrongKind = spec.Kind == LevelKind.Duel && spec.Hands == Hands.None ? p.Kind != ProgramKind.Meta : spec.Kind != LevelKind.Duel && p.Kind != ProgramKind.Unit;
                editorStatus.text = wrongKind ? (p.Kind == ProgramKind.Meta ? "this test takes a unit program: remove @meta" : "this test takes a @meta program")
                    : "OK   rules " + p.RuleCount + "   symbols " + p.Symbols + (spec.ParSymbols > 0 ? (p.Symbols <= spec.ParSymbols ? "   (under par)" : "   (par " + spec.ParSymbols + ")") : "");
                editorStatus.EnableInClassList("compile-err", wrongKind);
            }
            else
            {
                editorStatus.text = string.Join("\n", c.Errors.Take(3));
                editorStatus.EnableInClassList("compile-err", true);
            }
        }

        void BuildChips()
        {
            chipsBox.Clear();
            bool meta = spec.Kind == LevelKind.Duel;
            var vocab = spec.Vocab != null ? new HashSet<string>(spec.Vocab) : null;
            var news = spec.Unlocks != null ? new HashSet<string>(spec.Unlocks) : new HashSet<string>();
            void Group(string title, IEnumerable<string> words, string insertSuffix = " ")
            {
                var g = Box("cmp-chipgroup");
                g.Add(L(title, "cmp-chipgroup-title"));
                var wrap = Box("cmp-chips");
                foreach (var w in words)
                {
                    if (string.IsNullOrEmpty(w)) continue;
                    bool ok = vocab == null || vocab.Contains(w);
                    var chip = L(w, "cmp-chip" + (ok ? "" : " cmp-chip-locked") + (news.Contains(w) ? " cmp-chip-new" : ""));
                    var at = Campaign.UnlockedAt(w);
                    if (!ok) chip.tooltip = at != null ? "locked: taught in " + at : "locked";
                    else if (w.Length > 0) chip.tooltip = Help(w);
                    string word = w;
                    if (ok) chip.RegisterCallback<ClickEvent>(e => Insert(word + insertSuffix));
                    wrap.Add(chip);
                }
                g.Add(wrap);
                chipsBox.Add(g);
            }
            Group("CONTROL", meta ? new[] { "@meta", "always", "when", "not", "and" } : new[] { "always", "when", "not", "and" });
            Group("SENSORS", Argot.UnitSensors);
            Group("ACTIONS", new[] { "seek", "avoid", "flock", "spread", "form", "merge", "split", "mine", "hold", "wander", "infect", "mark", "holdfire" });
            Group("TARGETS", Argot.Fields.Concat(Argot.Patterns.Where(p => p != "")));
            if (meta)
            {
                Group("@META SENSORS", Argot.MetaSensors);
                Group("@META ACTIONS", new[] { "produce", "spawn", "beacon", "army" });
                Group("PLACES / ORDERS", Argot.Places.Where(p => p != "").Concat(Argot.ArmyOrders.Where(p => p != "")));
            }
        }

        static string Help(string w)
        {
            int i = Array.IndexOf(Argot.UnitSensors, w);
            if (i >= 0) return Argot.UnitSensorHelp[i];
            i = Array.IndexOf(Argot.MetaSensors, w);
            if (i >= 0) return "@meta: " + Argot.MetaSensorHelp[i];
            return null;
        }

        void Insert(string text)
        {
            Audio.Play(Sfx.Click, 0.6f);
            string v = codeField.value ?? "";
            bool focused = codeField.panel?.focusController?.focusedElement is VisualElement fe && (fe == codeField || codeField.Contains(fe));
            int at = v.Length;
            if (focused) { try { at = codeField.textSelection.cursorIndex; } catch { at = v.Length; } }
            else if (lastCursor >= 0) at = lastCursor;
            at = Mathf.Clamp(at, 0, v.Length);
            // never inside the header: a click before you ever typed starts a new line at the end
            if (!focused && lastCursor < 0 && v.Length > 0 && v[v.Length - 1] != '\n') text = "\n" + text;
            else if (at > 0 && v[at - 1] != ' ' && v[at - 1] != '\n' && v[at - 1] != ':') text = " " + text;
            codeField.value = v.Insert(at, text);
            lastCursor = at + text.Length;
            codeField.Focus();
            try { codeField.textSelection.cursorIndex = codeField.textSelection.selectIndex = lastCursor; } catch { }
        }

        public void ToggleEditor()
        {
            if (spec == null || !spec.HasEditor) return;
            bool on = editorPanel.ClassListContains("hidden");
            Show(editorPanel, on);
        }

        int componentAt;
        void ToggleComponents()
        {
            if (progress.Components.Count == 0) { app.Ui.Toast("no components yet: some tests save your program (FORAGER in 2-3)"); return; }
            var c = progress.Components[componentAt++ % progress.Components.Count];
            SetCode(c.source);
            app.Ui.Toast("loaded component " + c.name + (progress.Components.Count > 1 ? "  (click again for the next)" : ""));
        }

        // ---------------------------------------------------------------- running

        /// <summary>(Re)create the level's world. run=false shows the start state, paused.</summary>
        void Rebuild(bool run, bool frame = false)
        {
            if (spec == null) return;
            StopEvolve(keepChart: true);
            Program prog = null;
            string src = codeField.value;
            if (spec.HasEditor)
            {
                var c = Argot.Compile(src, "SOLUTION", spec.Vocab);
                bool kindOk = c.Ok && (spec.Kind == LevelKind.Duel ? (spec.Hands != Hands.None || c.Program.Kind == ProgramKind.Meta) : c.Program.Kind == ProgramKind.Unit);
                if (c.Ok && kindOk) prog = c.Program;
                else if (run)
                {
                    Audio.Play(Sfx.Error);
                    app.Ui.Toast(c.Ok ? editorStatus.text : "fix the errors first: " + c.Errors.FirstOrDefault());
                    return;
                }
            }
            if (prog == null)
            {
                var start = spec.StartProgram != 0 ? Presets.Get(spec.StartProgram, 0).Source : "always: hold";
                prog = Argot.Compile(start).Program;
            }
            prog.Id = Rules.ChamberProgramId;
            var w = Chambers.Create(spec, prog);
            var ls = new LocalSession(w, spec.Code + "  " + spec.Title) { CanCommand = spec.Hands != Hands.None, Speed = speed, Paused = !run };
            runningSource = run ? src : null;
            running = run;
            ended = false;
            app.RunCampaignSession(ls, frame);
            if (run) { progress.RecordRun(spec.Id); Audio.Play(Sfx.Run); }
            Show(result, false);
            RefreshControls();
        }

        void Run()
        {
            if (spec == null) return;
            if (lineAt < lines.Count) { lineAt = lines.Count; Show(proctorNext, false); proctorCount.text = ""; }
            Rebuild(true);
        }

        void TogglePause()
        {
            var s = app.Session;
            if (s == null || !running) return;
            s.Paused = !s.Paused;
            RefreshControls();
        }

        void Step()
        {
            if (!running) { Rebuild(true); if (app.Session != null) app.Session.Paused = true; RefreshControls(); return; }
            if (app.Session is LocalSession ls) { ls.Paused = true; ls.StepOnce(); RefreshControls(); }
        }

        void SetSpeed(float s)
        {
            speed = s;
            if (app.Session != null && running) app.Session.Speed = s;
            RefreshControls();
        }

        void Skip()
        {
            if (!running) Rebuild(true);
            if (app.Session is LocalSession ls) { ls.Paused = false; ls.RunToEnd(spec.TimeLimit + 2); }
        }

        void RefreshControls()
        {
            bool paused = app.Session?.Paused ?? true;
            runBtn.text = running ? "RE-RUN" : spec != null && !spec.HasEditor ? "BEGIN" : "RUN";
            pauseBtn.text = paused && running ? "RESUME" : "PAUSE";
            pauseBtn.SetEnabled(running);
            foreach (var kv in speedBtns) kv.Value.EnableInClassList("btn-on", Mathf.Approximately(kv.Key, speed));
        }

        // ---------------------------------------------------------------- per frame

        public void Update(float dt)
        {
            proctorTw.Update(dt);
            outroTw?.Update(dt);
            epiTw?.Update(dt);
            if (starAnimAt >= 0) AnimateStars();
            if (spec == null) return;
            if (evolving) EvolveFrame();
            var w = app.Session?.World;
            if (w == null || w.Chamber != spec) return;
            objTitle.text = spec.Code + "  " + spec.Title;
            objLine.text = Chambers.Describe(w);
            float tf = Mathf.Clamp01(w.Tick / (float)Mathf.Max(1, spec.TimeLimit));
            timeFill.style.width = Length.Percent(100f * tf);
            objTime.text = (w.Tick / Rules.TickHz) + "s / " + (spec.TimeLimit / Rules.TickHz) + "s";
            Show(heldFill.parent, spec.HoldTicks > 0);
            if (spec.HoldTicks > 0) heldFill.style.width = Length.Percent(100f * Mathf.Clamp01(w.ChamberHeld / (float)spec.HoldTicks));
            runState.text = !running ? "editing: the world is frozen at tick 0" : app.Session.Paused ? "paused at tick " + w.Tick : "tick " + w.Tick + "   x" + app.Session.Speed;
            stepBtn.SetEnabled(!w.Ended);
        }

        // ---------------------------------------------------------------- endings

        /// <summary>App calls this once when the level's world ends.</summary>
        public void OnEnd(World w)
        {
            if (spec == null || ended || !running) return;
            ended = true;
            var r = new ChamberResult
            {
                Success = w.Winner == 0,
                Ticks = w.Tick,
                Lost = w.Seats[0].Lost,
                Progress = w.ChamberProgress, Progress2 = w.ChamberProgress2,
                Target = w.ChamberTarget, Target2 = w.ChamberTarget2,
                Reason = w.ChamberReason,
            };
            if (spec.HasEditor && runningSource != null)
            {
                var c = Argot.Compile(runningSource, "SOLUTION", spec.Vocab);
                if (c.Ok) { r.Symbols = c.Program.Symbols; r.RuleCount = c.Program.RuleCount; }
            }
            r.Stars = Chambers.Stars(spec, r);
            bool first = false;
            if (r.Success) first = progress.RecordPass(spec, r, runningSource);
            else progress.RecordFail(spec.Id);
            ShowResult(r, first);
        }

        void BuildResult(VisualElement root)
        {
            result = Screen(root, "campaign-result", "cmp-result");
            var box = Box("panel cmp-result-box");
            resTitle = L("", "cmp-res-title display");
            box.Add(resTitle);
            starsRow = Row();
            starsRow.AddToClassList("cmp-stars");
            stars = new StarIcon[3];
            for (int i = 0; i < 3; i++) { stars[i] = new StarIcon(44f); starsRow.Add(stars[i]); }
            box.Add(starsRow);
            resStats = L("", "cmp-res-stats");
            box.Add(resStats);
            resReason = L("", "hot cmp-wrap");
            box.Add(resReason);
            resOutro = L("", "amber cmp-wrap cmp-res-outro");
            outroTw = new Typewriter(resOutro) { OnChar = () => Audio.Play(Sfx.Type, 0.2f) };
            box.Add(resOutro);
            conceptCard = Box("cmp-concept");
            conceptGlyph = new ShapeGlyph(Glyph.Circle, Design.Cobalt, 34f);
            conceptGlyph.style.position = Position.Absolute; conceptGlyph.style.right = 14; conceptGlyph.style.top = 12;
            conceptCard.Add(conceptGlyph);
            conceptCard.Add(L("CONCEPT UNLOCKED", "cmp-concept-k"));
            conceptTitle = L("", "cmp-concept-title display");
            conceptBody = L("", "cmp-wrap cmp-concept-body body");
            conceptInterview = L("", "cmp-wrap cmp-interview");
            conceptCard.Add(conceptTitle); conceptCard.Add(conceptBody); conceptCard.Add(conceptInterview);
            box.Add(conceptCard);
            resUnlock = L("", "good cmp-wrap");
            box.Add(resUnlock);
            resButtons = Row();
            resButtons.AddToClassList("cmp-res-buttons");
            box.Add(resButtons);
            result.Add(box);
            result.RegisterCallback<ClickEvent>(e => { if (!outroTw.Done) outroTw.Finish(); });
        }

        void ShowResult(ChamberResult r, bool first)
        {
            Show(result, true);
            bool pass = r.Success;
            resTitle.text = pass ? "TEST PASSED" : "TEST FAILED";
            resTitle.style.color = pass ? Palette.Noise : Palette.Foe;
            starsWon = pass ? r.Stars : 0;
            foreach (var s in stars) { s.On = false; s.Pop(1f); }
            starAnimAt = pass ? Time.unscaledTime + 0.25f : -1;
            Show(starsRow, pass);
            var sb = new StringBuilder();
            sb.Append("ticks ").Append(r.Ticks).Append(" / par ").Append(spec.ParTicks);
            if (spec.HasEditor) sb.Append("     symbols ").Append(r.Symbols).Append(spec.ParSymbols > 0 ? " / par " + spec.ParSymbols : "");
            sb.Append("     lost ").Append(r.Lost);
            if (!pass) sb.Append("\n").Append(Chambers.Describe(app.Session.World));
            resStats.text = sb.ToString();
            resReason.text = pass ? "" : "REASON: " + (r.Reason ?? "the goal was not met") + ".   " + FailQuip(r);
            outro.Clear();
            if (pass && spec.Outro != null) outro.AddRange(spec.Outro);
            if (outro.Count > 0) outroTw.Say(string.Join("  ", outro), 70f); else { outroTw.Say(""); }
            Show(conceptCard, pass && spec.ConceptTitle != null);
            if (pass)
            {
                conceptTitle.text = spec.ConceptTitle;
                conceptGlyph.Shape = Design.ChapterGlyph(spec.Chapter);
                conceptGlyph.Color = Design.ChapterColor(spec.Chapter);
                conceptGlyph.MarkDirtyRepaint();
                conceptBody.text = spec.ConceptBody;
                conceptInterview.text = spec.Interview != null ? "INTERVIEW ECHO   " + spec.Interview : "";
                Show(conceptInterview, spec.Interview != null);
            }
            int idx = Campaign.IndexOf(spec.Id);
            var next = idx + 1 < Campaign.Levels.Length ? Campaign.Levels[idx + 1] : null;
            resUnlock.text = pass && first && next != null ? "UNLOCKED  " + next.Code + " " + next.Title + (next.Unlocks != null && next.Unlocks.Length > 0 ? "   new words: " + string.Join(" ", next.Unlocks) : "") : "";
            if (pass && first && !string.IsNullOrEmpty(spec.Component)) resUnlock.text += (resUnlock.text.Length > 0 ? "\n" : "") + "COMPONENT SAVED  " + spec.Component;
            resButtons.Clear();
            if (pass && next != null) resButtons.Add(B("NEXT TEST  " + next.Code, () => Enter(next.Id), "btn btn-hot"));
            if (pass && next == null && first) resButtons.Add(B("EPILOGUE", ShowEpilogue, "btn btn-hot"));
            resButtons.Add(B(pass ? "IMPROVE" : "EDIT", () => { Show(result, false); Rebuild(false); }, pass ? "btn" : "btn btn-hot"));
            resButtons.Add(B("WATCH AGAIN", () => Rebuild(true), "btn"));
            if (!pass && spec.Hints != null) resButtons.Add(B("HINT", () => { Show(result, false); Rebuild(false); Hint(); }, "btn"));
            if (pass && spec.HasEditor && (spec.Kind == LevelKind.Program || spec.Kind == LevelKind.Evolve))
            {
                string src = runningSource;
                int id = spec.Id;
                resButtons.Add(B("SUBMIT RECORD", () => app.SubmitChamber(id, src), "btn"));
            }
            resButtons.Add(B("MAP", () => ShowMap(spec.Id), "btn"));
            Audio.Play(pass ? Sfx.Victory : Sfx.Defeat);
            UpdateSolutionButton();
            if (pass && first && next == null) ShowEpilogue();
        }

        string FailQuip(ChamberResult r)
        {
            var rec = progress.Get(spec.Id);
            if (rec.fails == 3 && spec.Hints != null) return "Three failures. The HINT button is not a moral failing.";
            if (r.Reason != null && r.Reason.StartsWith("too many losses")) return "Winning expensively is still losing here.";
            if (r.Reason == "out of time") return "The swarm did exactly what you wrote. Just not fast enough.";
            if (r.Reason == "your swarm is gone") return "Every mote ran your code to the very end.";
            return "The swarm did exactly what you wrote. That is the problem.";
        }

        void AnimateStars()
        {
            float t = Time.unscaledTime - starAnimAt;
            for (int i = 0; i < 3; i++)
            {
                float k = t - i * 0.28f;
                if (k < 0) continue;
                bool on = i < starsWon;
                if (on && !stars[i].On) { stars[i].On = true; Audio.Play(Sfx.Star, 1f, 1f + i * 0.12f); }
                stars[i].Pop(on ? 1f + 0.35f * Mathf.Max(0, 1f - k * 4f) : 1f);
            }
            if (t > 1.5f) starAnimAt = -1;
        }

        // ---------------------------------------------------------------- evolution

        public void DebugEvolve() => StartEvolve();

        void StartEvolve()
        {
            if (spec == null || spec.Kind != LevelKind.Evolve) return;
            var weights = new[] { evoSliders[0].value, evoSliders[1].value, evoSliders[2].value };
            if (weights[0] + weights[1] + weights[2] == 0) { app.Ui.Toast("reward something: all weights are zero"); return; }
            evolver = new Evolver(spec, spec.EvolveTemplate, weights, (ulong)UnityEngine.Random.Range(1, int.MaxValue));
            evoChart.A.Clear(); evoChart.B.Clear(); evoChart.Refresh();
            evolving = true;
            evoBtn.text = "STOP";
            Audio.Play(Sfx.Run);
        }

        void StopEvolve(bool keepChart = true)
        {
            evolving = false;
            if (evoBtn != null) evoBtn.text = "EVOLVE";
        }

        void EvolveFrame()
        {
            var sw = Stopwatch.StartNew();
            bool gen = false;
            while (sw.ElapsedMilliseconds < 9)
            {
                if (evolver.Step()) { gen = true; break; }
            }
            if (!gen) return;
            evoChart.A.Clear(); evoChart.A.AddRange(evolver.BestHistory.Select(x => (float)x));
            evoChart.B.Clear(); evoChart.B.AddRange(evolver.MeanHistory.Select(x => (float)x));
            evoChart.Refresh();
            var b = evolver.Best;
            evoStatus.text = "generation " + evolver.Generation + "   " + evolver.Evaluations + " runs   best fitness " + b.Fitness.ToString("0.000") + "   " +
                             (b.Result.Success ? "PASSES THE TEST" : "fails (" + (b.Result.Reason ?? "goal not met") + ", " + b.Result.Progress + "/" + b.Result.Target + ")");
            evoStatus.EnableInClassList("good", b.Result.Success);
            evoSource.Set(b.Source);
            Audio.Play(Sfx.Tick, 0.5f, 1f + Mathf.Clamp01((float)b.Fitness) * 0.5f);
            if (evolver.Generation >= 60) { StopEvolve(); app.Ui.Toast("60 generations: evolution paused. USE BEST, or change the reward."); }
        }

        void UseBest()
        {
            if (evolver?.Best?.Source == null) { app.Ui.Toast("evolve first"); return; }
            StopEvolve();
            SetCode(evolver.Best.Source);
            Run();
        }

        // ---------------------------------------------------------------- epilogue

        void BuildEpilogue(VisualElement root)
        {
            epilogue = Screen(root, "campaign-epilogue", "cmp-result");
            var box = Box("panel cmp-epi-box");
            box.Add(L("TRAINED", "cmp-res-title cmp-epi-title display"));
            epiText = L("", "amber cmp-wrap cmp-epi-text");
            epiTw = new Typewriter(epiText) { OnChar = () => Audio.Play(Sfx.Type, 0.2f) };
            box.Add(epiText);
            box.Add(L("YOUR LEARNING CURVE   bars: runs needed per test    line: failed runs", "panel-title cmp-gap"));
            var cb = Box("cmp-epi-chart");
            epiChart = new Chart { BarsA = true, AutoMax = true, ColorA = Palette.Me, ColorB = Palette.Foe };
            cb.Add(epiChart);
            box.Add(cb);
            box.Add(Row(B("NEXT", EpiNext, "btn btn-hot"), B("SANDBOX", () => { Exit(); app.StartSandbox(); }, "btn"), B("MAP", () => ShowMap(), "btn")));
            epilogue.Add(box);
            epilogue.RegisterCallback<ClickEvent>(e => { if (!epiTw.Done) epiTw.Finish(); });
        }

        void ShowEpilogue()
        {
            Show(result, false);
            Show(epilogue, true);
            progress.EpilogueSeen = true;
            epiChart.A.Clear(); epiChart.B.Clear();
            foreach (var l in Campaign.Levels)
            {
                var rec = progress.Get(l.Id);
                epiChart.A.Add(Mathf.Max(1, rec.runsToSolve > 0 ? rec.runsToSolve : rec.runs));
                epiChart.B.Add(rec.fails);
            }
            epiChart.Refresh();
            epiLines.Clear();
            var fin = Campaign.Get(604);
            if (fin?.Outro != null) epiLines.AddRange(fin.Outro);
            epiAt = 0;
            epiTw.Say(epiLines.Count > 0 ? epiLines[0] : "", 40f);
            Audio.Play(Sfx.Victory);
        }

        void EpiNext()
        {
            if (!epiTw.Done) { epiTw.Finish(); return; }
            epiAt++;
            if (epiAt < epiLines.Count) epiTw.Say(epiLines[epiAt], 40f);
            else ShowMap();
        }
    }

    /// <summary>Read-only, monospace view of an Argot program.</summary>
    public sealed class SourceView
    {
        public readonly Label Root;
        public SourceView()
        {
            Root = new Label();
            Root.AddToClassList("cmp-source");
        }
        public void Set(string src) => Root.text = src ?? "";
    }
}
