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
// Jev reads JEV_API_KEY from the environment or ./.env.local (gitignored).

static class Arena
{
    static int Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "tournament";
        if (mode == "jev") return Jev(args.Length > 1 ? args[1] : "OVERMIND", args.Length > 2 ? ulong.Parse(args[2]) : 2026UL);
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
