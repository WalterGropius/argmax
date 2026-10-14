using System;
using System.IO;
using ArgmaxClient;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Argmax.EditorTools
{
    /// <summary>
    /// Editor utilities. The game needs no scene setup (App boots itself), but builds want a scene and CI
    /// wants a headless smoke run. Smoke: plays a mode for a while and writes screenshots to docs/screens.
    ///   Unity -batchmode -projectPath client -executeMethod Argmax.EditorTools.ArgmaxTools.Smoke
    ///   env ARGMAX_SMOKE = sandbox | skirmish | chamber | gym   (default sandbox)
    /// </summary>
    public static class ArgmaxTools
    {
        const string ScenePath = "Assets/Argmax/Scenes/Main.unity";

        [MenuItem("ARGMAX/Create Main Scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Argmax/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("ARGMAX: created " + ScenePath + " (empty on purpose: App boots itself)");
        }

        [MenuItem("ARGMAX/Open Repository Folder")]
        public static void Reveal() => EditorUtility.RevealInFinder(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..")));

        // ------------------------------------------------------------------ headless smoke run

        static string mode;
        static double started;
        static int shots;
        static readonly float[] ShotAt = { 6f, 14f, 26f };
        static string outDir;

        public static void Smoke()
        {
            mode = Environment.GetEnvironmentVariable("ARGMAX_SMOKE") ?? "sandbox";
            outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "screens"));
            Directory.CreateDirectory(outDir);
            if (!File.Exists(ScenePath)) CreateScene();
            EditorSceneManager.OpenScene(ScenePath);
            shots = 0;
            // keep this static state and the update hook alive across the play-mode transition
            prevOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            prevOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
            Debug.Log("ARGMAX SMOKE: entering play mode (" + mode + ")");
        }

        static bool begun;
        static RenderTexture rt, uiRt;
        static bool prevOptionsEnabled;
        static EnterPlayModeOptions prevOptions;

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            var app = UnityEngine.Object.FindAnyObjectByType<App>();
            if (app == null) return;
            if (!begun)
            {
                begun = true;
                started = EditorApplication.timeSinceStartup;
                app.Rig.EdgePan = false; // the batch-mode cursor sits on a screen edge
                var look = Environment.GetEnvironmentVariable("ARGMAX_LOOK");
                if (look != null) app.View.Morph = look != "grid";
                // world and UI render to separate targets and are composited on capture
                rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
                uiRt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
                app.Rig.Cam.targetTexture = rt;
                var doc = app.GetComponent<UIDocument>();
                if (doc != null && doc.panelSettings != null)
                {
                    doc.panelSettings.targetTexture = uiRt;
                    doc.panelSettings.clearColor = true;
                    doc.panelSettings.colorClearValue = new Color(0, 0, 0, 0);
                }
                switch (mode)
                {
                    case "skirmish": app.StartSkirmish(); break;
                    case "chamber": app.RunChamber(5, "always: merge 2\nalways: flock\nwhen big: seek core 3", 1f); break;
                    case "gym": app.StartGym(4); break;
                    case "battle": app.StartSpectate(); if (app.Session != null) app.Session.Speed = 8f; break;
                    case "film": BeginFilm(app); return;
                    case "fields":
                        app.StartSpectate(30, Argmax.Sim.Presets.Overmind, Argmax.Sim.Presets.Turtle, 4f);
                        app.Ui.Cinematic();
                        app.View.FieldMode = 3;
                        break;
                    case "menu": break;
                    case "map":
                        app.Progress.DryRun = true;
                        if (int.TryParse(Environment.GetEnvironmentVariable("ARGMAX_DEMO_PROGRESS"), out var demo)) app.Progress.DemoFill(demo);
                        app.StartCampaign();
                        break;
                    case "level":
                    {
                        app.Progress.DryRun = true;
                        int id = int.TryParse(Environment.GetEnvironmentVariable("ARGMAX_LEVEL"), out var lv) ? lv : 202;
                        float sp = float.TryParse(Environment.GetEnvironmentVariable("ARGMAX_SPEED"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var spd) ? spd : 1f;
                        app.Campaign.Autoplay(id, sp);
                        if (Environment.GetEnvironmentVariable("ARGMAX_FIELD") != null) app.View.FieldMode = int.Parse(Environment.GetEnvironmentVariable("ARGMAX_FIELD"));
                        break;
                    }
                    case "evolve":
                    {
                        app.Progress.DryRun = true;
                        app.Campaign.Enter(602);
                        app.Campaign.DebugEvolve();
                        break;
                    }
                    case "briefing":
                    {
                        app.Progress.DryRun = true;
                        int id = int.TryParse(Environment.GetEnvironmentVariable("ARGMAX_LEVEL"), out var lv) ? lv : 201;
                        app.Campaign.Enter(id);
                        break;
                    }
                    case "ml": app.Settings.Pilot = PilotKind.Ml; app.StartSandbox(); break;
                    case "jev": app.Settings.Pilot = PilotKind.Jev; app.StartSandbox(); break;
                    default: app.StartSandbox(); break;
                }
                Debug.Log("ARGMAX SMOKE: started " + mode);
                return;
            }
            if (mode == "film") { FilmFrame(app); return; }
            double t = EditorApplication.timeSinceStartup - started;
            if (shots < ShotAt.Length && t >= ShotAt[shots])
            {
                if (mode != "fields" && mode != "level" && mode != "map" && mode != "briefing" && mode != "evolve")
                {
                    if (shots == 1 && app.Session?.World != null) { SelectSome(app); app.View.FieldMode = mode == "battle" ? 5 : mode == "chamber" ? 2 : 3; }
                    if (shots == 2) app.View.FieldMode = 0;
                }
                var tag = mode == "level" || mode == "briefing" ? mode + "-" + (Environment.GetEnvironmentVariable("ARGMAX_LEVEL") ?? "202") : mode;
                if (mode == "level" && shots == 1 && Environment.GetEnvironmentVariable("ARGMAX_FIELD") != null) app.View.FieldMode = int.Parse(Environment.GetEnvironmentVariable("ARGMAX_FIELD"));
                Capture(app, Path.Combine(outDir, tag + "-" + shots + ".png"));
                var w = app.Session?.World;
                Debug.Log($"ARGMAX SMOKE: t={t:0.0}s session={app.Session?.Title} status={app.Session?.Status} tick={(w != null ? w.Tick : -1)} motes={(w != null ? w.MoteCount : 0)} pilot={app.Session?.PilotReadout}");
                shots++;
                if (mode == "fields") app.View.FieldMode = shots == 1 ? 5 : 2; // gradient for the next shot
            }
            if (shots >= ShotAt.Length)
            {
                var w = app.Session?.World;
                bool ok = mode == "map" || (w != null && w.MoteCount > 0);
                Debug.Log("ARGMAX SMOKE: " + (ok ? "PASS" : "FAIL") + " (" + mode + ")");
                app.Settings.Pilot = PilotKind.Server; // don't leave a smoke choice in the user's prefs
                app.Settings.Save();
                EditorApplication.update -= Tick;
                EditorApplication.isPlaying = false;
                EditorSettings.enterPlayModeOptionsEnabled = prevOptionsEnabled;
                EditorSettings.enterPlayModeOptions = prevOptions;
                EditorApplication.Exit(ok ? 0 : 1);
            }
        }

        // ------------------------------------------------------------------ headless training session

        static double trainUntil, nextReport;

        /// <summary>
        /// Run the gym in play mode for ARGMAX_TRAIN_MINUTES (default 10) so a waiting `mlagents-learn` can
        /// train against it, then quit; the trainer exports the .onnx when the environment closes.
        /// </summary>
        public static void Train()
        {
            if (!File.Exists(ScenePath)) CreateScene();
            EditorSceneManager.OpenScene(ScenePath);
            float minutes = float.TryParse(Environment.GetEnvironmentVariable("ARGMAX_TRAIN_MINUTES"), out var m) ? m : 10f;
            prevOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            prevOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            begun = false;
            trainUntil = minutes * 60;
            EditorApplication.update += TrainTick;
            EditorApplication.isPlaying = true;
            Debug.Log("ARGMAX TRAIN: entering play mode for " + minutes + " min");
        }

        static void TrainTick()
        {
            if (!EditorApplication.isPlaying) return;
            var app = UnityEngine.Object.FindAnyObjectByType<App>();
            if (app == null) return;
            if (!begun)
            {
                begun = true;
                started = EditorApplication.timeSinceStartup;
                nextReport = 30;
                int arenas = int.TryParse(Environment.GetEnvironmentVariable("ARGMAX_ARENAS"), out var a) ? a : 8;
                app.StartGym(arenas);
                return;
            }
            double t = EditorApplication.timeSinceStartup - started;
            if (t >= nextReport)
            {
                nextReport += 30;
                Debug.Log($"ARGMAX TRAIN: t={t:0}s {app.Session?.Status}");
            }
            if (t < trainUntil) return;
            Debug.Log("ARGMAX TRAIN: done. " + app.Session?.Status);
            EditorApplication.update -= TrainTick;
            EditorApplication.isPlaying = false;
            EditorSettings.enterPlayModeOptionsEnabled = prevOptionsEnabled;
            EditorSettings.enterPlayModeOptions = prevOptions;
            EditorApplication.Exit(0);
        }

        static void SelectSome(App app)
        {
            var w = app.Session.World;
            int seat = app.Session.LocalSeat, n = 0;
            for (int i = 0; i < w.MoteCount && n < 12; i++)
                if (w.Motes[i].Seat == seat) { app.View.Selected.Add(w.Motes[i].Id); n++; }
        }

        // ------------------------------------------------------------------ film: fixed-timestep frames for trailers

        static int filmEnd, lastFrame, frameNo;
        static string frameDir;

        /// <summary>ARGMAX_FILM = "seed,startTick,ticks,speed" (defaults: a known 48 s highlight, OVERMIND vs ASSEMBLER).</summary>
        static void BeginFilm(App app)
        {
            var p = (Environment.GetEnvironmentVariable("ARGMAX_FILM") ?? "30,1593,480,2,OVERMIND,TURTLE").Split(',');
            ulong seed = ulong.Parse(p[0]); int start = int.Parse(p[1]); int ticks = int.Parse(p[2]); float speed = float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture);
            uint a = p.Length > 4 ? Argmax.Sim.Presets.IdOf(p[4]) : Argmax.Sim.Presets.Overmind;
            uint b = p.Length > 5 ? Argmax.Sim.Presets.IdOf(p[5]) : Argmax.Sim.Presets.Turtle;
            app.StartSpectate(seed, a, b, speed);
            if (app.Session is LocalSession ls) ls.RunToEnd(start);
            var post = app.Rig.Cam.GetComponent<NeonPost>();
            if (post != null) post.Grain = 0.012f; // grain is noise to a GIF encoder
            app.Ui.Cinematic();
            filmEnd = start + ticks;
            frameDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "logs", "frames"));
            if (Directory.Exists(frameDir)) Directory.Delete(frameDir, true);
            Directory.CreateDirectory(frameDir);
            Time.captureFramerate = 15;
            lastFrame = Time.frameCount;
            frameNo = 0;
            Debug.Log("ARGMAX FILM: seed " + seed + " ticks " + start + ".." + filmEnd + " -> " + frameDir);
        }

        static void FilmFrame(App app)
        {
            var w = app.Session?.World;
            if (w == null || Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            // follow the densest firefight: the firing mote with the most firing neighbours, and its cluster
            int best = -1, bestN = 0;
            for (int i = 0; i < w.MoteCount; i++)
            {
                if (!w.Motes[i].Has(Argmax.Sim.MoteFlags.Fired)) continue;
                int c = 0;
                for (int j = 0; j < w.MoteCount; j++)
                    if (w.Motes[j].Has(Argmax.Sim.MoteFlags.Fired) && Math.Abs(w.Motes[j].X - w.Motes[i].X) <= 6 && Math.Abs(w.Motes[j].Y - w.Motes[i].Y) <= 6) c++;
                if (c > bestN) { bestN = c; best = i; }
            }
            if (best >= 0)
            {
                float sx = 0, sz = 0; int n = 0;
                for (int j = 0; j < w.MoteCount; j++)
                    if (Math.Abs(w.Motes[j].X - w.Motes[best].X) <= 8 && Math.Abs(w.Motes[j].Y - w.Motes[best].Y) <= 8) { sx += w.Motes[j].X; sz += w.Motes[j].Y; n++; }
                var target = new Vector3(sx / n + 0.5f, 0, sz / n + 0.5f);
                app.Rig.Focus = frameNo == 0 ? target : Vector3.Lerp(app.Rig.Focus, target, 0.06f);
            }
            app.Rig.Height = 34f;
            if (frameNo > 2) Capture(app, Path.Combine(frameDir, "f" + frameNo.ToString("0000") + ".png"), quiet: true);
            frameNo++;
            if (w.Tick < filmEnd && !w.Ended && frameNo < 2000) return;
            Debug.Log("ARGMAX FILM: " + frameNo + " frames");
            Time.captureFramerate = 0;
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
            EditorSettings.enterPlayModeOptionsEnabled = prevOptionsEnabled;
            EditorSettings.enterPlayModeOptions = prevOptions;
            EditorApplication.Exit(0);
        }

        static Color[] Read(RenderTexture src)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = src;
            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels();
            UnityEngine.Object.DestroyImmediate(tex);
            return px;
        }

        static void Capture(App app, string path, bool quiet = false)
        {
            var world = Read(rt);
            var ui = Read(uiRt);
            // UI Toolkit draws premultiplied alpha: out = ui + world * (1 - ui.a)
            for (int i = 0; i < world.Length; i++)
            {
                var u = ui[i];
                var w = world[i];
                world[i] = new Color(u.r + w.r * (1 - u.a), u.g + w.g * (1 - u.a), u.b + w.b * (1 - u.a), 1);
            }
            var outTex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            outTex.SetPixels(world);
            outTex.Apply();
            File.WriteAllBytes(path, outTex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(outTex);
            if (!quiet) Debug.Log("ARGMAX SMOKE: wrote " + path);
        }
    }
}
