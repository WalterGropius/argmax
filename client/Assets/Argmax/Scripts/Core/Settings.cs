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

        public string Server = "local"; // local | maincloud | custom
        public string CustomUri = "";
        public string LocalDb = "argmax";
        public string MaincloudDb = "argmax-deadnet";
        public string Name = "process";
        public uint AiMeta = Presets.Overmind;
        public PilotKind Pilot = PilotKind.Server;
        public string JevKeyEntered = "";

        public string Uri => Server == "local" ? LocalUri : Server == "maincloud" ? MaincloudUri : CustomUri;
        public string Database => Server == "maincloud" ? MaincloudDb : LocalDb;

        public static Settings Load()
        {
            var s = new Settings
            {
                Server = PlayerPrefs.GetString("argmax.server", "local"),
                CustomUri = PlayerPrefs.GetString("argmax.customUri", ""),
                LocalDb = PlayerPrefs.GetString("argmax.localDb", "argmax"),
                MaincloudDb = PlayerPrefs.GetString("argmax.maincloudDb", "argmax-deadnet"),
                Name = PlayerPrefs.GetString("argmax.name", "proc-" + UnityEngine.Random.Range(1000, 9999)),
                AiMeta = (uint)PlayerPrefs.GetInt("argmax.aiMeta", (int)Presets.Overmind),
                Pilot = (PilotKind)PlayerPrefs.GetInt("argmax.pilot", 0),
                JevKeyEntered = PlayerPrefs.GetString("argmax.jevKey", ""),
            };
            return s;
        }

        public void Save()
        {
            PlayerPrefs.SetString("argmax.server", Server);
            PlayerPrefs.SetString("argmax.customUri", CustomUri);
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

    /// <summary>The Deadnet palette. Your swarm is always cyan; whoever you fight is always hot pink.</summary>
    public static class Palette
    {
        public static readonly Color Background = new Color(0.012f, 0.018f, 0.03f);
        public static readonly Color Me = new Color(0.0f, 0.9f, 1.0f);
        public static readonly Color Foe = new Color(1.0f, 0.18f, 0.44f);
        public static readonly Color Noise = new Color(0.62f, 1.0f, 0.23f);
        public static readonly Color Scrap = new Color(1.0f, 0.69f, 0.0f);
        public static readonly Color Wall = new Color(0.10f, 0.14f, 0.20f);
        public static readonly Color PortalA = new Color(0.18f, 0.49f, 1.0f);
        public static readonly Color PortalB = new Color(1.0f, 0.48f, 0.1f);
        public static readonly Color Well = new Color(0.69f, 0.3f, 1.0f);
        public static readonly Color Goal = new Color(0.95f, 0.95f, 0.45f);
        public static readonly Color White = new Color(0.92f, 0.97f, 1.0f);

        public static Color Seat(int seat, int localSeat)
        {
            if (seat == Rules.NoiseSeat) return Noise;
            return seat == localSeat ? Me : Foe;
        }

        /// <summary>Bigger numbers glow whiter, like 2048 tiles getting hotter.</summary>
        public static Color Tiered(Color c, int tier)
        {
            float t = Mathf.Clamp01((tier - 1) / 10f);
            return Color.Lerp(c, White, t * 0.55f);
        }

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
