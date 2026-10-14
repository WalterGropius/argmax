#nullable disable
using System;
using System.Collections.Generic;
using System.Text;

namespace Argmax.Sim
{
    public enum Op : byte
    {
        Nop = 0,
        // unit ops (programs)
        Seek = 1, Avoid = 2, Flock = 3, Spread = 4, Form = 5, Merge = 6, Split = 7, Mine = 8,
        Hold = 9, Wander = 10, Infect = 11, Mark = 12, Holdfire = 13,
        // seat ops (metaprograms)
        Produce = 20, Spawn = 21, Beacon = 22, Army = 23,
    }

    /// <summary>
    /// ARGOT: the language motes run. Each line is a rule: a conjunction of sensor literals and one action.
    ///
    ///     when not full: mine 3
    ///     when full: seek home 4
    ///     when enemy and outnumbered: avoid enemy 3
    ///     always: spread
    ///
    /// A rule compiles to one 64-bit chip: a minterm (mask/want over 20 sensor bits) plus an op, an argument
    /// and a gain. A program is a sum of products. Every matching rule adds its weights (they compose
    /// linearly); flags OR together. Programs ("@unit", default) steer motes. Metaprograms ("@meta") steer a
    /// whole seat: what to build, where to rally, when to strike. Lilly: programs, metaprograms, and you,
    /// the self-metaprogrammer.
    /// </summary>
    public static class Argot
    {
        public static readonly string[] UnitSensors =
        {
            "enemy", "contact", "allies", "crowded", "hurt", "carrying", "full", "order", "scrap", "home",
            "pair", "beacon", "outnumbered", "big", "siege", "clock", "trail", "lonely", "wild", "tiny",
        };

        public static readonly string[] UnitSensorHelp =
        {
            "an enemy within 4 tiles", "an enemy within attack range", "3+ allies in my 5x5", "5+ allies touching me",
            "below half health", "carrying scrap", "cargo at capacity", "I have an order target", "scrap within 4",
            "within 3 of my core", "an equal-value ally touches me", "my seat has a beacon", "more enemies than allies near me",
            "value 16 or more", "an enemy core within 4", "true on alternate 1.6 s phases", "standing on my trail",
            "no allies in my 5x5", "a feral (Noise) near me", "value 2",
        };

        public static readonly string[] MetaSensors =
        {
            "rich", "poor", "outnumbered", "ahead", "threat", "workers", "bigenemy", "bigarmy", "clock", "early",
            "late", "ascending", "danger", "hurt", "wild", "swarm", "sieging", "army", "lowcore", "beacon",
        };

        public static readonly string[] MetaSensorHelp =
        {
            "scrap >= 40", "scrap < 8", "enemy army outvalues mine by 25%", "my total value is 1.5x theirs",
            "3+ enemies within 10 of my core", "6+ workers", "enemy has a 32+", "I have a 32+", "slow clock (6.4 s)",
            "first 2 minutes", "after 10 minutes", "I have a 1024+", "enemy has a 1024+", "my core below half",
            "2+ ferals near my core", "60+ motes", "5+ of mine near their core", "army value >= 40",
            "their core below half", "my beacon is placed",
        };

        static readonly Dictionary<string, Op> unitOps = new Dictionary<string, Op>
        {
            { "seek", Op.Seek }, { "avoid", Op.Avoid }, { "flock", Op.Flock }, { "spread", Op.Spread },
            { "form", Op.Form }, { "merge", Op.Merge }, { "split", Op.Split }, { "mine", Op.Mine },
            { "hold", Op.Hold }, { "wander", Op.Wander }, { "infect", Op.Infect }, { "mark", Op.Mark },
            { "holdfire", Op.Holdfire },
        };

        static readonly Dictionary<string, Op> metaOps = new Dictionary<string, Op>
        {
            { "produce", Op.Produce }, { "spawn", Op.Spawn }, { "beacon", Op.Beacon }, { "army", Op.Army },
        };

        public static readonly string[] Fields = { "order", "enemy", "scrap", "home", "beacon", "core", "allies", "trail" };
        static readonly Channel[] FieldChannels =
        {
            Channel.Order, Channel.Enemy, Channel.Scrap, Channel.Home, Channel.Beacon, Channel.Core, Channel.Cohesion, Channel.Trail,
        };
        public static readonly string[] Patterns = { "", "checker", "rows", "cols", "grid", "diag" };
        public static readonly string[] Places = { "", "home", "front", "enemy", "scrap", "wild", "clear" };
        public static readonly string[] ArmyOrders = { "", "attack", "retreat", "merge", "split", "hold", "free" };
        public const int MaxMetaRules = 12;

        /// <summary>Optional: explains where a locked word is learned (the campaign sets this).</summary>
        public static Func<string, string> LockedHint;

        public sealed class Result
        {
            public Program Program;
            public readonly List<string> Errors = new List<string>();
            public bool Ok => Errors.Count == 0;
        }

        // ---------------------------------------------------------------- chips

        public static ulong Pack(uint mask, uint want, Op op, byte arg, sbyte gain) =>
            (mask & 0xFFFFFu) | ((ulong)(want & 0xFFFFFu) << 20) | ((ulong)op << 40) | ((ulong)arg << 48) | ((ulong)(byte)gain << 56);

        public static void Unpack(ulong c, out uint mask, out uint want, out Op op, out byte arg, out sbyte gain)
        {
            mask = (uint)(c & 0xFFFFF);
            want = (uint)((c >> 20) & 0xFFFFF);
            op = (Op)((c >> 40) & 0xFF);
            arg = (byte)((c >> 48) & 0xFF);
            gain = (sbyte)(byte)(c >> 56);
        }

        public static bool Matches(ulong chip, uint sensors)
        {
            uint mask = (uint)(chip & 0xFFFFF), want = (uint)((chip >> 20) & 0xFFFFF);
            return (sensors & mask) == want;
        }

        public static Channel FieldChannel(byte arg) => arg < FieldChannels.Length ? FieldChannels[arg] : Channel.Order;

        // ---------------------------------------------------------------- compiler

        /// <summary>
        /// Compile Argot. With <paramref name="vocab"/>, only those words are accepted: the campaign hands out the
        /// language one word at a time, the way Turing Complete hands out components.
        /// </summary>
        public static Result Compile(string source, string fallbackName = "UNTITLED", ICollection<string> vocab = null)
        {
            var res = new Result();
            bool Locked(string word) => vocab != null && !vocab.Contains(word);
            string LockMsg(string where, string word) => where + "'" + word + "' is locked" + (LockedHint != null ? LockedHint(word) : "");
            var chips = new List<ulong>();
            bool meta = false;
            string name = null;
            int symbols = 0;
            bool worker = false;
            var lines = (source ?? "").Replace("\r", "").Split('\n');
            for (int ln = 0; ln < lines.Length; ln++)
            {
                string raw = lines[ln];
                int hash = raw.IndexOf('#');
                if (hash >= 0) raw = raw.Substring(0, hash);
                raw = raw.Trim();
                if (raw.Length == 0) continue;
                string where = "line " + (ln + 1) + ": ";
                if (raw.StartsWith("@"))
                {
                    var parts = raw.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    var d = parts[0].ToLowerInvariant();
                    if (d == "@meta") { if (Locked("@meta")) { res.Errors.Add(LockMsg(where, "@meta")); continue; } if (chips.Count > 0) res.Errors.Add(where + "@meta must come before any rule"); meta = true; }
                    else if (d == "@unit") meta = false;
                    else if (d == "@name") name = parts.Length > 1 ? Sanitize(parts[1]) : null;
                    else res.Errors.Add(where + "unknown directive " + parts[0]);
                    continue;
                }

                string cond = "", act = raw;
                int colon = raw.IndexOf(':');
                if (colon >= 0) { cond = raw.Substring(0, colon).Trim(); act = raw.Substring(colon + 1).Trim(); }
                var sensors = meta ? MetaSensors : UnitSensors;
                uint mask = 0, want = 0;
                int literals = 0;
                var ctoks = Tokens(cond);
                int ci = 0;
                if (ci < ctoks.Count && (ctoks[ci] == "when" || ctoks[ci] == "if")) { if (Locked("when")) { res.Errors.Add(LockMsg(where, "when")); continue; } ci++; }
                else if (ci < ctoks.Count && ctoks[ci] == "always") { if (Locked("always")) { res.Errors.Add(LockMsg(where, "always")); continue; } ci++; if (ci < ctoks.Count) res.Errors.Add(where + "'always' takes no conditions"); }
                else if (ctoks.Count > 0) { res.Errors.Add(where + "a rule starts with 'when', 'if' or 'always'"); continue; }
                bool expectLit = ctoks.Count > ci;
                while (ci < ctoks.Count)
                {
                    bool neg = false;
                    string t = ctoks[ci++];
                    if (t == "not" || t == "!") { neg = true; if (ci >= ctoks.Count) { res.Errors.Add(where + "'not' needs a sensor"); break; } t = ctoks[ci++]; }
                    else if (t.StartsWith("!")) { neg = true; t = t.Substring(1); }
                    if (neg && Locked("not")) { res.Errors.Add(LockMsg(where, "not")); break; }
                    int bit = Array.IndexOf(sensors, t);
                    if (bit < 0) { res.Errors.Add(where + "unknown sensor '" + t + "'" + Suggest(t, sensors)); break; }
                    if (Locked(t)) { res.Errors.Add(LockMsg(where, t)); break; }
                    if ((mask & (1u << bit)) != 0) { res.Errors.Add(where + "sensor '" + t + "' used twice"); break; }
                    mask |= 1u << bit;
                    if (!neg) want |= 1u << bit;
                    literals++;
                    if (ci < ctoks.Count)
                    {
                        var j = ctoks[ci];
                        if (j == "and" || j == "&&" || j == "&" || j == ",") { if (Locked("and")) { res.Errors.Add(LockMsg(where, "and")); break; } ci++; }
                        else { res.Errors.Add(where + "join sensors with 'and' (got '" + j + "')"); break; }
                        if (ci >= ctoks.Count) res.Errors.Add(where + "dangling 'and'");
                    }
                }
                if (expectLit && literals == 0 && res.Errors.Count == 0) res.Errors.Add(where + "missing condition");
                if (literals > Rules.MaxLiterals) res.Errors.Add(where + "at most " + Rules.MaxLiterals + " sensors per rule");

                var atoks = Tokens(act);
                if (atoks.Count == 0) { res.Errors.Add(where + "missing action after ':'"); continue; }
                Op op;
                var table = meta ? metaOps : unitOps;
                if (!table.TryGetValue(atoks[0], out op))
                {
                    var other = meta ? unitOps : metaOps;
                    if (other.ContainsKey(atoks[0])) res.Errors.Add(where + "'" + atoks[0] + "' is a " + (meta ? "unit" : "@meta") + " action");
                    else res.Errors.Add(where + "unknown action '" + atoks[0] + "'" + Suggest(atoks[0], new List<string>(table.Keys).ToArray()));
                    continue;
                }
                if (Locked(atoks[0])) { res.Errors.Add(LockMsg(where, atoks[0])); continue; }
                bool argWord = op == Op.Seek || op == Op.Avoid || op == Op.Form || op == Op.Beacon || op == Op.Army;
                if (argWord && atoks.Count > 1 && Locked(atoks[1])) { res.Errors.Add(LockMsg(where, atoks[1])); continue; }
                byte arg = 0; int gain = 1; int ai = 1; int argSyms = 0;
                switch (op)
                {
                    case Op.Seek:
                    case Op.Avoid:
                        if (ai >= atoks.Count) { res.Errors.Add(where + atoks[0] + " needs a field: " + string.Join(", ", Fields)); continue; }
                        int f = Array.IndexOf(Fields, atoks[ai]);
                        if (f < 0) { res.Errors.Add(where + "unknown field '" + atoks[ai] + "'" + Suggest(atoks[ai], Fields)); continue; }
                        arg = (byte)f; ai++; argSyms = 1;
                        break;
                    case Op.Form:
                        if (ai >= atoks.Count) { res.Errors.Add(where + "form needs a pattern: checker, rows, cols, grid, diag"); continue; }
                        int pt = Array.IndexOf(Patterns, atoks[ai]);
                        if (pt <= 0) { res.Errors.Add(where + "unknown pattern '" + atoks[ai] + "'"); continue; }
                        arg = (byte)pt; ai++; argSyms = 1;
                        break;
                    case Op.Produce:
                        if (ai >= atoks.Count) { res.Errors.Add(where + "produce needs a slot 1-6 or a preset name"); continue; }
                        int slot;
                        if (int.TryParse(atoks[ai], out slot))
                        {
                            if (slot < 1 || slot > Rules.LoadoutSlots) { res.Errors.Add(where + "slot must be 1-6"); continue; }
                            arg = (byte)slot;
                        }
                        else
                        {
                            uint pid = Presets.IdOf(atoks[ai]);
                            if (pid == 0 || pid >= 200) { res.Errors.Add(where + "no preset program named '" + atoks[ai] + "'"); continue; }
                            arg = (byte)(16 + pid);
                        }
                        ai++; argSyms = 1;
                        break;
                    case Op.Spawn:
                        int n = 1;
                        if (ai < atoks.Count) { if (!int.TryParse(atoks[ai], out n) || n < 1 || n > 9) { res.Errors.Add(where + "spawn count must be 1-9"); continue; } ai++; argSyms = 1; }
                        arg = (byte)(n == 1 ? 1 : n <= 4 ? 2 : 3);
                        break;
                    case Op.Beacon:
                        if (ai >= atoks.Count) { res.Errors.Add(where + "beacon needs a place: home, front, enemy, scrap, wild, clear"); continue; }
                        int pl = Array.IndexOf(Places, atoks[ai]);
                        if (pl <= 0) { res.Errors.Add(where + "unknown place '" + atoks[ai] + "'"); continue; }
                        arg = (byte)pl; ai++; argSyms = 1;
                        break;
                    case Op.Army:
                        if (ai >= atoks.Count) { res.Errors.Add(where + "army needs an order: attack, retreat, merge, split, hold, free"); continue; }
                        int ao = Array.IndexOf(ArmyOrders, atoks[ai]);
                        if (ao <= 0) { res.Errors.Add(where + "unknown army order '" + atoks[ai] + "'"); continue; }
                        arg = (byte)ao; ai++; argSyms = 1;
                        break;
                }
                if (ai < atoks.Count)
                {
                    if (op >= Op.Produce || op == Op.Split || op == Op.Hold || op == Op.Infect || op == Op.Mark || op == Op.Holdfire)
                    { res.Errors.Add(where + "'" + atoks[0] + "' takes no strength"); continue; }
                    if (!int.TryParse(atoks[ai], out gain) || gain < 1 || gain > 9) { res.Errors.Add(where + "strength must be 1-9"); continue; }
                    ai++;
                    if (gain != 1) argSyms++;
                }
                if (ai < atoks.Count) { res.Errors.Add(where + "unexpected '" + atoks[ai] + "'"); continue; }
                if (op == Op.Mine) worker = true;
                chips.Add(Pack(mask, want, op, arg, (sbyte)gain));
                symbols += 1 + literals + argSyms;
            }
            int max = meta ? MaxMetaRules : Rules.MaxRules;
            if (chips.Count > max) res.Errors.Add("too many rules: " + chips.Count + " (max " + max + ")");
            if (chips.Count == 0 && res.Errors.Count == 0) res.Errors.Add("empty program");
            res.Program = new Program
            {
                Name = name ?? Sanitize(fallbackName),
                Source = source ?? "",
                Kind = meta ? ProgramKind.Meta : ProgramKind.Unit,
                Chips = chips.ToArray(),
                RuleCount = chips.Count,
                Symbols = symbols,
                Worker = worker,
            };
            return res;
        }

        /// <summary>Canonical source for a compiled program (used for presets, ferals and tooltips).</summary>
        public static string Decompile(Program p)
        {
            var sb = new StringBuilder();
            bool meta = p.Kind == ProgramKind.Meta;
            if (meta) sb.Append("@meta\n");
            sb.Append("@name ").Append(p.Name).Append('\n');
            foreach (var c in p.Chips) sb.Append(Rule(c, meta)).Append('\n');
            return sb.ToString();
        }

        public static string Rule(ulong chip, bool meta)
        {
            Unpack(chip, out uint mask, out uint want, out Op op, out byte arg, out sbyte gain);
            var sensors = meta ? MetaSensors : UnitSensors;
            var sb = new StringBuilder();
            if (mask == 0) sb.Append("always");
            else
            {
                sb.Append("when ");
                bool first = true;
                for (int b = 0; b < sensors.Length; b++)
                {
                    if ((mask & (1u << b)) == 0) continue;
                    if (!first) sb.Append(" and ");
                    if ((want & (1u << b)) == 0) sb.Append("not ");
                    sb.Append(sensors[b]);
                    first = false;
                }
            }
            sb.Append(": ").Append(op.ToString().ToLowerInvariant());
            switch (op)
            {
                case Op.Seek: case Op.Avoid: sb.Append(' ').Append(Fields[Math.Min(arg, (byte)(Fields.Length - 1))]); break;
                case Op.Form: sb.Append(' ').Append(Patterns[Math.Min(arg, (byte)(Patterns.Length - 1))]); break;
                case Op.Produce:
                    if (arg >= 16) { var pr = Presets.Get((uint)(arg - 16), 0); sb.Append(' ').Append(pr != null ? pr.Name.ToLowerInvariant() : "?"); }
                    else sb.Append(' ').Append(arg);
                    break;
                case Op.Spawn: sb.Append(' ').Append(arg == 1 ? 1 : arg == 2 ? 4 : 9); break;
                case Op.Beacon: sb.Append(' ').Append(Places[Math.Min(arg, (byte)(Places.Length - 1))]); break;
                case Op.Army: sb.Append(' ').Append(ArmyOrders[Math.Min(arg, (byte)(ArmyOrders.Length - 1))]); break;
            }
            if (gain != 1 && op < Op.Produce) sb.Append(' ').Append(gain);
            return sb.ToString();
        }

        static List<string> Tokens(string s)
        {
            var list = new List<string>();
            foreach (var t in s.ToLowerInvariant().Replace(",", " , ").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)) list.Add(t);
            return list;
        }

        static string Sanitize(string n)
        {
            var sb = new StringBuilder();
            foreach (var ch in n.Trim())
            {
                if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') sb.Append(char.ToUpperInvariant(ch));
                if (sb.Length >= 24) break;
            }
            return sb.Length == 0 ? "UNTITLED" : sb.ToString();
        }

        static string Suggest(string t, string[] options)
        {
            string best = null; int bestD = 3;
            foreach (var o in options)
            {
                if (string.IsNullOrEmpty(o)) continue;
                int d = Distance(t, o);
                if (d < bestD) { bestD = d; best = o; }
            }
            return best != null ? " (did you mean '" + best + "'?)" : "";
        }

        static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }
    }
}
