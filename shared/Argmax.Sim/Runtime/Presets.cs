#nullable disable
using System;
using System.Collections.Generic;

namespace Argmax.Sim
{
    /// <summary>
    /// Built-in programs. Ids are fixed so every client, the server and the gym agree on them without
    /// a database lookup. Feral (Noise) programs are generated from the match seed: noise becoming programs.
    /// </summary>
    public static class Presets
    {
        public const uint Swarm = 1, Harvest = 2, Fuse = 3, Raid = 4, Ward = 5, Hive = 6, Pulse = 7, Ant = 8, Lattice = 9;
        public const uint Overmind = 100, Turtle = 101, Locust = 102, Assembler = 103;

        public static readonly uint[] DefaultLoadout = { Swarm, Harvest, Fuse, Raid, Ward, Hive };
        public static readonly uint[] UnitIds = { Swarm, Harvest, Fuse, Raid, Ward, Hive, Pulse, Ant, Lattice };
        public static readonly uint[] MetaIds = { Overmind, Turtle, Locust, Assembler };

        static readonly Dictionary<uint, string> sources = new Dictionary<uint, string>
        {
            [Swarm] =
@"@name SWARM
# the default fighter: flock, follow orders, pick fights you can win
always: flock
when order: seek order 2
when enemy and not outnumbered: seek enemy 2
when hurt and outnumbered: avoid enemy 3",

            [Harvest] =
@"@name HARVEST
# fill up, go home, stay out of trouble
when not full: mine 3
when full: seek home 4
when enemy and outnumbered: avoid enemy 3
always: spread",

            [Fuse] =
@"@name FUSE
# 2 + 2 = 4. find your twin, become one
when pair: merge 2
always: flock
when order: seek order 2
when contact: hold",

            [Raid] =
@"@name RAID
# ignore the army, hit the core
always: seek core 3
when enemy and outnumbered: avoid enemy 2
when lonely: seek allies 2
always: spread",

            [Ward] =
@"@name WARD
# hold the blanket. checkerboard spacing laughs at splash
when not home: seek home 3
when enemy: seek enemy 3
always: form checker 2",

            [Hive] =
@"@name HIVE
# rewrite what you touch. infected kills join you
when contact: infect
when enemy: seek enemy 2
always: flock
when order: seek order 2",

            [Pulse] =
@"@name PULSE
# a two-phase oscillator: strike on the high clock, scatter on the low
when clock: seek enemy 3
when not clock: avoid enemy 2
always: flock
when order: seek order 1",

            [Ant] =
@"@name ANT
# stigmergy: lay trail when loaded, keep to the trail when empty
when carrying: mark
when carrying: seek home 4
when not carrying: mine 2
when not carrying and trail: seek trail 2
always: wander",

            [Lattice] =
@"@name LATTICE
# a crystal. sits in a checkerboard, advances as one
always: form checker 3
always: spread
when order: seek order 2
when contact: hold",

            [Overmind] =
@"@meta
@name OVERMIND
# the default opponent. rules about rules
always: produce harvest
when workers: produce swarm
when workers and bigenemy: produce hive
when workers and late: produce fuse
when rich: spawn 4
always: beacon home
always: army attack
when ahead: beacon enemy
when outnumbered and not threat: army retreat
when hurt: beacon home",

            [Turtle] =
@"@meta
@name TURTLE
# hold the blanket, grow a singularity
always: produce harvest
when workers: produce ward
when workers and swarm: produce fuse
when rich: spawn 4
always: beacon home
always: army attack
when bigarmy and ahead: beacon enemy
when ascending: army merge",

            [Locust] =
@"@meta
@name LOCUST
# all in. a wall of twos at minute two
always: produce swarm
when not workers: produce harvest
when army: produce raid
when rich: spawn 9
always: beacon front
when army: beacon enemy
always: army attack",

            [Assembler] =
@"@meta
@name ASSEMBLER
# assembly index 10 or bust
always: produce harvest
when workers: produce fuse
when rich: spawn 9
always: beacon home
always: army merge
when threat: army attack
when danger: beacon enemy",
        };

        static readonly Dictionary<uint, Program> compiled = new Dictionary<uint, Program>();
        // keyed by the full (seed, id) pair: a hashed single key once let two matches share a feral program
        static readonly Dictionary<(ulong, uint), Program> ferals = new Dictionary<(ulong, uint), Program>();

        static Presets()
        {
            foreach (var kv in sources)
            {
                var r = Argot.Compile(kv.Value);
                if (!r.Ok) throw new Exception("preset " + kv.Key + " does not compile: " + string.Join("; ", r.Errors));
                r.Program.Id = kv.Key;
                compiled[kv.Key] = r.Program;
            }
        }

        public static IEnumerable<Program> All => compiled.Values;

        public static uint IdOf(string name)
        {
            foreach (var kv in compiled) if (string.Equals(kv.Value.Name, name, StringComparison.OrdinalIgnoreCase)) return kv.Key;
            return 0;
        }

        public static Program Get(uint id, ulong seed)
        {
            Program p;
            if (compiled.TryGetValue(id, out p)) return p;
            if (id >= Rules.FeralProgramBase && id < Rules.FeralProgramBase + Rules.FeralPrograms)
            {
                var key = (seed, id);
                lock (ferals)
                {
                    if (ferals.TryGetValue(key, out p)) return p;
                    p = Feral(seed, (int)(id - Rules.FeralProgramBase));
                    if (ferals.Count > 256) ferals.Clear();
                    ferals[key] = p;
                    return p;
                }
            }
            return null;
        }

        static readonly string[] feralSensors = { "enemy", "contact", "allies", "hurt", "lonely", "clock", "crowded", "outnumbered", "big", "tiny", "pair" };
        static readonly string[] feralActions =
        {
            "seek enemy", "seek enemy", "avoid enemy", "flock", "flock", "spread", "wander", "merge", "hold",
            "seek core", "seek allies", "infect", "form diag", "form rows",
        };

        /// <summary>A random but valid program: what the dead internet dreams up.</summary>
        public static Program Feral(ulong seed, int k)
        {
            var rng = new Rng(seed ^ 0xFE4A1ul * (ulong)(k + 1));
            var lines = new List<string> { "@name NOISE-" + ((rng.Next() & 0xFFFF).ToString("X4")) };
            int rules = rng.Range(2, 6);
            for (int r = 0; r < rules; r++)
            {
                int lits = rng.Range(0, 3);
                var used = new List<string>();
                var cond = new List<string>();
                for (int l = 0; l < lits; l++)
                {
                    var s = feralSensors[rng.Range(0, feralSensors.Length)];
                    if (used.Contains(s)) continue;
                    used.Add(s);
                    cond.Add((rng.Range(0, 3) == 0 ? "not " : "") + s);
                }
                string act = feralActions[rng.Range(0, feralActions.Length)];
                int gain = rng.Range(1, 4);
                lines.Add((cond.Count == 0 ? "always" : "when " + string.Join(" and ", cond)) + ": " + act + (gain > 1 && act != "hold" && act != "infect" ? " " + gain : ""));
            }
            lines.Add("when enemy: seek enemy 1");
            var res = Argot.Compile(string.Join("\n", lines));
            var p = res.Program;
            p.Id = Rules.FeralProgramBase + (uint)k;
            if (!res.Ok) // never expected; fall back to something alive
            {
                var fb = Argot.Compile("@name NOISE\nalways: wander 2\nwhen enemy: seek enemy 2").Program;
                fb.Id = p.Id;
                return fb;
            }
            return p;
        }
    }
}
