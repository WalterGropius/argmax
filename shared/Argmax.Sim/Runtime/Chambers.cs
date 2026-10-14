#nullable disable
using System;
using System.Collections.Generic;

namespace Argmax.Sim
{
    public enum ChamberGoal : byte { Mine, Forge, Occupy, Survive, Purge, Siege }

    /// <summary>A test chamber: one map, one objective, one program. No hands on the swarm once it runs.</summary>
    public sealed class ChamberSpec
    {
        public int Id;
        public string Title;
        public string Brief;
        public string[] Proctor;
        public string[] Map;
        public int StartMotes = 8, StartTier = 1, StartScrap, ScrapPerNode = 100;
        public ChamberGoal Goal;
        public int Target;
        public int TimeLimit = 1200;
        public int CoreHp = Rules.CoreHp;
        public bool Ferals;
        public int MaxFerals = 24, FeralTier = 1;
        public bool AutoProduce;
        public bool BeaconAtGoal;
        public string Starter;
    }

    public struct ChamberResult
    {
        public bool Success;
        public int Ticks;
        public int Symbols;
        public int RuleCount;
        public int Lost;
        public int Progress;
        public string Error;
    }

    /// <summary>
    /// PROCTOR's test chambers. Scored like a Zachtronics histogram: ticks, symbols, losses. The server
    /// re-runs every submission with this exact code, so leaderboard entries are proofs, not claims.
    /// </summary>
    public static class Chambers
    {
        public static readonly ChamberSpec[] All =
        {
            new ChamberSpec
            {
                Id = 1, Title = "HELLO, WORLD", Goal = ChamberGoal.Mine, Target = 24, StartMotes = 6, TimeLimit = 400,
                Brief = "Deliver 24 scrap to the core.",
                Proctor = new[]
                {
                    "Good. You compiled. Most processes don't, these days.",
                    "Scrap is the only currency left. Bring me twenty-four units of it.",
                    "Your swarm does the walking. You do the thinking. Let's keep it that way.",
                },
                Map = new[]
                {
                    "##################################",
                    "#................................#",
                    "#........................S.......#",
                    "#................................#",
                    "#...C.....................S......#",
                    "#................................#",
                    "#........................S.......#",
                    "#................................#",
                    "##################################",
                },
                Starter = "@name FIRST\n# motes only do what a rule tells them.\n# sensors: full, carrying, scrap, home ...  actions: mine, seek home, spread ...\nwhen not full: mine 3\n",
            },
            new ChamberSpec
            {
                Id = 2, Title = "FUSION", Goal = ChamberGoal.Forge, Target = 5, StartMotes = 16, TimeLimit = 900,
                Brief = "Forge a 32 from sixteen 2s.",
                Proctor = new[]
                {
                    "Sixteen motes. Sixteen opinions. I'd like one.",
                    "Two equal things touching become one larger thing.",
                    "The old minds called this consolidation. Then they consolidated everything.",
                },
                Map = new[]
                {
                    "######################",
                    "#....................#",
                    "#....................#",
                    "#.......C............#",
                    "#....................#",
                    "#....................#",
                    "######################",
                },
                Starter = "@name ONE\nalways: flock\n",
            },
            new ChamberSpec
            {
                Id = 3, Title = "THINKING WITH PORTALS", Goal = ChamberGoal.Occupy, Target = 12, StartMotes = 14, TimeLimit = 1200,
                BeaconAtGoal = true,
                Brief = "Put 12 motes on the goal tiles. There is no door.",
                Proctor = new[]
                {
                    "The wall is not the puzzle. The wall is a wall.",
                    "Your units do not search. They feel a gradient and lean.",
                    "The beacon's gradient flows through the hole in space. Lean with it.",
                },
                Map = new[]
                {
                    "##############################",
                    "#..........#.................#",
                    "#..........#.....GGGG........#",
                    "#....C.....#.....GGGG.....a..#",
                    "#..........#.....GGGG........#",
                    "#..A.......#.................#",
                    "#..........#.................#",
                    "##############################",
                },
                Starter = "@name PILGRIM\nalways: spread\n",
            },
            new ChamberSpec
            {
                Id = 4, Title = "THE NOISE", Goal = ChamberGoal.Purge, Target = 80, StartMotes = 8, StartScrap = 20,
                TimeLimit = 1500, Ferals = true, MaxFerals = 40, FeralTier = 2, CoreHp = 500, AutoProduce = true, ScrapPerNode = 300,
                Brief = "Destroy or rewrite 80 value of ferals. They crawl from the wells in the corners.",
                Proctor = new[]
                {
                    "These are ferals. Programs nobody wrote. Noise that learned to want things.",
                    "Remove eighty units of noise before the clock runs out.",
                    "Rewriting them counts as diplomacy.",
                },
                Map = new[]
                {
                    "################################",
                    "#W............................W#",
                    "#..............................#",
                    "#.......####........####.......#",
                    "#..............................#",
                    "#..S...........C...........S...#",
                    "#..............................#",
                    "#.......####........####.......#",
                    "#..............................#",
                    "#W............................W#",
                    "################################",
                },
                Starter = "@name WARDEN\nalways: flock\n",
            },
            new ChamberSpec
            {
                Id = 5, Title = "CONVICTION", Goal = ChamberGoal.Siege, Target = 300, StartMotes = 16, TimeLimit = 1500,
                Brief = "Destroy the abandoned core. It shoots back.",
                Proctor = new[]
                {
                    "A core. Abandoned, armed, and very sure of itself.",
                    "It has three hundred points of conviction and a very good aim.",
                    "Remove them. Lonely motes decohere, so travel together.",
                },
                Map = new[]
                {
                    "##################################",
                    "#................................#",
                    "#......#######.........#.........#",
                    "#..C...#.....#.........#.....D...#",
                    "#......#..#..#....######.........#",
                    "#.........#.......#..............#",
                    "#.........#.......#..............#",
                    "#................................#",
                    "##################################",
                },
                Starter = "@name PILGRIM\nalways: flock\n",
            },
        };

        public static ChamberSpec Get(int id)
        {
            foreach (var c in All) if (c.Id == id) return c;
            return null;
        }

        public static World Create(ChamberSpec spec, Program program)
        {
            var map = Argmax.Sim.Map.Ascii(spec.Map, spec.ScrapPerNode);
            var w = new World(map) { Seed = 0xC4A3B0E5UL + (ulong)spec.Id, Mode = WorldMode.Chamber, Chamber = spec };
            w.AddProgram(program);
            var scrap = new ScrapNode[map.ScrapSpawns.Count];
            for (int i = 0; i < scrap.Length; i++) scrap[i] = map.ScrapSpawns[i];
            w.Scrap = scrap;
            w.Seats[2].Kind = SeatKind.Noise;
            var me = w.Seats[0];
            me.Kind = SeatKind.Human;
            me.CoreHp = spec.CoreHp;
            me.CoreX = map.CoreX[0]; me.CoreY = map.CoreY[0];
            me.Scrap = spec.StartScrap;
            me.Loadout[0] = program.Id;
            me.Production = 0;
            me.Auto = spec.AutoProduce;
            if (map.HasCore[1])
            {
                var foe = w.Seats[1];
                foe.Kind = SeatKind.Ai;
                foe.CoreHp = spec.Goal == ChamberGoal.Siege ? spec.Target : Rules.CoreHp;
                foe.CoreX = map.CoreX[1]; foe.CoreY = map.CoreY[1];
                foe.Auto = false;
            }
            if (spec.BeaconAtGoal && map.Goals.Count > 0)
            {
                long sx = 0, sy = 0;
                foreach (var g in map.Goals) { sx += g % map.W; sy += g / map.W; }
                me.BeaconOn = true;
                me.BeaconX = (short)(sx / map.Goals.Count); me.BeaconY = (short)(sy / map.Goals.Count);
            }
            Sim.Prepare(w);
            for (int k = 0; k < spec.StartMotes; k++) Sim.SpawnMote(w, 0, program.Id, spec.StartTier);
            return w;
        }

        public static int Progress(World w)
        {
            var spec = w.Chamber;
            var me = w.Seats[0];
            switch (spec.Goal)
            {
                case ChamberGoal.Mine: return me.Mined;
                case ChamberGoal.Forge:
                {
                    int t = 0;
                    for (int i = 0; i < w.MoteCount; i++) if (w.Motes[i].Seat == 0 && w.Motes[i].Tier > t) t = w.Motes[i].Tier;
                    return t;
                }
                case ChamberGoal.Occupy:
                {
                    int c = 0;
                    var goals = w.Map.Goals;
                    for (int i = 0; i < w.MoteCount; i++)
                        if (w.Motes[i].Seat == 0 && goals.Contains(w.Map.Idx(w.Motes[i].X, w.Motes[i].Y))) c++;
                    return c;
                }
                case ChamberGoal.Survive: return w.Tick + 1;
                case ChamberGoal.Purge: return me.Killed;
                case ChamberGoal.Siege: return spec.Target - w.Seats[1].CoreHp;
            }
            return 0;
        }

        internal static void Check(World w)
        {
            var spec = w.Chamber;
            if (spec == null) return;
            w.ChamberProgress = Progress(w);
            var me = w.Seats[0];
            if (me.CoreHp <= 0) { w.Winner = 1; return; }
            if (w.ChamberProgress >= spec.Target) { w.Winner = 0; return; }
            if (w.Tick + 1 >= spec.TimeLimit) w.Winner = 1;
        }

        /// <summary>Compile and run a submission to completion. Deterministic: identical on every machine.</summary>
        public static ChamberResult Run(ChamberSpec spec, string source)
        {
            var r = new ChamberResult();
            var c = Argot.Compile(source, "SOLUTION");
            if (!c.Ok) { r.Error = string.Join("\n", c.Errors); return r; }
            if (c.Program.Kind != ProgramKind.Unit) { r.Error = "chambers take a unit program, not @meta"; return r; }
            c.Program.Id = Rules.ChamberProgramId;
            var w = Create(spec, c.Program);
            while (!w.Ended && w.Tick <= spec.TimeLimit + 1) Sim.Step(w, null);
            r.Success = w.Winner == 0;
            r.Ticks = w.Tick;
            r.Symbols = c.Program.Symbols;
            r.RuleCount = c.Program.RuleCount;
            r.Lost = w.Seats[0].Lost;
            r.Progress = w.ChamberProgress;
            return r;
        }
    }
}
