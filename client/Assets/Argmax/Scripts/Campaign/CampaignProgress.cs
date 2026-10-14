using System;
using System.Collections.Generic;
using Argmax.Sim;
using UnityEngine;

namespace ArgmaxClient
{
    /// <summary>
    /// Everything the campaign remembers about you: which tests passed, with how many stars, how many runs it
    /// took (the epilogue plots that learning curve), your drafts, and the components you've built. One JSON
    /// blob in PlayerPrefs, so it works the same on desktop and in the browser.
    /// </summary>
    public sealed class CampaignProgress
    {
        const string Key = "argmax.campaign.v1";

        [Serializable]
        public sealed class Level
        {
            public int id;
            public bool solved;
            public int stars;
            public int bestTicks = int.MaxValue, bestSymbols = int.MaxValue;
            public int runs, fails, hints;
            public int runsToSolve;
            public string draft;
            public string solution;
        }

        [Serializable]
        public sealed class Component
        {
            public string name;
            public string source;
            public int fromLevel;
        }

        [Serializable]
        sealed class Data
        {
            public List<Level> levels = new List<Level>();
            public List<Component> components = new List<Component>();
            public bool unlockAll;
            public bool epilogueSeen;
            public int lastLevel;
        }

        Data data = new Data();
        /// <summary>Smoke runs and captures: unlock everything and never write to disk.</summary>
        public bool DryRun;
        readonly Dictionary<int, Level> byId = new Dictionary<int, Level>();

        public static CampaignProgress Load()
        {
            var p = new CampaignProgress();
            try
            {
                var json = PlayerPrefs.GetString(Key, "");
                if (json.Length > 0) p.data = JsonUtility.FromJson<Data>(json) ?? new Data();
            }
            catch (Exception e) { Debug.LogWarning("ARGMAX: campaign progress unreadable, starting fresh (" + e.Message + ")"); p.data = new Data(); }
            foreach (var l in p.data.levels) p.byId[l.id] = l;
            return p;
        }

        public void Save()
        {
            if (DryRun) return;
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        public Level Get(int id)
        {
            if (byId.TryGetValue(id, out var l)) return l;
            l = new Level { id = id };
            byId[id] = l;
            data.levels.Add(l);
            return l;
        }

        public bool Solved(int id) => byId.TryGetValue(id, out var l) && l.solved;
        public int Stars(int id) => byId.TryGetValue(id, out var l) ? l.stars : 0;
        public bool UnlockAll { get => data.unlockAll; set { data.unlockAll = value; Save(); } }
        public bool EpilogueSeen { get => data.epilogueSeen; set { data.epilogueSeen = value; Save(); } }
        public int LastLevel { get => data.lastLevel; set { data.lastLevel = value; Save(); } }
        public IReadOnlyList<Component> Components => data.components;

        /// <summary>Linear like a syllabus: a test opens when the one before it passes.</summary>
        public bool Unlocked(int id)
        {
            if (data.unlockAll || DryRun) return true;
            int i = Campaign.IndexOf(id);
            return i <= 0 || Solved(Campaign.Levels[i - 1].Id);
        }

        public int SolvedCount { get { int n = 0; foreach (var l in Campaign.Levels) if (Solved(l.Id)) n++; return n; } }
        public int StarCount { get { int n = 0; foreach (var l in Campaign.Levels) n += Stars(l.Id); return n; } }

        /// <summary>The first level you haven't passed (or the finale once everything has).</summary>
        public int NextLevel()
        {
            foreach (var l in Campaign.Levels) if (!Solved(l.Id)) return l.Id;
            return Campaign.Levels[Campaign.Levels.Length - 1].Id;
        }

        public string Draft(ChamberSpec spec)
        {
            var l = Get(spec.Id);
            if (!string.IsNullOrEmpty(l.draft)) return l.draft;
            return spec.Starter ?? "";
        }

        public void SetDraft(int id, string src) { Get(id).draft = src; }

        public void RecordRun(int id) { Get(id).runs++; Save(); }

        public void RecordFail(int id) { Get(id).fails++; Save(); }

        public void RecordHint(int id) { Get(id).hints++; Save(); }

        /// <summary>Returns true the first time a level is solved.</summary>
        public bool RecordPass(ChamberSpec spec, ChamberResult r, string source)
        {
            var l = Get(spec.Id);
            bool first = !l.solved;
            if (first) l.runsToSolve = Math.Max(1, l.runs);
            l.solved = true;
            l.stars = Math.Max(l.stars, r.Stars);
            if (r.Ticks < l.bestTicks) l.bestTicks = r.Ticks;
            if (spec.HasEditor && r.Symbols > 0 && r.Symbols < l.bestSymbols) { l.bestSymbols = r.Symbols; l.solution = source; }
            if (spec.HasEditor && l.solution == null) l.solution = source;
            if (!string.IsNullOrEmpty(spec.Component) && !string.IsNullOrEmpty(source)) SetComponent(spec.Component, source, spec.Id);
            Save();
            return first;
        }

        public void SetComponent(string name, string source, int fromLevel)
        {
            foreach (var c in data.components) if (c.name == name) { c.source = source; c.fromLevel = fromLevel; return; }
            data.components.Add(new Component { name = name, source = source, fromLevel = fromLevel });
        }

        /// <summary>Captures only (DryRun): pretend the first n tests were passed, with plausible stars.</summary>
        public void DemoFill(int n)
        {
            for (int i = 0; i < n && i < Campaign.Levels.Length; i++)
            {
                var l = Get(Campaign.Levels[i].Id);
                l.solved = true; l.stars = 1 + (i * 7 + 3) % 3; l.runs = 1 + (i * 5) % 6; l.runsToSolve = l.runs; l.fails = l.runs - 1;
                var spec = Campaign.Levels[i];
                if (!string.IsNullOrEmpty(spec.Component)) SetComponent(spec.Component, spec.Solution, spec.Id);
            }
        }

        public void Reset()
        {
            data = new Data();
            byId.Clear();
            Save();
        }
    }
}
