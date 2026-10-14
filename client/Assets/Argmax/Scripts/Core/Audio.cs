using System;
using UnityEngine;

namespace ArgmaxClient
{
    public enum Sfx { Click, Type, Select, Move, Attack, Shot, Death, Merge, Spawn, Run, Victory, Defeat, Star, Error, Tick, CoreHit, Beacon, Convert, Count }

    /// <summary>
    /// Every sound in the game is synthesized at startup: no audio assets, nothing to license, and it works in
    /// a browser build (pre-rendered clips, no OnAudioFilterRead). The music is a generative ambient loop in
    /// A minor whose notes wrap around the loop boundary, so it repeats without a seam.
    /// </summary>
    public static class Audio
    {
        const int Rate = 22050;
        static AudioSource[] pool;
        static AudioSource music;
        static AudioClip[] clips;
        static float[] last;
        static int next;
        static float[] musicData;
        static int musicDone, musicLen;
        static bool ready;

        public static float Master = 0.8f, Effects = 0.8f, Music = 0.45f;
        public static bool Muted;

        static readonly float[] MinGap =
        {
            0.03f, 0.025f, 0.05f, 0.06f, 0.06f, 0.035f, 0.05f, 0.04f, 0.06f, 0.1f, 0.5f, 0.5f, 0.05f, 0.15f, 0.04f, 0.12f, 0.2f, 0.08f,
        };

        public static void Init(GameObject host)
        {
            if (ready) return;
            pool = new AudioSource[16];
            for (int i = 0; i < pool.Length; i++) { pool[i] = host.AddComponent<AudioSource>(); pool[i].playOnAwake = false; pool[i].spatialBlend = 0f; }
            music = host.AddComponent<AudioSource>();
            music.loop = true; music.playOnAwake = false; music.spatialBlend = 0f;
            clips = new AudioClip[(int)Sfx.Count];
            last = new float[(int)Sfx.Count];
            for (int i = 0; i < clips.Length; i++) { clips[i] = Make((Sfx)i); last[i] = -10f; }
            musicLen = Rate * 48;
            musicData = new float[musicLen];
            musicDone = 0;
            ready = true;
        }

        /// <summary>Renders the music a few seconds per frame, then starts it. Call every frame.</summary>
        public static void Pump()
        {
            if (!ready) return;
            if (musicDone < Notes.Length)
            {
#if UNITY_WEBGL
                int end = Math.Min(Notes.Length, musicDone + 2);
#else
                int end = Math.Min(Notes.Length, musicDone + 6);
#endif
                for (; musicDone < end; musicDone++) RenderNote(Notes[musicDone]);
                if (musicDone == Notes.Length)
                {
                    Normalize(musicData, 0.6f);
                    var clip = AudioClip.Create("argmax-ambient", musicLen, 1, Rate, false);
                    clip.SetData(musicData, 0);
                    musicData = null;
                    music.clip = clip;
                    music.Play();
                }
            }
            if (music != null) music.volume = Muted ? 0f : Master * Music;
        }

        public static void Play(Sfx s, float volume = 1f, float pitch = 1f)
        {
            if (!ready || Muted) return;
            int i = (int)s;
            float now = Time.unscaledTime;
            if (now - last[i] < MinGap[i]) return;
            last[i] = now;
            var src = pool[next++ % pool.Length];
            src.pitch = pitch;
            src.PlayOneShot(clips[i], Mathf.Clamp01(volume * Effects * Master));
        }

        /// <summary>Merges climb a pentatonic scale: the bigger the number, the higher the chime.</summary>
        public static void Merge(int tier)
        {
            int[] penta = { 0, 2, 4, 7, 9, 12, 14, 16, 19, 21, 24 };
            int st = penta[Mathf.Clamp(tier - 2, 0, penta.Length - 1)];
            Play(Sfx.Merge, 0.55f + 0.04f * tier, Mathf.Pow(2f, st / 12f));
        }

        // ------------------------------------------------------------------ synthesis

        static float Env(float t, float a, float d) => t < a ? t / a : Mathf.Exp(-(t - a) / d);
        static float Noise(ref uint s) { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFF) / 32768f - 1f; }

        static AudioClip Make(Sfx s)
        {
            float len;
            switch (s)
            {
                case Sfx.Victory: len = 1.6f; break;
                case Sfx.Defeat: len = 1.4f; break;
                case Sfx.Beacon: len = 0.9f; break;
                case Sfx.Merge: len = 0.7f; break;
                case Sfx.Run: len = 0.35f; break;
                case Sfx.Death: case Sfx.Star: case Sfx.Error: case Sfx.CoreHit: case Sfx.Convert: len = 0.4f; break;
                default: len = 0.14f; break;
            }
            int n = (int)(len * Rate);
            var d = new float[n];
            uint seed = 0x9E3779B9u + (uint)s * 7919u;
            double ph = 0, ph2 = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float v = 0;
                switch (s)
                {
                    case Sfx.Click: v = Mathf.Sin(2 * Mathf.PI * 1700 * t) * Env(t, 0.001f, 0.012f) * 0.5f; break;
                    case Sfx.Type: v = (Mathf.Sin(2 * Mathf.PI * 2600 * t) > 0 ? 1 : -1) * Env(t, 0.0005f, 0.006f) * 0.18f; break;
                    case Sfx.Select: v = (Mathf.Sin(2 * Mathf.PI * 880 * t) + 0.6f * Mathf.Sin(2 * Mathf.PI * 1320 * t)) * Env(t, 0.002f, 0.03f) * 0.35f; break;
                    case Sfx.Move:
                        ph += 2 * Math.PI * (600 + 900 * t) / Rate;
                        v = (float)Math.Sin(ph) * Env(t, 0.003f, 0.04f) * 0.35f; break;
                    case Sfx.Attack:
                        ph += 2 * Math.PI * (300 - 600 * t) / Rate;
                        v = ((float)(ph / Math.PI % 2) - 1f) * Env(t, 0.002f, 0.05f) * 0.3f + Noise(ref seed) * Env(t, 0.001f, 0.02f) * 0.1f; break;
                    case Sfx.Shot:
                        ph += 2 * Math.PI * (2400 * Mathf.Exp(-t * 40) + 300) / Rate;
                        v = (float)Math.Sin(ph) * Env(t, 0.001f, 0.02f) * 0.22f; break;
                    case Sfx.Death:
                        ph += 2 * Math.PI * (320 * Mathf.Exp(-t * 9) + 50) / Rate;
                        v = (float)Math.Sin(ph) * Env(t, 0.002f, 0.12f) * 0.4f + Noise(ref seed) * Env(t, 0.001f, 0.07f) * 0.25f; break;
                    case Sfx.Merge:
                        v = (Mathf.Sin(2 * Mathf.PI * 660 * t) + 0.5f * Mathf.Sin(2 * Mathf.PI * 1320 * t) + 0.25f * Mathf.Sin(2 * Mathf.PI * 1980.5f * t)) * Env(t, 0.003f, 0.22f) * 0.3f; break;
                    case Sfx.Spawn: v = Mathf.Sin(2 * Mathf.PI * 520 * t) * Env(t, 0.004f, 0.03f) * 0.25f; break;
                    case Sfx.Run:
                    {
                        int step = Mathf.Min(2, (int)(t / 0.08f));
                        float f = 440f * Mathf.Pow(2f, new[] { 0, 4, 7 }[step] / 12f);
                        float tl = t - step * 0.08f;
                        v = Mathf.Sin(2 * Mathf.PI * f * t) * Env(tl, 0.003f, 0.06f) * 0.35f; break;
                    }
                    case Sfx.Victory:
                    {
                        int[] seq = { 0, 4, 7, 12, 16 };
                        for (int k = 0; k < seq.Length; k++)
                        {
                            float tk = t - k * 0.13f;
                            if (tk < 0) continue;
                            float f = 523.25f * Mathf.Pow(2f, seq[k] / 12f);
                            v += (Mathf.Sin(2 * Mathf.PI * f * t) + 0.3f * Mathf.Sin(4 * Mathf.PI * f * t)) * Env(tk, 0.004f, 0.35f) * 0.2f;
                        }
                        break;
                    }
                    case Sfx.Defeat:
                    {
                        int[] seq = { 7, 3, 0, -5 };
                        for (int k = 0; k < seq.Length; k++)
                        {
                            float tk = t - k * 0.22f;
                            if (tk < 0) continue;
                            float f = 293.66f * Mathf.Pow(2f, seq[k] / 12f);
                            v += Mathf.Sin(2 * Mathf.PI * f * t + 0.6f * Mathf.Sin(2 * Mathf.PI * 3 * t)) * Env(tk, 0.01f, 0.3f) * 0.22f;
                        }
                        break;
                    }
                    case Sfx.Star: v = (Mathf.Sin(2 * Mathf.PI * 1046.5f * t) + 0.4f * Mathf.Sin(2 * Mathf.PI * 2093f * t)) * Env(t, 0.002f, 0.12f) * 0.3f; break;
                    case Sfx.Error: v = (Mathf.Sin(2 * Mathf.PI * 110 * t) > 0 ? 1 : -1) * Env(t, 0.005f, 0.1f) * 0.18f; break;
                    case Sfx.Tick: v = Mathf.Sin(2 * Mathf.PI * 1200 * t) * Env(t, 0.001f, 0.01f) * 0.3f; break;
                    case Sfx.CoreHit:
                        ph += 2 * Math.PI * (90 * Mathf.Exp(-t * 6) + 40) / Rate;
                        v = (float)Math.Sin(ph) * Env(t, 0.002f, 0.15f) * 0.6f + Noise(ref seed) * Env(t, 0.001f, 0.03f) * 0.2f; break;
                    case Sfx.Beacon:
                        v = Mathf.Sin(2 * Mathf.PI * 1400 * t) * (Env(t, 0.002f, 0.12f) + 0.35f * Env(Mathf.Max(0, t - 0.25f), 0.002f, 0.12f) * (t > 0.25f ? 1 : 0)) * 0.3f; break;
                    case Sfx.Convert:
                        ph += 2 * Math.PI * (200 + 1200 * t) / Rate;
                        ph2 += 2 * Math.PI * (206 + 1210 * t) / Rate;
                        v = ((float)Math.Sin(ph) + (float)Math.Sin(ph2)) * Env(t, 0.01f, 0.15f) * 0.2f; break;
                }
                d[i] = v;
            }
            var clip = AudioClip.Create("sfx-" + s, n, 1, Rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        // ------------------------------------------------------------------ music

        struct Note { public float At, Len, Freq, Amp; public int Voice; }
        static readonly Note[] Notes = Compose();

        static Note[] Compose()
        {
            // Am  F  C  G, eight seconds each, twice: pads, a sub, and a sparse arpeggio
            var list = new System.Collections.Generic.List<Note>();
            int[][] chords = { new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 48, 52, 55 }, new[] { 55, 59, 62 } };
            float Hz(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);
            uint seed = 12345;
            for (int bar = 0; bar < 6; bar++)
            {
                var ch = chords[bar % 4];
                float t0 = bar * 8f;
                foreach (var m in ch) list.Add(new Note { At = t0, Len = 9.5f, Freq = Hz(m), Amp = 0.12f, Voice = 0 });
                list.Add(new Note { At = t0, Len = 8.5f, Freq = Hz(ch[0] - 24), Amp = 0.16f, Voice = 1 });
                for (int k = 0; k < 16; k++)
                {
                    seed = seed * 1664525u + 1013904223u;
                    if ((seed >> 28) < 6) continue;
                    int m = ch[(int)((seed >> 20) % 3)] + 12 * (1 + (int)((seed >> 16) & 1));
                    list.Add(new Note { At = t0 + k * 0.5f, Len = 1.6f, Freq = Hz(m), Amp = 0.05f, Voice = 2 });
                }
            }
            return list.ToArray();
        }

        static void RenderNote(Note n)
        {
            int start = (int)(n.At * Rate), len = (int)(n.Len * Rate);
            var d = musicData;
            double dt = 1.0 / Rate;
            for (int i = 0; i < len; i++)
            {
                double t = i * dt;
                float v;
                if (n.Voice == 0)
                {
                    // pad: three detuned sines, slow swell and release
                    float env = Mathf.Clamp01((float)t / 2.5f) * Mathf.Clamp01((n.Len - (float)t) / 3f);
                    v = (float)(Math.Sin(2 * Math.PI * n.Freq * t) + Math.Sin(2 * Math.PI * n.Freq * 1.003 * t) * 0.7 + Math.Sin(2 * Math.PI * n.Freq * 0.997 * t) * 0.7) * env;
                }
                else if (n.Voice == 1)
                {
                    float env = Mathf.Clamp01((float)t / 1.5f) * Mathf.Clamp01((n.Len - (float)t) / 2f);
                    v = (float)Math.Sin(2 * Math.PI * n.Freq * t) * env;
                }
                else
                {
                    // pluck: decaying sine with a soft second harmonic
                    float env = Mathf.Exp(-(float)t * 3.2f) * Mathf.Clamp01((float)t * 400f);
                    v = (float)(Math.Sin(2 * Math.PI * n.Freq * t) + 0.25 * Math.Sin(4 * Math.PI * n.Freq * t)) * env;
                }
                int j = (start + i) % musicLen;
                d[j] += v * n.Amp;
            }
        }

        static void Normalize(float[] d, float peak)
        {
            float m = 1e-4f;
            for (int i = 0; i < d.Length; i++) m = Mathf.Max(m, Mathf.Abs(d[i]));
            float k = peak / m;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }
    }
}
