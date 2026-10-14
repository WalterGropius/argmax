#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Argmax.Sim
{
    /// <summary>
    /// Bridge to TypeSafe's Jev, a "System 1" model that answers typed questions with probabilities.
    /// It becomes a fourth commander next to Argot metaprograms, the ML-Agents policy and humans: each decision
    /// cycle it reads a terse situation report and answers four choice questions, one per MetaDecision branch.
    /// No HTTP here (the sim stays dependency-free); callers POST <see cref="Request"/> and feed the reply to
    /// <see cref="TryDecode"/>.
    /// </summary>
    public static class JevBrief
    {
        public const string Endpoint = "https://api.typesafe.ai/v1/systemone";
        public const string Model = "jev-latest";
        public const int DecideEvery = 30; // ticks: one decision per 3 s keeps cost and latency in budget

        static readonly string[] ArmyOptions = { "attack", "retreat", "merge", "split", "hold", "free" };
        static readonly string[] ArmyHelp =
        {
            "advance on the rally beacon or the enemy core", "pull back home under core cover",
            "fuse equal motes into bigger, armored ones", "break big motes into many small fast ones",
            "stand still and fire", "let unit programs run without orders",
        };
        static readonly string[] BeaconOptions = { "home", "front", "enemy", "scrap", "wild" };
        static readonly string[] BeaconHelp =
        {
            "in front of my core", "where my army currently is", "at the enemy core", "at the best scrap node", "at the nearest feral well",
        };

        public sealed class Reading
        {
            public string Model;
            public string Error;
            public Dictionary<string, Dictionary<string, double>> Choices = new Dictionary<string, Dictionary<string, double>>();
            public double Spend;
            public int InputTokens, OutputTokens;

            public string Summary()
            {
                if (Error != null) return "JEV error: " + Error;
                var sb = new StringBuilder();
                foreach (var q in Choices)
                {
                    string best = null; double bp = -1;
                    foreach (var kv in q.Value) if (kv.Value > bp) { bp = kv.Value; best = kv.Key; }
                    sb.Append(q.Key).Append(' ').Append(best).Append(' ').Append(bp.ToString("0.00", CultureInfo.InvariantCulture)).Append("  ");
                }
                sb.Append("spend ").Append(Spend.ToString("0.00", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>Plain-language state of the match from one seat's point of view.</summary>
        public static string State(World w, int seat)
        {
            if (w.S == null) Sim.Refresh(w);
            var T = w.S.Tally;
            var me = w.Seats[seat];
            int foe = w.Other(seat);
            var en = w.Seats[foe];
            ref var t = ref T[seat];
            ref var e = ref T[foe];
            var sb = new StringBuilder();
            int secs = w.Tick / Rules.TickHz;
            sb.Append("ARGMAX swarm RTS. Two AI swarms fight over scrap; destroy the enemy core or hold a 2048 mote for 15 s to win. ");
            sb.Append("Time ").Append(secs / 60).Append(':').Append((secs % 60).ToString("00")).Append(". ");
            sb.Append("ME: core ").Append(me.CoreHp).Append('/').Append(Rules.CoreHp).Append(", scrap ").Append(me.Scrap)
              .Append(", ").Append(t.Count).Append(" motes (total value ").Append(t.Value).Append("), workers ").Append(t.Workers)
              .Append(", army value ").Append(t.ArmyValue).Append(", biggest mote ").Append(t.MaxTier > 0 ? Rules.Value(t.MaxTier) : 0)
              .Append(", producing ").Append(ProgramName(w, me.Loadout[Math.Min(me.Production, (byte)5)])).Append(". ");
            if (t.EnemiesNearCore > 0) sb.Append(t.EnemiesNearCore).Append(" hostile motes within 10 tiles of my core. ");
            sb.Append("ENEMY: core ").Append(en.CoreHp).Append('/').Append(Rules.CoreHp).Append(", ").Append(e.Count)
              .Append(" motes (value ").Append(e.Value).Append("), army value ").Append(e.ArmyValue)
              .Append(", biggest mote ").Append(e.MaxTier > 0 ? Rules.Value(e.MaxTier) : 0).Append(". ");
            if (T[foe].SiegeMotes > 0) sb.Append("They have ").Append(T[foe].SiegeMotes).Append(" motes near my core. ");
            if (t.SiegeMotes > 0) sb.Append("I have ").Append(t.SiegeMotes).Append(" motes near their core. ");
            ref var nz = ref T[Rules.NoiseSeat];
            if (nz.Count > 0) sb.Append("FERALS (hostile to all): ").Append(nz.Count).Append(". ");
            if (me.SingularityTicks > 0) sb.Append("My singularity is forming. ");
            if (en.SingularityTicks > 0) sb.Append("THE ENEMY SINGULARITY IS FORMING. ");
            sb.Append("My beacon: ").Append(me.BeaconOn ? "placed" : "none").Append('.');
            return sb.ToString();
        }

        static string ProgramName(World w, uint id)
        {
            var p = w.GetProgram(id);
            return p != null ? p.Name.ToLowerInvariant() : "nothing";
        }

        /// <summary>The full JSON request body.</summary>
        public static string Request(World w, int seat)
        {
            var me = w.Seats[seat];
            var sb = new StringBuilder(2048);
            sb.Append("{\"model\":").Append(Q(Model)).Append(",\"state\":").Append(Q(State(w, seat))).Append(",\"questions\":{");
            sb.Append("\"army\":{\"type\":\"choice\",\"instructions\":\"What should my army do right now?\",\"criteria\":{");
            for (int i = 0; i < ArmyOptions.Length; i++) sb.Append(i > 0 ? "," : "").Append(Q(ArmyOptions[i])).Append(':').Append(Q(ArmyHelp[i]));
            sb.Append("}},\"produce\":{\"type\":\"choice\",\"instructions\":\"Which program should my core produce next?\",\"criteria\":{");
            bool first = true;
            var seen = new HashSet<string>();
            for (int s = 0; s < Rules.LoadoutSlots; s++)
            {
                var p = w.GetProgram(me.Loadout[s]);
                if (p == null) continue;
                string key = p.Name.ToLowerInvariant();
                if (!seen.Add(key)) continue;
                sb.Append(first ? "" : ",").Append(Q(key)).Append(':').Append(Q(Describe(p)));
                first = false;
            }
            sb.Append("}},\"beacon\":{\"type\":\"choice\",\"instructions\":\"Where should the rally beacon be?\",\"criteria\":{");
            for (int i = 0; i < BeaconOptions.Length; i++) sb.Append(i > 0 ? "," : "").Append(Q(BeaconOptions[i])).Append(':').Append(Q(BeaconHelp[i]));
            sb.Append("}},\"spend\":{\"type\":\"noul\",\"instructions\":\"Should I spend my scrap on more units immediately?\"}}}");
            return sb.ToString();
        }

        /// <summary>First comment line of a program, else a generic description.</summary>
        static string Describe(Program p)
        {
            foreach (var line in (p.Source ?? "").Split('\n'))
            {
                var l = line.Trim();
                if (l.StartsWith("#")) return l.TrimStart('#', ' ');
            }
            return p.Worker ? "scrap miners" : "fighters";
        }

        public static bool TryDecode(string json, World w, int seat, out MetaDecision d, out Reading reading)
        {
            d = new MetaDecision();
            reading = new Reading();
            object root;
            try { root = MiniJson.Parse(json); }
            catch (Exception ex) { reading.Error = "bad json: " + ex.Message; return false; }
            var obj = root as Dictionary<string, object>;
            if (obj == null) { reading.Error = "unexpected reply"; return false; }
            if (obj.TryGetValue("model", out var model)) reading.Model = model as string;
            if (!(obj.TryGetValue("answers", out var answersObj) && answersObj is Dictionary<string, object> answers))
            {
                reading.Error = obj.TryGetValue("detail", out var det) ? Convert.ToString(det, CultureInfo.InvariantCulture) : "no answers";
                return false;
            }
            if (obj.TryGetValue("usage", out var u) && u is Dictionary<string, object> usage)
            {
                if (usage.TryGetValue("input_tokens", out var it)) reading.InputTokens = (int)ToDouble(it);
                if (usage.TryGetValue("output_tokens", out var ot)) reading.OutputTokens = (int)ToDouble(ot);
            }
            foreach (var kv in answers)
            {
                var a = kv.Value as Dictionary<string, object>;
                if (a == null) continue;
                if (a.TryGetValue("noul", out var noul)) { reading.Spend = ToDouble(noul); continue; }
                if (a.TryGetValue("probabilities", out var pr) && pr is Dictionary<string, object> probs)
                {
                    var map = new Dictionary<string, double>();
                    foreach (var p in probs) map[p.Key] = ToDouble(p.Value);
                    reading.Choices[kv.Key] = map;
                }
            }
            var me = w.Seats[seat];
            string army = Best(reading, "army"), produce = Best(reading, "produce"), beacon = Best(reading, "beacon");
            int ai = Array.IndexOf(ArmyOptions, army);
            if (ai >= 0) d.Army = ai + 1;
            int bi = Array.IndexOf(BeaconOptions, beacon);
            if (bi >= 0) d.Beacon = bi + 1;
            if (produce != null)
                for (int s = 0; s < Rules.LoadoutSlots; s++)
                {
                    var p = w.GetProgram(me.Loadout[s]);
                    if (p != null && p.Name.ToLowerInvariant() == produce) { d.Produce = s + 1; break; }
                }
            d.Spawn = reading.Spend > 0.8 ? 3 : reading.Spend > 0.5 ? 2 : 0;
            return true;
        }

        static string Best(Reading r, string q)
        {
            if (!r.Choices.TryGetValue(q, out var m)) return null;
            string best = null; double bp = -1;
            foreach (var kv in m) if (kv.Value > bp) { bp = kv.Value; best = kv.Key; }
            return best;
        }

        static double ToDouble(object o) => o is double d ? d : o is long l ? l : 0.0;

        static string Q(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }

    /// <summary>Just enough JSON to read API replies without pulling a library into the shared sim.</summary>
    public static class MiniJson
    {
        public static object Parse(string s)
        {
            int i = 0;
            var v = Value(s, ref i);
            Ws(s, ref i);
            return v;
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++; Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    string k = Str(s, ref i);
                    Ws(s, ref i);
                    if (s[i++] != ':') throw new FormatException("expected ':'");
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("expected ',' or '}'");
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("expected ',' or ']'");
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < s.Length && ("+-.eE".IndexOf(s[i]) >= 0 || char.IsDigit(s[i]))) i++;
            var num = s.Substring(start, i - start);
            if (num.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lv)) return lv;
            return double.Parse(num, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("expected string");
            i++;
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            i++;
            return sb.ToString();
        }
    }
}
