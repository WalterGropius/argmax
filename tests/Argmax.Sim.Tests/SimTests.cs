using System;
using System.Collections.Generic;
using System.Diagnostics;
using Argmax.Sim;
using Xunit;
using Xunit.Abstractions;

namespace Argmax.Sim.Tests
{
    public class ArgotTests
    {
        [Fact]
        public void Presets_compile_and_round_trip()
        {
            foreach (var p in Presets.All)
            {
                var again = Argot.Compile(Argot.Decompile(p));
                Assert.True(again.Ok, p.Name + ": " + string.Join("; ", again.Errors));
                Assert.Equal(p.Chips, again.Program.Chips);
                Assert.Equal(p.Kind, again.Program.Kind);
            }
        }

        [Fact]
        public void Errors_are_helpful()
        {
            var r = Argot.Compile("when enemey: seek enemy");
            Assert.False(r.Ok);
            Assert.Contains("did you mean 'enemy'", r.Errors[0]);
            Assert.False(Argot.Compile("when enemy seek enemy").Ok);
            Assert.False(Argot.Compile("always: produce swarm").Ok); // meta op in a unit program
            Assert.False(Argot.Compile("@meta\nalways: flock").Ok);  // unit op in a metaprogram
            Assert.True(Argot.Compile("@meta\nwhen rich and not threat: spawn 4").Ok);
        }

        [Fact]
        public void Instruction_tax()
        {
            var small = Argot.Compile("always: flock\nwhen enemy: seek enemy").Program;
            Assert.Equal(Rules.MoteCost, small.SpawnCost);
            var big = Argot.Compile(string.Join("\n", new[] { "always: flock", "always: spread", "when enemy: seek enemy", "when hurt: avoid enemy", "when order: seek order", "when pair: merge" })).Program;
            Assert.Equal(Rules.MoteCost + 2, big.SpawnCost);
        }

        [Fact]
        public void Ferals_are_valid_programs()
        {
            for (ulong seed = 1; seed < 40; seed++)
                for (int k = 0; k < Rules.FeralPrograms; k++)
                {
                    var p = Presets.Get(Rules.FeralProgramBase + (uint)k, seed);
                    Assert.NotNull(p);
                    Assert.True(p.RuleCount >= 1);
                    Assert.Equal(ProgramKind.Unit, p.Kind);
                }
        }
    }

    public class MapTests
    {
        [Fact]
        public void Duel_map_is_point_symmetric_and_deterministic()
        {
            foreach (ulong seed in new ulong[] { 1, 42, 9001, 123456789 })
            {
                var a = Map.Duel(seed);
                for (int y = 0; y < a.H; y++)
                    for (int x = 0; x < a.W; x++)
                    {
                        var c = a.Cell[a.Idx(x, y)];
                        var m = a.Cell[a.Idx(a.W - 1 - x, a.H - 1 - y)];
                        Assert.Equal(c, m);
                    }
                var h0 = a.Home(0);
                Assert.NotEqual(Fields.Unreachable, h0[a.Idx(a.CoreX[1] + 2, a.CoreY[1])]);
            }
        }

        [Fact]
        public void Chamber_maps_are_sound()
        {
            foreach (var spec in Chambers.All)
            {
                var m = Map.Ascii(spec.Map, spec.ScrapPerNode);
                Assert.True(m.HasCore[0], spec.Title);
                var h = m.Home(0);
                foreach (var g in m.Goals) Assert.NotEqual(Fields.Unreachable, h[g]);
                if (m.HasCore[1]) Assert.NotEqual(Fields.Unreachable, m.Home(1)[m.Idx(m.CoreX[0] + 2, m.CoreY[0])]);
            }
        }

        [Fact]
        public void Gradients_flow_through_portals()
        {
            var m = Map.Ascii(Chambers.Get(3).Map, 100);
            var f = new ushort[m.W * m.H];
            var q = new int[m.W * m.H * 2];
            Fields.Bfs(m, new List<int> { m.Goals[0] }, f, q);
            int portalA = m.Portals[0];
            Assert.NotEqual(Fields.Unreachable, f[portalA]);
            Assert.NotEqual(Fields.Unreachable, f[m.Idx(m.CoreX[0] + 2, m.CoreY[0])]);
        }
    }

    public class SimTests
    {
        readonly ITestOutputHelper output;
        public SimTests(ITestOutputHelper output) { this.output = output; }

        static World Bots(ulong seed, uint meta0, uint meta1) => Sim.NewDuel(seed, SeatKind.Ai, SeatKind.Ai, meta0, meta1);

        [Fact]
        public void Same_seed_same_history()
        {
            var a = Bots(77, Presets.Overmind, Presets.Turtle);
            var b = Bots(77, Presets.Overmind, Presets.Turtle);
            for (int t = 0; t < 2500; t++)
            {
                Sim.Step(a, null); Sim.Step(b, null);
                if (t % 250 == 0) Assert.Equal(a.StateHash(), b.StateHash());
            }
            Assert.Equal(a.StateHash(), b.StateHash());
        }

        [Fact]
        public void Clone_mid_game_continues_identically()
        {
            var a = Bots(5, Presets.Overmind, Presets.Locust);
            for (int t = 0; t < 900; t++) Sim.Step(a, null);
            var b = a.Clone();
            for (int t = 0; t < 900; t++) { Sim.Step(a, null); Sim.Step(b, null); }
            Assert.Equal(a.StateHash(), b.StateHash());
        }

        [Fact]
        public void Round_trip_through_rows_is_lossless()
        {
            // what the server does every tick: rebuild the world from rows, step, write back
            var a = Bots(11, Presets.Overmind, Presets.Overmind);
            for (int t = 0; t < 600; t++) Sim.Step(a, null);
            var rebuilt = Sim.NewDuel(11, SeatKind.Ai, SeatKind.Ai, Presets.Overmind, Presets.Overmind);
            rebuilt.Tick = a.Tick; rebuilt.NextId = a.NextId;
            rebuilt.MoteCount = 0;
            var shuffled = new List<Mote>();
            for (int i = 0; i < a.MoteCount; i++) shuffled.Add(a.Motes[i]);
            shuffled.Reverse();
            foreach (var m in shuffled) rebuilt.AddMote(m);
            rebuilt.SortMotes();
            for (int s = 0; s < 3; s++) rebuilt.Seats[s] = a.Seats[s].Clone();
            rebuilt.Scrap = (ScrapNode[])a.Scrap.Clone();
            rebuilt.Trails[0] = (byte[])a.Trails[0].Clone();
            rebuilt.Trails[1] = (byte[])a.Trails[1].Clone();
            for (int t = 0; t < 300; t++) { Sim.Step(a, null); Sim.Step(rebuilt, null); }
            Assert.Equal(a.StateHash(), rebuilt.StateHash());
        }

        [Fact]
        public void Bot_match_finishes_with_a_winner()
        {
            var w = Bots(2026, Presets.Overmind, Presets.Locust);
            var sw = Stopwatch.StartNew();
            int maxMotes = 0;
            while (!w.Ended) { Sim.Step(w, null); maxMotes = Math.Max(maxMotes, w.MoteCount); }
            sw.Stop();
            output.WriteLine($"winner {w.Winner} at tick {w.Tick} ({w.Tick / 10 / 60}m{w.Tick / 10 % 60}s), peak motes {maxMotes}, {sw.ElapsedMilliseconds} ms total, {sw.Elapsed.TotalMilliseconds * 1000 / w.Tick:F0} us/tick");
            for (int s = 0; s < 2; s++)
                output.WriteLine($"  seat {s}: core {w.Seats[s].CoreHp} scrap {w.Seats[s].Scrap} mined {w.Seats[s].Mined} killed {w.Seats[s].Killed} lost {w.Seats[s].Lost}");
            Assert.True(w.Ended);
        }

        [Fact]
        [Trait("Category", "Slow")]
        public void Meta_presets_tournament()
        {
            var metas = Presets.MetaIds;
            var wins = new int[metas.Length];
            for (int a = 0; a < metas.Length; a++)
                for (int b = 0; b < metas.Length; b++)
                {
                    if (a == b) continue;
                    var w = Bots((ulong)(a * 31 + b * 7 + 1), metas[a], metas[b]);
                    while (!w.Ended) Sim.Step(w, null);
                    if (w.Winner == 0) wins[a]++; else if (w.Winner == 1) wins[b]++;
                    output.WriteLine($"{Presets.Get(metas[a], 0).Name,-10} vs {Presets.Get(metas[b], 0).Name,-10} -> {(w.Winner == 0 ? "first" : w.Winner == 1 ? "second" : "draw")} @ {w.Tick}");
                }
            for (int i = 0; i < metas.Length; i++) output.WriteLine($"{Presets.Get(metas[i], 0).Name}: {wins[i]} wins");
        }

        [Fact]
        public void Two_equal_motes_fuse()
        {
            var w = Chambers.Create(Chambers.Get(2), Presets.Get(Presets.Swarm, 0));
            Sim.Refresh(w);
            int before = w.MoteCount;
            var ids = new uint[w.MoteCount];
            for (int i = 0; i < w.MoteCount; i++) ids[i] = w.Motes[i].Id;
            var cmds = new List<Cmd> { new Cmd { Seat = 0, Kind = CmdKind.Merge, Ids = ids } };
            Sim.Step(w, cmds);
            Assert.True(w.MoteCount < before, "some adjacent pairs should have fused on the first tick");
            int maxTier = 0;
            for (int i = 0; i < w.MoteCount; i++) maxTier = Math.Max(maxTier, w.Motes[i].Tier);
            Assert.Equal(2, maxTier);
        }

        [Fact]
        public void Analyze_explains_a_decision()
        {
            var w = Bots(3, 0, 0);
            var a = Sim.Analyze(w, w.Motes[0].Id);
            Assert.True(a.Valid);
            int sum = 0;
            foreach (var p in a.Permille) sum += p;
            Assert.InRange(sum, 990, 1000);
        }

        [Fact]
        public void Commander_observation_has_fixed_size()
        {
            var w = Bots(9, Presets.Overmind, Presets.Overmind);
            var o = new float[CommanderObs.Size];
            for (int t = 0; t < 300; t++) Sim.Step(w, null);
            Sim.Refresh(w);
            CommanderObs.Write(w, 0, o);
            CommanderObs.Write(w, 1, o);
            foreach (var v in o) Assert.False(float.IsNaN(v));
        }
    }

    public class ChamberTests
    {
        readonly ITestOutputHelper output;
        public ChamberTests(ITestOutputHelper output) { this.output = output; }

        public static readonly Dictionary<int, string> Solutions = new Dictionary<int, string>
        {
            [1] = "when not full: mine 3\nwhen full: seek home 4\nalways: spread",
            [2] = "always: merge 2\nalways: flock 2",
            [3] = "always: seek beacon 4",
            [4] = "always: seek scrap 2\nwhen enemy: seek enemy 3\nalways: flock",
            [5] = "always: merge 2\nalways: flock\nwhen big: seek core 3",
        };

        [Fact]
        public void Starters_run_and_reference_solutions_pass()
        {
            foreach (var spec in Chambers.All)
            {
                var starter = Chambers.Run(spec, spec.Starter);
                Assert.Null(starter.Error);
                var r = Chambers.Run(spec, Solutions[spec.Id]);
                output.WriteLine($"{spec.Id} {spec.Title,-24} starter:{(starter.Success ? "PASS" : "fail")}  reference:{(r.Success ? "PASS" : "FAIL")} ticks {r.Ticks} symbols {r.Symbols} lost {r.Lost} progress {r.Progress}/{spec.Target}");
                Assert.True(r.Success, spec.Title + " reference solution should pass (progress " + r.Progress + "/" + spec.Target + ")");
            }
        }

        [Fact]
        public void Runs_are_reproducible()
        {
            var a = Chambers.Run(Chambers.Get(4), Solutions[4]);
            var b = Chambers.Run(Chambers.Get(4), Solutions[4]);
            Assert.Equal(a.Ticks, b.Ticks);
            Assert.Equal(a.Lost, b.Lost);
        }
    }
}

namespace Argmax.Sim.Tests
{
    /// <summary>Golden values shared with the Unity EditMode tests and the server smoke test.</summary>
    public class GoldenTests
    {
        readonly Xunit.Abstractions.ITestOutputHelper output;
        public GoldenTests(Xunit.Abstractions.ITestOutputHelper output) { this.output = output; }

        public const ulong Seed = 77;
        public const int Ticks = 1000;

        [Xunit.Fact]
        public void Print_golden_hash()
        {
            var w = Sim.NewDuel(Seed, SeatKind.Ai, SeatKind.Ai, Presets.Overmind, Presets.Turtle);
            for (int t = 0; t < Ticks; t++) Sim.Step(w, null);
            output.WriteLine("GOLDEN duel seed 77 OVERMIND v TURTLE @1000 = " + w.StateHash());
            var h = Sim.NewDuel(42, SeatKind.Human, SeatKind.Ai, 0, Presets.Turtle);
            for (int t = 0; t < 300; t++) Sim.Step(h, null);
            output.WriteLine("GOLDEN server-shape seed 42 human-idle v TURTLE @300 = " + h.StateHash());
        }
    }
}

namespace Argmax.Sim.Tests
{
    public class IsolationTests
    {
        static ulong Run(ulong seed)
        {
            var w = Sim.NewDuel(seed, SeatKind.Human, SeatKind.Ai, 0, Presets.Turtle);
            for (int t = 0; t < 300; t++) Sim.Step(w, null);
            return w.StateHash();
        }

        /// <summary>Regression: one match's caches must never leak into another's (the server hosts many).</summary>
        [Xunit.Fact]
        public void Matches_do_not_influence_each_other()
        {
            ulong before = Run(42);
            for (ulong s = 1; s < 48; s++)
                for (int k = 0; k < Rules.FeralPrograms; k++) Presets.Get(Rules.FeralProgramBase + (uint)k, s);
            Xunit.Assert.Equal(before, Run(42));
        }
    }
}
