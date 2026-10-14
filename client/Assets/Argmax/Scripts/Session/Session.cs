using System;
using System.Collections.Generic;
using Argmax.Sim;
using UnityEngine;

namespace ArgmaxClient
{
    /// <summary>
    /// Everything the view, input and UI need from "a game in progress", wherever the simulation lives:
    /// locally (sandbox, chambers), on SpacetimeDB (skirmish, duels) or in the ML-Agents gym.
    /// </summary>
    public interface ISession : IDisposable
    {
        string Title { get; }
        string Status { get; }
        string Banner { get; }
        World World { get; }
        int LocalSeat { get; }
        float TickSeconds { get; }
        bool CanCommand { get; }
        float Speed { get; set; }
        bool Paused { get; set; }
        string PilotReadout { get; }
        void Update(float dt);
        void Issue(Cmd cmd);
        bool TakeFx(StepLog into);
        IEnumerable<Program> Library { get; }
        void SaveProgram(string source, Action<Program, string> done);
    }

    /// <summary>A brain that drives one seat with MetaDecisions: Jev, an ML-Agents policy, ...</summary>
    public interface IPilot : IDisposable
    {
        string Readout { get; }
        void Tick();
    }

    public interface ICommanderBody
    {
        World World { get; }
        int Seat { get; }
        void Apply(MetaDecision d);
    }

    public static class FxUtil
    {
        public static void Append(StepLog into, StepLog from)
        {
            into.Shots.AddRange(from.Shots); into.Deaths.AddRange(from.Deaths); into.Merges.AddRange(from.Merges);
            into.Converts.AddRange(from.Converts); into.Teleports.AddRange(from.Teleports); into.Spawns.AddRange(from.Spawns);
        }

        public static bool Any(StepLog l) =>
            l.Shots.Count + l.Deaths.Count + l.Merges.Count + l.Converts.Count + l.Teleports.Count + l.Spawns.Count > 0;
    }

    /// <summary>The simulation running inside the client: offline sandbox, chamber runs, replays.</summary>
    public sealed class LocalSession : ISession, ICommanderBody
    {
        public World W;
        readonly List<Cmd> queue = new List<Cmd>();
        readonly StepLog log = new StepLog();
        readonly StepLog pending = new StepLog();
        readonly Dictionary<uint, Program> library = new Dictionary<uint, Program>();
        uint nextProgramId = 2_000_000;
        float acc;
        public IPilot Pilot;
        public Action<World> AfterStep;

        public LocalSession(World w, string title, int localSeat = 0)
        {
            W = w;
            Title = title;
            LocalSeat = localSeat;
            foreach (var p in Presets.All) library[p.Id] = p;
            foreach (var p in w.Programs.Values) library[p.Id] = p;
        }

        public string Title { get; }
        public string Status => W.Ended ? "ended" : Paused ? "paused" : Speed == 1f ? "offline" : "offline x" + Speed;
        public string Banner { get; set; } = "";
        public World World => W;
        public int LocalSeat { get; }
        public float TickSeconds => Rules.TickMs / 1000f / Mathf.Max(0.01f, Speed);
        public bool CanCommand { get; set; } = true;
        public float Speed { get; set; } = 1f;
        public bool Paused { get; set; }
        public string PilotReadout => Pilot?.Readout;

        // ICommanderBody: an AI seat in this local world (seat 1)
        int ICommanderBody.Seat => 1;
        public void Apply(MetaDecision d) { foreach (var c in Meta.ToCommands(W, 1, d)) queue.Add(c); }

        public void Update(float dt)
        {
            if (W.Ended || Paused) return;
            Pilot?.Tick();
            acc += dt * Speed;
            float step = Rules.TickMs / 1000f;
            int steps = 0;
            while (acc >= step && steps < 200 && !W.Ended)
            {
                Sim.Step(W, queue, log);
                queue.Clear();
                FxUtil.Append(pending, log);
                acc -= step;
                steps++;
                AfterStep?.Invoke(W);
            }
            if (steps == 200) acc = 0;
        }

        /// <summary>Run as fast as possible (chambers' "skip to result").</summary>
        public void RunToEnd(int maxTicks)
        {
            int n = 0;
            while (!W.Ended && n++ < maxTicks) { Sim.Step(W, queue, log); queue.Clear(); }
            pending.Clear();
        }

        public void Issue(Cmd cmd) { if (CanCommand) queue.Add(cmd); }

        public bool TakeFx(StepLog into)
        {
            if (!FxUtil.Any(pending)) return false;
            FxUtil.Append(into, pending);
            pending.Clear();
            return true;
        }

        public IEnumerable<Program> Library => library.Values;

        public void SaveProgram(string source, Action<Program, string> done)
        {
            var r = Argot.Compile(source);
            if (!r.Ok) { done(null, string.Join("\n", r.Errors)); return; }
            r.Program.Id = nextProgramId++;
            W.AddProgram(r.Program);
            library[r.Program.Id] = r.Program;
            done(r.Program, null);
        }

        public void Dispose() { Pilot?.Dispose(); Pilot = null; }
    }
}
