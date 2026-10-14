using System;
using System.Collections.Generic;
using System.Diagnostics;
using Argmax.Sim;
using Unity.InferenceEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;

namespace ArgmaxClient
{
    /// <summary>
    /// The gym: N arenas of commander-vs-commander self-play on the exact simulation the server runs.
    /// Start `mlagents-learn ml/config/commander.yaml` and press Play in the GYM screen to train; without a
    /// trainer the agents fall back to the shipped model or the OVERMIND heuristic, so you can just watch.
    /// The arena on screen is a live feed, AI Warehouse style.
    /// </summary>
    public sealed class GymSession : ISession
    {
        public sealed class RewardSpec
        {
            public float Win = 1f;            // terminal, zero-sum
            public float Trade = 0.0005f;     // per value of (killed - lost)
            public float Mining = 0.0003f;    // per scrap mined
            public int EpisodeTicks = 3600;   // 6 minutes, then judged on value
            public float Lead = 0.5f;         // timeout reward for the side with more total value
        }

        sealed class Body : ICommanderBody
        {
            public Arena Arena;
            public int SeatIndex;
            public World World => Arena.W;
            public int Seat => SeatIndex;
            public void Apply(MetaDecision d) => Meta.Apply(Arena.W, SeatIndex, d);
        }

        sealed class Arena
        {
            public World W;
            public readonly CommanderAgent[] Agents = new CommanderAgent[2];
            public readonly int[] Killed = new int[2], Lost = new int[2], Mined = new int[2];
            public int Episodes;
            public ulong Seed;
        }

        readonly List<Arena> arenas = new List<Arena>();
        readonly GameObject root;
        readonly StepLog log = new StepLog();
        readonly StepLog pending = new StepLog();
        public readonly RewardSpec Rewards = new RewardSpec();
        public int Focus;
        public int Episodes, Wins0, Wins1, Draws;
        public long Decisions;
        float watchAcc;
        ulong nextSeed = 1;

        public GymSession(int count)
        {
            Academy.Instance.AutomaticSteppingEnabled = false;
            root = new GameObject("Gym");
            var model = Resources.Load<ModelAsset>(MlPilot.ModelPath);
            for (int i = 0; i < count; i++)
            {
                var a = new Arena();
                arenas.Add(a);
                Reset(a);
                for (int s = 0; s < 2; s++)
                {
                    var go = new GameObject("Arena" + i + "-Seat" + s);
                    go.transform.SetParent(root.transform);
                    a.Agents[s] = CommanderAgent.Create(go, "Commander", s, model, new Body { Arena = a, SeatIndex = s }, BehaviorType.Default);
                }
            }
        }

        void Reset(Arena a)
        {
            a.Seed = nextSeed++ * 7919UL;
            a.W = Sim.NewDuel(a.Seed, SeatKind.Ai, SeatKind.Ai);
            Array.Clear(a.Killed, 0, 2); Array.Clear(a.Lost, 0, 2); Array.Clear(a.Mined, 0, 2);
        }

        public bool Training => Academy.Instance.IsCommunicatorOn;
        public string Title => "GYM x" + arenas.Count;
        public string Status => (Training ? "TRAINING (trainer connected)" : "watching (no trainer: model or heuristic)") +
                                $" | episodes {Episodes}  seat0 {Wins0} / seat1 {Wins1} / draws {Draws} | decisions {Decisions}";
        public string Banner => $"arena {Focus + 1}/{arenas.Count}  [ and ] to switch";
        public World World => arenas[Focus].W;
        public int LocalSeat => 0;
        public float TickSeconds => Rules.TickMs / 1000f / Mathf.Max(0.01f, Speed);
        public bool CanCommand => false;
        public float Speed { get; set; } = 4f;
        public bool Paused { get; set; }
        public string PilotReadout => "seat0 " + arenas[Focus].Agents[0].LastDecision + "\nseat1 " + arenas[Focus].Agents[1].LastDecision;

        public void Update(float dt)
        {
            if (Paused) return;
            if (Training)
            {
                // as many decision intervals as fit in the frame budget: the trainer is the bottleneck
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 25) Interval();
                return;
            }
            watchAcc += dt * Speed;
            float interval = Rules.MetaEvery * Rules.TickMs / 1000f;
            int n = 0;
            while (watchAcc >= interval && n++ < 20) { watchAcc -= interval; Interval(); }
        }

        /// <summary>One decision for every agent, then 10 simulation ticks in every arena.</summary>
        void Interval()
        {
            foreach (var a in arenas) { a.Agents[0].RequestDecision(); a.Agents[1].RequestDecision(); }
            Academy.Instance.EnvironmentStep();
            Decisions += arenas.Count * 2;
            for (int i = 0; i < arenas.Count; i++)
            {
                var a = arenas[i];
                for (int t = 0; t < Rules.MetaEvery && !a.W.Ended; t++)
                {
                    Sim.Step(a.W, null, log);
                    if (i == Focus) FxUtil.Append(pending, log);
                }
                Reward(a);
            }
        }

        void Reward(Arena a)
        {
            var w = a.W;
            for (int s = 0; s < 2; s++)
            {
                var seat = w.Seats[s];
                int dk = seat.Killed - a.Killed[s], dl = seat.Lost - a.Lost[s], dm = seat.Mined - a.Mined[s];
                a.Killed[s] = seat.Killed; a.Lost[s] = seat.Lost; a.Mined[s] = seat.Mined;
                a.Agents[s].AddReward(Rewards.Trade * (dk - dl) + Rewards.Mining * dm);
            }
            bool timeout = w.Tick >= Rewards.EpisodeTicks;
            if (!w.Ended && !timeout) return;
            Episodes++; a.Episodes++;
            if (w.Winner == 0) { Wins0++; a.Agents[0].AddReward(Rewards.Win); a.Agents[1].AddReward(-Rewards.Win); }
            else if (w.Winner == 1) { Wins1++; a.Agents[1].AddReward(Rewards.Win); a.Agents[0].AddReward(-Rewards.Win); }
            else if (timeout)
            {
                var t0 = Sim.Tally(w, 0); var t1 = Sim.Tally(w, 1);
                int v0 = t0.Value + w.Seats[0].Scrap + w.Seats[0].CoreHp / 10, v1 = t1.Value + w.Seats[1].Scrap + w.Seats[1].CoreHp / 10;
                int lead = v0 > v1 ? 0 : v1 > v0 ? 1 : -1;
                if (lead >= 0) { a.Agents[lead].AddReward(Rewards.Lead); a.Agents[1 - lead].AddReward(-Rewards.Lead); }
                Draws++;
            }
            else Draws++;
            if (timeout && !w.Ended) { a.Agents[0].EpisodeInterrupted(); a.Agents[1].EpisodeInterrupted(); }
            else { a.Agents[0].EndEpisode(); a.Agents[1].EndEpisode(); }
            Reset(a);
        }

        public void Cycle(int dir) { Focus = (Focus + dir + arenas.Count) % arenas.Count; pending.Clear(); }

        public void Issue(Cmd cmd) { }

        public bool TakeFx(StepLog into)
        {
            if (!FxUtil.Any(pending)) return false;
            FxUtil.Append(into, pending);
            pending.Clear();
            return true;
        }

        public IEnumerable<Program> Library => Presets.All;
        public void SaveProgram(string source, Action<Program, string> done) => done(null, "the gym has no program library");

        public void Dispose()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            Academy.Instance.AutomaticSteppingEnabled = true;
        }
    }
}
