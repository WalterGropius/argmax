#nullable disable
using System;
using System.Collections.Generic;
using System.Text;

namespace Argmax.Sim
{
    /// <summary>
    /// A genetic algorithm over Argot programs. The genome is the gain of each rule in a template (0 = rule
    /// off); fitness is a weighted mix of goal progress, survival and speed, measured by actually running the
    /// level on the deterministic simulation. Mutate, evaluate, select, repeat: no gradients, no understanding,
    /// and the result is a program you can read. Call <see cref="Step"/> a few times per frame.
    /// </summary>
    public sealed class Evolver
    {
        public sealed class Genome
        {
            public int[] Gains;
            public double Fitness = double.NaN;
            public ChamberResult Result;
            public string Source;
        }

        public readonly ChamberSpec Spec;
        public readonly string[] Template;
        public int[] Weights;
        public int Population = 16, Elite = 3, MaxGain = 6;
        public int Generation { get; private set; }
        public int Evaluations { get; private set; }
        public Genome Best { get; private set; }
        public readonly List<double> BestHistory = new List<double>();
        public readonly List<double> MeanHistory = new List<double>();
        List<Genome> pop = new List<Genome>();
        int next;
        Rng rng;

        public Evolver(ChamberSpec spec, string[] template, int[] weights, ulong seed = 1)
        {
            Spec = spec;
            Template = template;
            Weights = (int[])weights.Clone();
            rng = new Rng(seed * 0x9E37UL + (ulong)spec.Id);
            for (int i = 0; i < Population; i++)
            {
                var g = new int[template.Length];
                for (int k = 0; k < g.Length; k++) g[k] = Toggle(k) ? rng.Range(0, 2) : rng.Range(0, MaxGain + 1);
                if (i == 0) for (int k = 0; k < g.Length; k++) g[k] = 1;
                pop.Add(new Genome { Gains = g });
            }
        }

        bool Toggle(int k) => !Template[k].Contains("{g}");

        public static string Source(string[] template, int[] gains)
        {
            var sb = new StringBuilder("@name EVOLVED\n");
            int rules = 0;
            for (int k = 0; k < template.Length; k++)
            {
                if (gains[k] <= 0) continue;
                sb.Append(template[k].Replace("{g}", gains[k].ToString())).Append('\n');
                rules++;
            }
            if (rules == 0) sb.Append("always: hold\n");
            return sb.ToString();
        }

        public double Score(ChamberResult r)
        {
            if (r.Error != null) return 0;
            double progress = r.Target > 0 ? Math.Min(1.0, Math.Max(0, r.Progress) / (double)r.Target) : 0;
            if (Spec.Goal == ChamberGoal.Exact) progress = r.Success ? 1 : 0.5 * Math.Min(1.0, r.Progress / (double)Math.Max(1, r.Target));
            double survival = r.StartValue > 0 ? Math.Max(0, 1.0 - r.Lost / (double)r.StartValue) : 1;
            double speed = r.Success ? Math.Max(0, 1.0 - r.Ticks / (double)Math.Max(1, Spec.TimeLimit)) : 0;
            int sum = Weights[0] + Weights[1] + Weights[2];
            if (sum <= 0) return 0;
            return (Weights[0] * progress + Weights[1] * survival + Weights[2] * speed) / sum - r.Symbols * 0.0005;
        }

        /// <summary>Evaluate one genome. Returns true when that finished a generation.</summary>
        public bool Step()
        {
            var g = pop[next];
            if (double.IsNaN(g.Fitness))
            {
                g.Source = Source(Template, g.Gains);
                g.Result = Chambers.Run(Spec, g.Source);
                g.Fitness = Score(g.Result);
                Evaluations++;
            }
            next++;
            if (next < pop.Count) return false;
            Breed();
            return true;
        }

        void Breed()
        {
            pop.Sort((a, b) => b.Fitness.CompareTo(a.Fitness));
            double mean = 0;
            foreach (var g in pop) mean += g.Fitness;
            mean /= pop.Count;
            if (Best == null || pop[0].Fitness > Best.Fitness) Best = pop[0];
            BestHistory.Add(pop[0].Fitness);
            MeanHistory.Add(mean);
            var children = new List<Genome>();
            for (int i = 0; i < Elite && i < pop.Count; i++) children.Add(pop[i]);
            while (children.Count < Population)
            {
                var a = Tournament(); var b = Tournament();
                var genes = new int[Template.Length];
                for (int k = 0; k < genes.Length; k++)
                {
                    genes[k] = (rng.Next() & 1) == 0 ? a.Gains[k] : b.Gains[k];
                    uint roll = rng.Next() % 100;
                    if (roll < 25) genes[k] = Toggle(k) ? 1 - Math.Min(1, genes[k]) : Clamp(genes[k] + (int)(rng.Next() % 5) - 2, 0, MaxGain);
                    else if (roll < 32) genes[k] = Toggle(k) ? (int)(rng.Next() & 1) : rng.Range(0, MaxGain + 1);
                }
                children.Add(new Genome { Gains = genes });
            }
            pop = children;
            next = 0;
            Generation++;
        }

        Genome Tournament()
        {
            Genome best = null;
            for (int i = 0; i < 3; i++)
            {
                var g = pop[rng.Range(0, pop.Count)];
                if (best == null || g.Fitness > best.Fitness) best = g;
            }
            return best;
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
