using System;
using System.IO;
using Argmax.Sim;
using UnityEngine;

namespace ArgmaxClient
{
    public enum PilotKind { Server, Ml, Jev }

    /// <summary>Player-facing settings, persisted in PlayerPrefs. Secrets never go into assets.</summary>
    public sealed class Settings
    {
        public const string LocalUri = "http://127.0.0.1:3000";
        public const string MaincloudUri = "https://maincloud.spacetimedb.com";

        public string Server = "local"; // local | custom | maincloud (editor only: release builds are self-hosted)
        public string CustomUri = "";
        public string CustomDb = "argmax";
        public string LocalDb = "argmax";
        public string MaincloudDb = "argmax-deadnet";
        public string Name = "process";
        public uint AiMeta = Presets.Overmind;
        public PilotKind Pilot = PilotKind.Server;
        public string JevKeyEntered = "";
        public bool Muted, MusicOn = true;
        /// <summary>MORPH: raymarched matter. GRID: flat Bauhaus shapes (lighter on old GPUs and browsers).</summary>
        public bool Morph = true;

        public string Uri => Server == "local" ? LocalUri : Server == "maincloud" ? MaincloudUri : CustomUri;
        public string Database => Server == "maincloud" ? MaincloudDb : Server == "custom" ? CustomDb : LocalDb;

        /// <summary>Release builds only know your own servers: localhost or one you type in.</summary>
        public static string[] ServerChoices => Application.isEditor ? new[] { "local", "custom", "maincloud" } : new[] { "local", "custom" };

        public static Settings Load()
        {
            var s = new Settings
            {
                Server = PlayerPrefs.GetString("argmax.server", "local"),
                CustomUri = PlayerPrefs.GetString("argmax.customUri", ""),
                CustomDb = PlayerPrefs.GetString("argmax.customDb", "argmax"),
                Muted = PlayerPrefs.GetInt("argmax.muted", 0) != 0,
                MusicOn = PlayerPrefs.GetInt("argmax.music", 1) != 0,
#if UNITY_WEBGL
                Morph = PlayerPrefs.GetInt("argmax.morph", 0) != 0,
#else
                Morph = PlayerPrefs.GetInt("argmax.morph", 1) != 0,
#endif
                LocalDb = PlayerPrefs.GetString("argmax.localDb", "argmax"),
                MaincloudDb = PlayerPrefs.GetString("argmax.maincloudDb", "argmax-deadnet"),
                Name = PlayerPrefs.GetString("argmax.name", "proc-" + UnityEngine.Random.Range(1000, 9999)),
                AiMeta = (uint)PlayerPrefs.GetInt("argmax.aiMeta", (int)Presets.Overmind),
                Pilot = (PilotKind)PlayerPrefs.GetInt("argmax.pilot", 0),
                JevKeyEntered = PlayerPrefs.GetString("argmax.jevKey", ""),
            };
            if (System.Array.IndexOf(ServerChoices, s.Server) < 0) s.Server = "local";
            return s;
        }

        public void Save()
        {
            PlayerPrefs.SetString("argmax.server", Server);
            PlayerPrefs.SetString("argmax.customUri", CustomUri);
            PlayerPrefs.SetString("argmax.customDb", CustomDb);
            PlayerPrefs.SetInt("argmax.muted", Muted ? 1 : 0);
            PlayerPrefs.SetInt("argmax.music", MusicOn ? 1 : 0);
            PlayerPrefs.SetInt("argmax.morph", Morph ? 1 : 0);
            PlayerPrefs.SetString("argmax.localDb", LocalDb);
            PlayerPrefs.SetString("argmax.maincloudDb", MaincloudDb);
            PlayerPrefs.SetString("argmax.name", Name);
            PlayerPrefs.SetInt("argmax.aiMeta", (int)AiMeta);
            PlayerPrefs.SetInt("argmax.pilot", (int)Pilot);
            PlayerPrefs.SetString("argmax.jevKey", JevKeyEntered);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// The Jev API key, from (in order) the menu field, the JEV_API_KEY environment variable, or a
        /// gitignored .env.local next to the Unity project (handy in the editor). Never stored in an asset.
        /// </summary>
        public string JevKey
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(JevKeyEntered)) return JevKeyEntered.Trim();
                var env = Environment.GetEnvironmentVariable("JEV_API_KEY");
                if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
                try
                {
                    var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".env.local"));
                    if (File.Exists(path))
                        foreach (var line in File.ReadAllLines(path))
                            if (line.StartsWith("JEV_API_KEY=")) return line.Substring("JEV_API_KEY=".Length).Trim();
                }
                catch (Exception) { }
                return null;
            }
        }
    }

    /// <summary>
    /// MORPHOGENESIS: warm ink and bone, with the Bauhaus primaries as living matter. You are cobalt circles,
    /// whoever you fight is vermilion squares, and the Noise is chrome-yellow triangles (Kandinsky's mapping).
    /// </summary>
    public static class Palette
    {
        public static readonly Color Background = new Color(0.052f, 0.047f, 0.040f);
        public static readonly Color Ink = new Color(0.055f, 0.051f, 0.043f);
        public static readonly Color Bone = new Color(0.925f, 0.894f, 0.827f);
        public static readonly Color Me = new Color(0.184f, 0.380f, 1.0f);
        public static readonly Color Foe = new Color(1.0f, 0.275f, 0.149f);
        public static readonly Color Noise = new Color(0.957f, 0.761f, 0.051f);
        public static readonly Color Scrap = new Color(0.93f, 0.87f, 0.72f);
        public static readonly Color Wall = new Color(0.15f, 0.137f, 0.12f);
        public static readonly Color PortalA = new Color(0.45f, 0.62f, 1.0f);
        public static readonly Color PortalB = new Color(1.0f, 0.58f, 0.42f);
        public static readonly Color Well = new Color(0.957f, 0.761f, 0.051f);
        public static readonly Color Goal = new Color(0.96f, 0.84f, 0.38f);
        public static readonly Color White = new Color(0.925f, 0.894f, 0.827f);

        public static Color Seat(int seat, int localSeat)
        {
            if (seat == Rules.NoiseSeat) return Noise;
            return seat == localSeat ? Me : Foe;
        }

        /// <summary>Bigger numbers grow deeper and richer, not whiter: value reads from size and rings.</summary>
        public static Color Tiered(Color c, int tier)
        {
            float t = Mathf.Clamp01((tier - 1) / 10f);
            return Color.Lerp(c, c * 0.72f + Bone * 0.28f, t * 0.5f);
        }

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
