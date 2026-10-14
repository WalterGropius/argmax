using System;
using System.Text;
using Argmax.Sim;
using Unity.InferenceEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.Networking;

namespace ArgmaxClient
{
    /// <summary>
    /// The ML-Agents commander. Observes <see cref="CommanderObs"/>, acts with four discrete branches that map
    /// 1:1 onto <see cref="MetaDecision"/>: the same interface Argot metaprograms and Jev use. Its heuristic is
    /// the OVERMIND metaprogram, so demonstrations can bootstrap training.
    /// </summary>
    public sealed class CommanderAgent : Agent
    {
        public ICommanderBody Body;
        public MetaDecision LastDecision;
        readonly float[] obs = new float[CommanderObs.Size];
        static Program overmind;

        public static CommanderAgent Create(GameObject go, string behavior, int team, ModelAsset model, ICommanderBody body, BehaviorType type)
        {
            go.SetActive(false); // configure before Agent.OnEnable reads the parameters
            var bp = go.AddComponent<BehaviorParameters>();
            bp.BehaviorName = behavior;
            bp.TeamId = team;
            bp.BrainParameters.VectorObservationSize = CommanderObs.Size;
            bp.BrainParameters.NumStackedVectorObservations = 1;
            bp.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(Meta.Branches);
            bp.Model = model;
            bp.BehaviorType = type;
            var agent = go.AddComponent<CommanderAgent>();
            agent.Body = body;
            agent.MaxStep = 0;
            go.SetActive(true);
            return agent;
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            var w = Body.World;
            Sim.Refresh(w);
            CommanderObs.Write(w, Body.Seat, obs);
            for (int i = 0; i < obs.Length; i++) sensor.AddObservation(obs[i]);
        }

        public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
        {
            var w = Body.World;
            for (int b = 0; b < Meta.Branches.Length; b++)
                for (int a = 1; a < Meta.Branches[b]; a++)
                    if (!CommanderObs.Allowed(w, Body.Seat, b, a)) actionMask.SetActionEnabled(b, a, false);
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            var da = actions.DiscreteActions;
            var d = new MetaDecision { Produce = da[0], Spawn = da[1], Beacon = da[2], Army = da[3] };
            LastDecision = d;
            Body.Apply(d);
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            if (overmind == null) overmind = Presets.Get(Presets.Overmind, 0);
            var w = Body.World;
            Sim.Refresh(w);
            var d = Meta.Decide(w, Body.Seat, overmind);
            var da = actionsOut.DiscreteActions;
            da[0] = d.Produce; da[1] = d.Spawn; da[2] = d.Beacon; da[3] = d.Army;
        }
    }

    /// <summary>Hosts a <see cref="CommanderAgent"/> on a seat. Uses the trained model if one ships in Resources.</summary>
    public sealed class MlPilot : IPilot
    {
        public const string ModelPath = "Argmax/Brains/Commander";
        readonly GameObject go;
        readonly CommanderAgent agent;
        readonly ICommanderBody body;
        readonly bool hasModel;
        int nextTick;

        public MlPilot(ICommanderBody body)
        {
            this.body = body;
            var model = Resources.Load<ModelAsset>(ModelPath);
            hasModel = model != null;
            go = new GameObject("CommanderAgent (seat " + body.Seat + ")");
            agent = CommanderAgent.Create(go, "Commander", body.Seat, model, body, hasModel ? BehaviorType.InferenceOnly : BehaviorType.HeuristicOnly);
        }

        public string Readout => (hasModel ? "ML policy" : "ML (no model yet: OVERMIND heuristic)") + " -> " + agent.LastDecision;

        public void Tick()
        {
            var w = body.World;
            if (w == null || w.Ended || w.Tick < nextTick) return;
            nextTick = w.Tick + Rules.MetaEvery;
            agent.RequestDecision();
        }

        public void Dispose() { if (go != null) UnityEngine.Object.Destroy(go); }
    }

    /// <summary>
    /// TypeSafe Jev as a commander: every 3 s it reads a plain-language situation report and answers four
    /// typed choice questions. Its probabilities are shown in the terminal so you can watch System 1 think.
    /// </summary>
    public sealed class JevPilot : IPilot
    {
        readonly ICommanderBody body;
        readonly string key;
        UnityWebRequest req;
        int nextTick;
        float sentAt;
        public int Calls, Failures;
        public float LastLatency;
        public JevBrief.Reading Last;
        string readout = "JEV warming up";

        public JevPilot(ICommanderBody body, string key)
        {
            this.body = body;
            this.key = key;
            if (string.IsNullOrEmpty(key)) readout = "JEV needs an API key (menu field, JEV_API_KEY env, or .env.local)";
        }

        public string Readout => readout;

        public void Tick()
        {
            var w = body.World;
            if (w == null || w.Ended || string.IsNullOrEmpty(key)) return;
            if (req != null)
            {
                if (!req.isDone) return;
                Receive(w);
                req.Dispose();
                req = null;
            }
            if (w.Tick < nextTick) return;
            nextTick = w.Tick + JevBrief.DecideEvery;
            Sim.Refresh(w);
            var json = JevBrief.Request(w, body.Seat);
            req = new UnityWebRequest(JevBrief.Endpoint, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 10,
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + key);
            sentAt = Time.realtimeSinceStartup;
            req.SendWebRequest();
        }

        void Receive(World w)
        {
            LastLatency = Time.realtimeSinceStartup - sentAt;
            if (req.result != UnityWebRequest.Result.Success)
            {
                Failures++;
                readout = "JEV unreachable (" + req.responseCode + " " + req.error + ")";
                return;
            }
            if (JevBrief.TryDecode(req.downloadHandler.text, w, body.Seat, out var d, out var reading))
            {
                Calls++;
                Last = reading;
                body.Apply(d);
                readout = "JEV " + reading.Model + " " + (LastLatency * 1000f).ToString("0") + "ms | " + reading.Summary();
            }
            else
            {
                Failures++;
                readout = "JEV: " + reading.Error;
            }
        }

        public void Dispose()
        {
            if (req != null) { req.Abort(); req.Dispose(); req = null; }
        }
    }
}
