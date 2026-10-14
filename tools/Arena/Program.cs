using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using Argmax.Sim;
using SimProgram = Argmax.Sim.Program;

// ARGMAX arena: headless matches on the shared simulation.
//   dotnet run --project tools/Arena -- tournament            every metaprogram vs every other
//   dotnet run --project tools/Arena -- jev OVERMIND [seed]    TypeSafe Jev (live API) vs a metaprogram
//   dotnet run --project tools/Arena -- highlights A B [n]     find the most violent 48 s window (for capture)
//   dotnet run --project tools/Arena -- campaign [id]          run every level's starter and reference solution
// Jev reads JEV_API_KEY from the environment or ./.env.local (gitignored).

static class Arena
{
    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "tournament";
        if (mode == "jev") return Jev(args.Length > 1 ? args[1] : "OVERMIND", args.Length > 2 ? ulong.Parse(args[2]) : 2026UL);
        if (mode == "evolve") return Evolve(int.Parse(args[1]), int.Parse(args[2]), args.Length > 5 ? new[] { int.Parse(args[3]), int.Parse(args[4]), int.Parse(args[5]) } : null);
        if (mode == "syllabus") return Syllabus(args.Length > 1 ? args[1] : "md");
        if (mode == "campaign" && args.Length > 2) return CampaignTrace(int.Parse(args[1]), args[2], args.Length > 3 ? int.Parse(args[3]) : -1);
        if (mode == "campaign") return CampaignTable(args.Length > 1 ? int.Parse(args[1]) : 0);
        if (mode == "highlights") return Highlights(args.Length > 1 ? args[1] : "OVERMIND", args.Length > 2 ? args[2] : "ASSEMBLER", args.Length > 3 ? int.Parse(args[3]) : 40);
        return Tournament();
    }

    static int Tournament()
    {
        var ids = Presets.MetaIds;
        var wins = new int[ids.Length];
        var sw = Stopwatch.StartNew();
        long ticks = 0;
        for (int a = 0; a < ids.Length; a++)
            for (int b = 0; b < ids.Length; b++)
            {
                if (a == b) continue;
                var w = Sim.NewDuel((ulong)(a * 131 + b * 17 + 5), SeatKind.Ai, SeatKind.Ai, ids[a], ids[b]);
                while (!w.Ended) Sim.Step(w, null);
                ticks += w.Tick;
                string res = w.Winner == 0 ? Name(ids[a]) : w.Winner == 1 ? Name(ids[b]) : "draw";
                if (w.Winner == 0) wins[a]++; else if (w.Winner == 1) wins[b]++;
                Console.WriteLine($"{Name(ids[a]),-10} vs {Name(ids[b]),-10}  ->  {res,-10} at {w.Tick / 10 / 60}:{w.Tick / 10 % 60:00}");
            }
        Console.WriteLine();
        for (int i = 0; i < ids.Length; i++) Console.WriteLine($"{Name(ids[i]),-10} {wins[i]} wins");
        Console.WriteLine($"\n{ticks} ticks simulated in {sw.Elapsed.TotalSeconds:0.0}s ({ticks / sw.Elapsed.TotalSeconds:0} ticks/s)");
        return 0;
    }

    static string Name(uint id) => Presets.Get(id, 0).Name;

    /// <summary>Step one level and print the state every 50 ticks. which: solution | starter | literal Argot source.</summary>
    static int CampaignTrace(int id, string which, int dumpAt)
    {
        var l = Chambers.Get(id);
        string src = which == "solution" ? l.Solution : which == "starter" ? l.Starter : which.Replace("\\n", "\n");
        if (l.Kind == LevelKind.Hands && which == "solution") src = null;
        SimProgram prog;
        if (string.IsNullOrEmpty(src)) prog = Argot.Compile(Presets.Get(l.StartProgram, 0).Source).Program;
        else
        {
            var c = Argot.Compile(src, "TRACE", l.Vocab);
            if (!c.Ok) { Console.WriteLine(string.Join("\n", c.Errors)); return 1; }
            prog = c.Program;
        }
        prog.Id = Rules.ChamberProgramId;
        var w = Chambers.Create(l, prog);
        var cmds = new System.Collections.Generic.List<Cmd>();
        int si = 0;
        var script = which == "solution" ? l.Script : null;
        while (!w.Ended && w.Tick <= l.TimeLimit + 1)
        {
            cmds.Clear();
            while (script != null && si < script.Length && script[si].Tick <= w.Tick)
            {
                var t = script[si++]; var c = t.Cmd;
                if (c.Ids == null && Chambers.IsUnitCommand(c.Kind))
                {
                    var ids = t.Tier > 0 ? Chambers.MyIds(w, t.Tier) : Chambers.MyIds(w);
                    if (t.Count > 0 && ids.Length > t.Count) Array.Resize(ref ids, t.Count);
                    c.Ids = ids;
                }
                if (c.X < 0) { int cell = Chambers.Target(w, c.X); c.X = (short)(cell % w.W); c.Y = (short)(cell / w.W); }
                cmds.Add(c);
            }
            if (w.Tick == dumpAt) Dump(w);
            if (w.Tick % 50 == 0)
            {
                var a = Sim.Tally(w, 0); var b = Sim.Tally(w, 1); var f = Sim.Tally(w, 2);
                Console.WriteLine($"t{w.Tick,5}  me {a.Count,3}/{a.Value,-4} foe {b.Count,3}/{b.Value,-4} feral {f.Count,3}/{f.Value,-4} core {w.Seats[0].CoreHp,5} {w.Seats[1].CoreHp,5}  scrap {w.Seats[0].Scrap,4}/{w.Seats[1].Scrap,-4} lost {w.Seats[0].Lost,4}  {Chambers.Describe(w)}");
            }
            Sim.Step(w, cmds);
        }
        Console.WriteLine($"end t{w.Tick} winner {w.Winner} {w.ChamberReason} lost {w.Seats[0].Lost} killed {w.Seats[0].Killed} :: {Chambers.Describe(w)}");
        return 0;
    }

    /// <summary>Run the level's genetic algorithm for a number of generations and report the champion.</summary>
    static int Evolve(int id, int gens, int[] weights)
    {
        var l = Chambers.Get(id);
        var ev = new Evolver(l, l.EvolveTemplate, weights ?? l.EvolveWeights);
        var sw = Stopwatch.StartNew();
        while (ev.Generation < gens) ev.Step();
        for (int g = 0; g < ev.BestHistory.Count; g++) Console.WriteLine($"gen {g,3}  best {ev.BestHistory[g]:0.000}  mean {ev.MeanHistory[g]:0.000}");
        var b = ev.Best;
        Console.WriteLine($"champion {b.Fitness:0.000} success {b.Result.Success} prog {b.Result.Progress}/{b.Result.Target} lost {b.Result.Lost} t {b.Result.Ticks}  [{string.Join(",", b.Gains)}]  {ev.Evaluations} evals in {sw.Elapsed.TotalSeconds:0.0}s");
        Console.WriteLine(b.Source);
        return b.Result.Success ? 0 : 1;
    }

    /// <summary>The curriculum as a Markdown or HTML table, generated from the level data.</summary>
    static int Syllabus(string fmt)
    {
        string Esc(string x) => System.Net.WebUtility.HtmlEncode(x ?? "");
        foreach (var l in Campaign.Levels)
        {
            string words = l.Unlocks != null && l.Unlocks.Length > 0 ? string.Join(" ", l.Unlocks) : "";
            string kind = l.Kind == LevelKind.Hands ? "hands" : l.Kind == LevelKind.Hybrid ? "code + hands" : l.Kind == LevelKind.Evolve ? "evolve" : l.Kind == LevelKind.Duel ? (l.Hands == Hands.None ? "@meta" : "duel") : "code";
            string echo = l.Interview ?? "";
            if (fmt == "html")
                Console.WriteLine($"        <tr><td>{l.Code}</td><td>{Esc(l.Title)}</td><td>{kind}</td><td>{(words.Length > 0 ? "<code>" + Esc(words) + "</code>" : "")}</td><td>{Esc(l.ConceptTitle)}</td></tr>");
            else
                Console.WriteLine($"| {l.Code} | {l.Title} | {kind} | {(words.Length > 0 ? "`" + words + "`" : "")} | {l.ConceptTitle} | {echo.Replace("|", "/")} |");
        }
        return 0;
    }

    static void Dump(World w)
    {
        var map = w.Map;
        for (int y = map.H - 1; y >= 0; y--)
        {
            var sb = new StringBuilder();
            for (int x = 0; x < map.W; x++)
            {
                int i = map.Idx(x, y);
                char c = map.Passable(x, y) ? (map.Goals.Contains(i) ? 'G' : '.') : '#';
                for (int k = 0; k < w.MoteCount; k++)
                    if (w.Motes[k].X == x && w.Motes[k].Y == y) c = w.Motes[k].Seat == 0 ? (char)('0' + w.Motes[k].Tier) : w.Motes[k].Seat == 1 ? 'e' : 'f';
                sb.Append(c);
            }
            Console.WriteLine(sb.ToString());
        }
    }

    /// <summary>Every campaign level: does the starter fail, does the reference pass, and how close to par.</summary>
    static int CampaignTable(int only)
    {
        int bad = 0;
        var sw = Stopwatch.StartNew();
        foreach (var l in Campaign.Levels)
        {
            if (only != 0 && l.Id != only) continue;
            string src = l.Kind == LevelKind.Hands ? "" : l.Solution;
            var r = Chambers.Run(l, src, l.Script);
            string starter = "-";
            if (l.Kind != LevelKind.Hands && l.Starter != null)
            {
                var s = Chambers.Run(l, l.Starter);
                starter = s.Error != null ? "ERR " + s.Error : (s.Success ? "PASS" : $"fail {s.Progress}/{s.Target} {s.Reason}") + $" t{s.Ticks} lost {s.Lost}";
            }
            if (!r.Success) bad++;
            string res = r.Error != null ? "ERROR " + r.Error : r.Success ? "PASS" : "FAIL " + r.Reason;
            Console.WriteLine($"{l.Code,-4} {l.Title,-15} {res,-22} t {r.Ticks,4}/{l.ParTicks,-4} sym {r.Symbols,2}/{l.ParSymbols,-2} lost {r.Lost,3}/{r.StartValue,-3} prog {r.Progress}/{r.Target}{(l.Goal2 != ChamberGoal.None ? $" + {r.Progress2}/{r.Target2}" : "")} *{r.Stars}  starter: {starter}");
        }
        Console.WriteLine($"{bad} failing, {sw.Elapsed.TotalSeconds:0.0}s");
        return bad;
    }

    /// <summary>Scan seeds for the window with the most shots, merges and deaths: a highlight reel finder.</summary>
    static int Highlights(string a, string b, int seeds)
    {
        uint ia = Presets.IdOf(a), ib = Presets.IdOf(b);
        const int window = 480;
        var log = new StepLog();
        (ulong seed, int start, int score, int merges, int len) best = default;
        for (ulong seed = 1; seed <= (ulong)seeds; seed++)
        {
            var w = Sim.NewDuel(seed, SeatKind.Ai, SeatKind.Ai, ia, ib);
            var per = new System.Collections.Generic.List<int>();
            var mer = new System.Collections.Generic.List<int>();
            while (!w.Ended && w.Tick < 9000)
            {
                Sim.Step(w, null, log);
                per.Add(log.Shots.Count + 4 * log.Deaths.Count + 6 * log.Merges.Count);
                mer.Add(log.Merges.Count);
            }
            int run = 0, runM = 0;
            for (int t = 0; t < per.Count; t++)
            {
                run += per[t]; runM += mer[t];
                if (t >= window) { run -= per[t - window]; runM -= mer[t - window]; }
                if (t >= window && run > best.score) best = (seed, t - window, run, runM, per.Count);
            }
        }
        Console.WriteLine($"best: seed {best.seed} window starts at tick {best.start} (score {best.score}, merges {best.merges}, game length {best.len})");
        return 0;
    }

    static int Jev(string opponent, ulong seed)
    {
        string key = Environment.GetEnvironmentVariable("JEV_API_KEY");
        if (string.IsNullOrEmpty(key) && File.Exists(".env.local"))
            foreach (var line in File.ReadAllLines(".env.local"))
                if (line.StartsWith("JEV_API_KEY=")) key = line.Substring(12).Trim();
        if (string.IsNullOrEmpty(key)) { Console.Error.WriteLine("set JEV_API_KEY or create .env.local"); return 2; }
        uint opp = Presets.IdOf(opponent);
        if (opp < 100) { Console.Error.WriteLine("unknown metaprogram " + opponent); return 2; }

        var w = Sim.NewDuel(seed, SeatKind.Ai, SeatKind.Ai, 0, opp);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Add("Authorization", "Bearer " + key);
        int calls = 0, failures = 0, inTok = 0, outTok = 0;
        double latency = 0;
        Console.WriteLine($"JEV (seat 0) vs {opponent} (seat 1), seed {seed}. One decision every {JevBrief.DecideEvery} ticks.\n");
        while (!w.Ended && w.Tick < 9000)
        {
            if (w.Tick % JevBrief.DecideEvery == 0)
            {
                Sim.Refresh(w);
                var body = new StringContent(JevBrief.Request(w, 0), Encoding.UTF8, "application/json");
                var sw = Stopwatch.StartNew();
                try
                {
                    var resp = http.PostAsync(JevBrief.Endpoint, body).GetAwaiter().GetResult();
                    var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    latency += sw.Elapsed.TotalMilliseconds;
                    if (resp.IsSuccessStatusCode && JevBrief.TryDecode(text, w, 0, out var d, out var reading))
                    {
                        calls++;
                        inTok += reading.InputTokens; outTok += reading.OutputTokens;
                        Meta.Apply(w, 0, d);
                        if (calls % 5 == 1) Console.WriteLine($"[{w.Tick / 10 / 60}:{w.Tick / 10 % 60:00}] {reading.Summary()}");
                    }
                    else { failures++; Console.WriteLine($"[{w.Tick}] jev failed: {(int)resp.StatusCode} {Trim(text)}"); }
                }
                catch (Exception e) { failures++; Console.WriteLine($"[{w.Tick}] jev error: {e.Message}"); }
                if (failures > 5) { Console.WriteLine("too many failures, stopping"); return 1; }
            }
            Sim.Step(w, null);
        }
        var me = w.Seats[0]; var them = w.Seats[1];
        Console.WriteLine();
        Console.WriteLine(w.Winner == 0 ? "JEV WINS" : w.Winner == 1 ? opponent + " WINS" : "DRAW / unfinished");
        Console.WriteLine($"time {w.Tick / 10 / 60}:{w.Tick / 10 % 60:00}  |  JEV mined {me.Mined} killed {me.Killed} lost {me.Lost} core {me.CoreHp}  |  {opponent} mined {them.Mined} killed {them.Killed} lost {them.Lost} core {them.CoreHp}");
        Console.WriteLine($"{calls} Jev decisions, {failures} failures, avg latency {(calls > 0 ? latency / calls : 0):0} ms, tokens in {inTok} / out {outTok}");
        return 0;
    }

    static string Trim(string s) => s.Length > 200 ? s.Substring(0, 200) + "..." : s;
}
