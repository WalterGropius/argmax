#nullable disable
using System;
using System.Collections.Generic;

namespace Argmax.Sim
{
    public enum ChamberGoal : byte
    {
        None,
        Mine,      // deliver N scrap
        Forge,     // own a mote of tier N
        Occupy,    // N of your motes on goal tiles
        Survive,   // keep the core alive N ticks
        Purge,     // destroy or rewrite N value
        Siege,     // bring the enemy core to zero (Target = its hp)
        Count,     // own N motes
        Exact,     // the values on goal tiles sum to exactly N, using at most MaxOnGoal motes
        Eliminate, // no enemy or feral motes left
        Value,     // own N total value
    }

    /// <summary>Which commands a level lets your hands give. Program levels give none: no hands, only code.</summary>
    [Flags]
    public enum Hands : ushort
    {
        None = 0, Move = 1, Attack = 2, Stop = 4, Hold = 8, Merge = 16, Split = 32, Beacon = 64, Spawn = 128,
        Produce = 256, Reprogram = 512, Auto = 1024, Meta = 2048, Slot = 4096,
        Units = Move | Attack | Stop | Hold,
        All = 0xFFFF,
    }

    public enum LevelKind : byte { Program, Hands, Hybrid, Evolve, Duel }

    /// <summary>
    /// A scripted command. Ids == null on a unit command selects your motes (all, or those of <see cref="Tier"/>,
    /// the first <see cref="Count"/> by id). Negative X targets: -1 goal, -2 enemy core, -3 hostiles, -4 scrap.
    /// </summary>
    public struct TimedCmd
    {
        public int Tick;
        public Cmd Cmd;
        public int Tier, Count;
        public TimedCmd(int tick, Cmd cmd, int tier = 0, int count = 0) { Tick = tick; Cmd = cmd; Tier = tier; Count = count; }
    }

    /// <summary>
    /// A level: a map, a goal, and what you may use. Test chambers and every campaign level share this shape,
    /// and all of them run on the same deterministic simulation as a ranked match.
    /// </summary>
    public sealed class ChamberSpec
    {
        public int Id;
        public string Code;              // "2-3", or "C1" for a test chamber
        public int Chapter;
        public LevelKind Kind = LevelKind.Program;
        public string Title;
        public string Brief;
        public string[] Proctor;         // briefing lines
        public string[] Outro;           // after a pass
        public string[] Map;
        public int StartMotes = 8, StartTier = 1, StartScrap, ScrapPerNode = 100;
        public ChamberGoal Goal;
        public int Target;
        public ChamberGoal Goal2;
        public int Target2;
        public int MaxOnGoal;
        public int MaxLost;              // fail once you've lost more value than this (0 = no limit)
        public int HoldTicks;            // the goals must stay met this many consecutive ticks
        public int TimeLimit = 1200;
        public int CoreHp = Rules.CoreHp;
        public bool Ferals;
        public int MaxFerals = 24, FeralTier = 1;
        public int WellEvery;            // ticks between well spawns (0 = Rules.WellEvery)
        public uint FeralProgram;        // 0 = generated from the seed
        public bool AutoProduce;
        public bool BeaconAtGoal;
        public bool OrderAtGoal;         // placed motes start with an order to the goal
        public int OrderTier;            // ...only those of this tier (0 = all)
        public string Starter;
        // campaign
        public uint StartProgram;        // program for placed motes in hands levels (0 = your solution)
        public Hands Hands;
        public string[] Vocab;           // allowed Argot words; null = everything
        public string[] Unlocks;         // what this level teaches
        public string[] Lock;            // words taught earlier but withheld here
        public uint[] Loadout;           // production slots (0 = your solution)
        public string Component;         // a passing solution is kept under this name
        public string[] Hints;
        public string Solution;          // reference program (tests, last hint)
        public TimedCmd[] Script;        // reference hands for hands levels (tests)
        public string ConceptTitle, ConceptBody, Interview;
        public int ParTicks, ParSymbols;
        public int EnemyTier = 1, EnemyScrap, EnemyCoreHp;
        public uint EnemyProgram = Presets.Sentry, EnemyMeta;
        public string[] EvolveTemplate;  // rules with {g}; the evolver searches the gains
        public int[] EvolveWeights;      // default fitness weights: progress, survival, speed

        public bool HasEditor => Kind != LevelKind.Hands;
    }

    public struct ChamberResult
    {
        public bool Success;
        public int Ticks;
        public int Symbols;
        public int RuleCount;
        public int Lost;
        public int StartValue;
        public int Progress, Progress2;
        public int Target, Target2;
        public int Stars;
        public string Error;
        public string Reason;
    }

    /// <summary>
    /// PROCTOR's test chambers plus the shared level engine. Scored like a Zachtronics histogram: ticks,
    /// symbols, losses. The server re-runs submissions with this exact code, so records are proofs.
    /// </summary>
    public static class Chambers
    {
        public static readonly ChamberSpec[] All =
        {
            new ChamberSpec
            {
                Id = 1, Code = "C1", Title = "HELLO, WORLD", Goal = ChamberGoal.Mine, Target = 24, StartMotes = 6, TimeLimit = 400,
                Brief = "Deliver 24 scrap to the core.", ParTicks = 160, ParSymbols = 8,
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
                Solution = "when not full: mine 3\nwhen full: seek home 4\nalways: spread",
            },
            new ChamberSpec
            {
                Id = 2, Code = "C2", Title = "FUSION", Goal = ChamberGoal.Forge, Target = 5, StartMotes = 16, TimeLimit = 900,
                Brief = "Forge a 32 from sixteen 2s.", ParTicks = 80, ParSymbols = 4,
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
                Solution = "always: merge 2\nalways: flock 2",
            },
            new ChamberSpec
            {
                Id = 3, Code = "C3", Title = "THINKING WITH PORTALS", Goal = ChamberGoal.Occupy, Target = 12, StartMotes = 14, TimeLimit = 1200,
                BeaconAtGoal = true, ParTicks = 50, ParSymbols = 3,
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
                Solution = "always: seek beacon 4",
            },
            new ChamberSpec
            {
                Id = 4, Code = "C4", Title = "THE NOISE", Goal = ChamberGoal.Purge, Target = 80, StartMotes = 8, StartScrap = 20,
                TimeLimit = 1500, Ferals = true, MaxFerals = 40, FeralTier = 2, CoreHp = 500, AutoProduce = true, ScrapPerNode = 300,
                ParTicks = 430, ParSymbols = 8,
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
                Solution = "always: seek scrap 2\nwhen enemy: seek enemy 3\nalways: flock",
            },
            new ChamberSpec
            {
                Id = 5, Code = "C5", Title = "CONVICTION", Goal = ChamberGoal.Siege, Target = 300, StartMotes = 16, TimeLimit = 1500,
                Brief = "Destroy the abandoned core. It shoots back.", ParTicks = 300, ParSymbols = 7,
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
                Solution = "always: merge 2\nalways: flock\nwhen big: seek core 3",
            },
        };

        public static ChamberSpec Get(int id)
        {
            foreach (var c in All) if (c.Id == id) return c;
            foreach (var c in Campaign.Levels) if (c.Id == id) return c;
            return null;
        }

        // ------------------------------------------------------------------ world construction

        /// <summary>Build the level's world. <paramref name="program"/> is your solution (id ChamberProgramId).</summary>
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
            me.CoreHp = map.HasCore[0] ? spec.CoreHp : 0;
            me.CoreX = map.CoreX[0]; me.CoreY = map.CoreY[0];
            me.Scrap = spec.StartScrap;
            me.Production = 0;
            me.Auto = spec.AutoProduce;
            bool meta = program.Kind == ProgramKind.Meta;
            uint unitProgram = meta ? (spec.StartProgram != 0 ? spec.StartProgram : Presets.Swarm) : program.Id;
            for (int s = 0; s < Rules.LoadoutSlots; s++) me.Loadout[s] = 0;
            if (spec.Loadout != null)
                for (int s = 0; s < spec.Loadout.Length && s < Rules.LoadoutSlots; s++)
                    me.Loadout[s] = spec.Loadout[s] == 0 ? unitProgram : spec.Loadout[s];
            else me.Loadout[0] = unitProgram;
            if (meta) me.Meta = program.Id;

            bool enemies = map.HasCore[1];
            foreach (var u in map.Units) if (u.Seat == 1) enemies = true;
            if (enemies)
            {
                var foe = w.Seats[1];
                foe.Kind = SeatKind.Ai;
                foe.CoreHp = !map.HasCore[1] ? 0 : spec.Goal == ChamberGoal.Siege ? spec.Target : spec.EnemyCoreHp > 0 ? spec.EnemyCoreHp : Rules.CoreHp;
                foe.CoreX = map.CoreX[1]; foe.CoreY = map.CoreY[1];
                foe.Meta = spec.EnemyMeta;
                foe.Scrap = spec.EnemyScrap;
                foe.Auto = spec.EnemyMeta != 0;
                Array.Copy(Presets.DefaultLoadout, foe.Loadout, Rules.LoadoutSlots);
            }
            if (spec.BeaconAtGoal && map.Goals.Count > 0)
            {
                long sx = 0, sy = 0;
                foreach (var g in map.Goals) { sx += g % map.W; sy += g / map.W; }
                me.BeaconOn = true;
                me.BeaconX = (short)(sx / map.Goals.Count); me.BeaconY = (short)(sy / map.Goals.Count);
            }
            Sim.Prepare(w);
            uint placed = spec.StartProgram != 0 ? spec.StartProgram : unitProgram;
            foreach (var u in map.Units)
            {
                int x = u.Cell % map.W, y = u.Cell / map.W;
                if (u.Seat == 0) Sim.PlaceMote(w, 0, placed, u.Tier > 0 ? u.Tier : spec.StartTier, x, y);
                else if (u.Seat == 1) Sim.PlaceMote(w, 1, spec.EnemyProgram, u.Tier > 0 ? u.Tier : u.Tier == -2 ? spec.EnemyTier + 2 : spec.EnemyTier, x, y);
                else Sim.PlaceMote(w, 2, spec.FeralProgram != 0 ? spec.FeralProgram : Rules.FeralProgramBase + (uint)(u.Cell % Rules.FeralPrograms), u.Tier > 0 ? u.Tier : 1, x, y);
            }
            if (map.HasCore[0])
                for (int k = 0; k < spec.StartMotes; k++) Sim.SpawnMote(w, 0, placed, spec.StartTier);
            if (spec.OrderAtGoal && map.Goals.Count > 0)
            {
                int gc = Target(w, -1);
                for (int i = 0; i < w.MoteCount; i++)
                {
                    ref var m = ref w.Motes[i];
                    if (m.Seat != 0 || (spec.OrderTier > 0 && m.Tier != spec.OrderTier)) continue;
                    m.Ox = (short)(gc % map.W); m.Oy = (short)(gc / map.W); m.Flags |= MoteFlags.HasOrder;
                }
            }
            w.ChamberTarget = Effective(w, spec.Goal, spec.Target);
            w.ChamberTarget2 = Effective(w, spec.Goal2, spec.Target2);
            Sim.Prepare(w);
            return w;
        }

        static int Effective(World w, ChamberGoal g, int target)
        {
            if (g != ChamberGoal.Eliminate) return target;
            int n = 0;
            for (int i = 0; i < w.MoteCount; i++) if (w.Motes[i].Seat != 0) n++;
            return n;
        }

        // ------------------------------------------------------------------ goals

        public static int Progress(World w) => Measure(w, w.Chamber.Goal, w.ChamberTarget);

        public static int Measure(World w, ChamberGoal goal, int target)
        {
            var me = w.Seats[0];
            switch (goal)
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
                case ChamberGoal.Siege: return w.Chamber.Target - w.Seats[1].CoreHp;
                case ChamberGoal.Count:
                {
                    int c = 0;
                    for (int i = 0; i < w.MoteCount; i++) if (w.Motes[i].Seat == 0) c++;
                    return c;
                }
                case ChamberGoal.Value:
                {
                    int v = 0;
                    for (int i = 0; i < w.MoteCount; i++) if (w.Motes[i].Seat == 0) v += w.Motes[i].Value;
                    return v;
                }
                case ChamberGoal.Exact:
                {
                    int v = 0;
                    var goals = w.Map.Goals;
                    for (int i = 0; i < w.MoteCount; i++)
                        if (w.Motes[i].Seat == 0 && goals.Contains(w.Map.Idx(w.Motes[i].X, w.Motes[i].Y))) v += w.Motes[i].Value;
                    return v;
                }
                case ChamberGoal.Eliminate:
                {
                    int left = 0;
                    for (int i = 0; i < w.MoteCount; i++) if (w.Motes[i].Seat != 0) left++;
                    return target - left;
                }
            }
            return 0;
        }

        public static int OnGoal(World w)
        {
            int c = 0;
            var goals = w.Map.Goals;
            for (int i = 0; i < w.MoteCount; i++)
                if (w.Motes[i].Seat == 0 && goals.Contains(w.Map.Idx(w.Motes[i].X, w.Motes[i].Y))) c++;
            return c;
        }

        public static bool Met(World w, ChamberGoal goal, int target, int progress)
        {
            if (goal == ChamberGoal.None) return true;
            if (goal == ChamberGoal.Exact)
                return progress == target && (w.Chamber.MaxOnGoal <= 0 || OnGoal(w) <= w.Chamber.MaxOnGoal);
            return progress >= target;
        }

        internal static void Check(World w)
        {
            var spec = w.Chamber;
            if (spec == null) return;
            w.ChamberProgress = Measure(w, spec.Goal, w.ChamberTarget);
            w.ChamberProgress2 = Measure(w, spec.Goal2, w.ChamberTarget2);
            var me = w.Seats[0];
            if (w.Map.HasCore[0] && me.CoreHp <= 0) { w.Winner = 1; w.ChamberReason = "your core fell"; return; }
            if (spec.MaxLost > 0 && me.Lost > spec.MaxLost) { w.Winner = 1; w.ChamberReason = "too many losses (" + me.Lost + " > " + spec.MaxLost + ")"; return; }
            if (Met(w, spec.Goal, w.ChamberTarget, w.ChamberProgress) && Met(w, spec.Goal2, w.ChamberTarget2, w.ChamberProgress2))
            {
                w.ChamberHeld++;
                if (w.ChamberHeld >= spec.HoldTicks) { w.Winner = 0; return; }
            }
            else w.ChamberHeld = 0;
            if (!w.Map.HasCore[0])
            {
                bool any = false;
                for (int i = 0; i < w.MoteCount && !any; i++) if (w.Motes[i].Seat == 0) any = true;
                if (!any) { w.Winner = 1; w.ChamberReason = "your swarm is gone"; return; }
            }
            if (w.Tick + 1 >= spec.TimeLimit) { w.Winner = 1; w.ChamberReason = "out of time"; }
        }

        /// <summary>Human-readable progress line, e.g. "scrap 12 / 24".</summary>
        public static string Describe(World w, ChamberGoal goal, int target, int progress)
        {
            switch (goal)
            {
                case ChamberGoal.Mine: return "scrap " + progress + " / " + target;
                case ChamberGoal.Forge: return "largest " + (progress > 0 ? Rules.Value(progress) : 0) + " / " + Rules.Value(target);
                case ChamberGoal.Occupy: return "on goal " + progress + " / " + target;
                case ChamberGoal.Survive: return "survive " + progress / Rules.TickHz + "s / " + target / Rules.TickHz + "s";
                case ChamberGoal.Purge: return "purged " + progress + " / " + target;
                case ChamberGoal.Siege: return "core damage " + progress + " / " + target;
                case ChamberGoal.Count: return "motes " + progress + " / " + target;
                case ChamberGoal.Value: return "value " + progress + " / " + target;
                case ChamberGoal.Exact: return "on pad " + progress + " = " + target + "?" + (w.Chamber.MaxOnGoal > 0 ? "  motes " + OnGoal(w) + " / max " + w.Chamber.MaxOnGoal : "");
                case ChamberGoal.Eliminate: return "hostiles left " + (target - progress);
            }
            return "";
        }

        public static string Describe(World w)
        {
            var s = Describe(w, w.Chamber.Goal, w.ChamberTarget, Measure(w, w.Chamber.Goal, w.ChamberTarget));
            if (w.Chamber.Goal2 != ChamberGoal.None) s += "   ·   " + Describe(w, w.Chamber.Goal2, w.ChamberTarget2, Measure(w, w.Chamber.Goal2, w.ChamberTarget2));
            if (w.Chamber.HoldTicks > 0) s += "   ·   held " + w.ChamberHeld / Rules.TickHz + "s / " + w.Chamber.HoldTicks / Rules.TickHz + "s";
            if (w.Chamber.MaxLost > 0) s += "   ·   losses " + w.Seats[0].Lost + " / max " + w.Chamber.MaxLost;
            return s;
        }

        public static int Stars(ChamberSpec spec, ChamberResult r)
        {
            if (!r.Success) return 0;
            int s = 1;
            if (spec.ParTicks <= 0 || r.Ticks <= spec.ParTicks) s++;
            if (spec.Kind == LevelKind.Hands ? r.Lost == 0 : (spec.ParSymbols <= 0 || r.Symbols <= spec.ParSymbols)) s++;
            return s;
        }

        // ------------------------------------------------------------------ headless runs

        /// <summary>
        /// Compile and run a solution to completion, optionally with scripted commands (hands levels).
        /// Deterministic: identical on every machine, so the server can verify any claim.
        /// </summary>
        public static ChamberResult Run(ChamberSpec spec, string source, IList<TimedCmd> script = null, int maxTicks = 0)
        {
            var r = new ChamberResult();
            Program prog;
            if (string.IsNullOrEmpty(source) && spec.StartProgram != 0)
            {
                var sp = Presets.Get(spec.StartProgram, 0);
                prog = Argot.Compile(sp.Source).Program;
            }
            else
            {
                var c = Argot.Compile(source, "SOLUTION", spec.Vocab);
                if (!c.Ok) { r.Error = string.Join("\n", c.Errors); return r; }
                if (c.Program.Kind != ProgramKind.Unit && spec.Kind != LevelKind.Duel) { r.Error = "this level takes a unit program, not @meta"; return r; }
                if (c.Program.Kind != ProgramKind.Meta && spec.Kind == LevelKind.Duel && spec.Hands == Hands.None) { r.Error = "this level takes a @meta program"; return r; }
                prog = c.Program;
            }
            prog.Id = Rules.ChamberProgramId;
            var w = Create(spec, prog);
            r.StartValue = Sim.Tally(w, 0).Value;
            var cmds = new List<Cmd>();
            int si = 0, limit = maxTicks > 0 ? maxTicks : spec.TimeLimit + 1;
            while (!w.Ended && w.Tick <= limit)
            {
                cmds.Clear();
                while (script != null && si < script.Count && script[si].Tick <= w.Tick)
                {
                    var t = script[si++];
                    var c = t.Cmd;
                    if (c.Ids == null && IsUnitCommand(c.Kind))
                    {
                        var ids = t.Tier > 0 ? MyIds(w, t.Tier) : MyIds(w);
                        if (t.Count > 0 && ids.Length > t.Count) Array.Resize(ref ids, t.Count);
                        c.Ids = ids;
                    }
                    if (c.X < 0) { int cell = Target(w, c.X); c.X = (short)(cell % w.W); c.Y = (short)(cell / w.W); }
                    cmds.Add(c);
                }
                Sim.Step(w, cmds);
            }
            r.Success = w.Winner == 0;
            r.Ticks = w.Tick;
            r.Symbols = prog.Symbols;
            r.RuleCount = prog.RuleCount;
            r.Lost = w.Seats[0].Lost;
            r.Progress = w.ChamberProgress;
            r.Progress2 = w.ChamberProgress2;
            r.Target = w.ChamberTarget;
            r.Target2 = w.ChamberTarget2;
            r.Reason = w.ChamberReason;
            r.Stars = Stars(spec, r);
            return r;
        }

        /// <summary>Resolve a symbolic target: -1 goal centroid, -2 enemy core ring, -3 hostiles' centroid, -4 nearest scrap.</summary>
        public static int Target(World w, int code)
        {
            var map = w.Map;
            long sx = 0, sy = 0, n = 0;
            switch (code)
            {
                case -1:
                    foreach (var g in map.Goals) { sx += g % map.W; sy += g / map.W; n++; }
                    break;
                case -2:
                    if (map.HasCore[1]) return map.NearestPassable(map.CoreX[1] + (map.CoreX[0] < map.CoreX[1] ? -2 : 2), map.CoreY[1], 3);
                    break;
                case -3:
                    for (int i = 0; i < w.MoteCount; i++) if (w.Motes[i].Seat != 0) { sx += w.Motes[i].X; sy += w.Motes[i].Y; n++; }
                    break;
                case -4:
                    foreach (var s in w.Scrap) if (s.Amount > 0) { sx += s.X; sy += s.Y; n++; break; }
                    break;
            }
            if (n == 0) return map.Idx(map.W / 2, map.H / 2);
            int c = map.NearestPassable((int)(sx / n), (int)(sy / n), 4);
            return c < 0 ? map.Idx((int)(sx / n), (int)(sy / n)) : c;
        }

        public static bool IsUnitCommand(CmdKind k) =>
            k == CmdKind.Move || k == CmdKind.Attack || k == CmdKind.Stop || k == CmdKind.Hold || k == CmdKind.Merge || k == CmdKind.Split || k == CmdKind.Reprogram;

        public static uint[] MyIds(World w)
        {
            var ids = new List<uint>();
            for (int i = 0; i < w.MoteCount; i++) if (w.Motes[i].Seat == 0) ids.Add(w.Motes[i].Id);
            return ids.ToArray();
        }

        /// <summary>Ids of your motes of one tier (for scripted merges and splits).</summary>
        public static uint[] MyIds(World w, int tier)
        {
            var ids = new List<uint>();
            for (int i = 0; i < w.MoteCount; i++) if (w.Motes[i].Seat == 0 && w.Motes[i].Tier == tier) ids.Add(w.Motes[i].Id);
            return ids.ToArray();
        }

        public static Hands Requires(CmdKind k)
        {
            switch (k)
            {
                case CmdKind.Move: return Hands.Move;
                case CmdKind.Attack: return Hands.Attack;
                case CmdKind.Stop: return Hands.Stop;
                case CmdKind.Hold: return Hands.Hold;
                case CmdKind.Merge: return Hands.Merge;
                case CmdKind.Split: return Hands.Split;
                case CmdKind.Reprogram: return Hands.Reprogram;
                case CmdKind.Beacon: case CmdKind.ClearBeacon: return Hands.Beacon;
                case CmdKind.Produce: return Hands.Produce;
                case CmdKind.Auto: return Hands.Auto;
                case CmdKind.Spawn: return Hands.Spawn;
                case CmdKind.SetSlot: return Hands.Slot;
                case CmdKind.SetMeta: return Hands.Meta;
                default: return Hands.All;
            }
        }
    }
}
