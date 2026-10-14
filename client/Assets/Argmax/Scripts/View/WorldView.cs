using System.Collections.Generic;
using Argmax.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace ArgmaxClient
{
    /// <summary>
    /// Draws any World: no GameObject per unit, just instanced SDF quads (1023 per call), interpolated between
    /// simulation ticks. Effects come from the step log (locally) or the server's Fx event table (online).
    /// The ground can show the gradient fields and stigmergy trails that units read instead of pathfinding.
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        public static readonly string[] FieldNames = { "off", "trails (stigmergy)", "beacon gradient", "scrap gradient", "home gradient", "enemy-core gradient" };

        public Camera Cam;
        public int FieldMode;
        public readonly HashSet<uint> Selected = new HashSet<uint>();
        public uint Hovered;
        public struct Drawn { public uint Id; public Vector3 Pos; public byte Seat; public byte Tier; }
        public readonly List<Drawn> Visible = new List<Drawn>();
        public System.Action<float> Shake;
        public bool Sound = true;
        /// <summary>MORPH (raymarched matter) or GRID (flat Bauhaus shapes). Toggle with G.</summary>
        public bool Morph = true;
        public bool MorphAvailable => morph != null && morph.Ready;
        MorphRenderer morph;
        Texture2D fieldDistTex;
        ushort[] fieldDistPx;

        Mesh quad, cube;
        Material spriteMat, glowMat, blockMat, groundMat;
        Batch bodies, glows, rings, lines, blocks, walls, shadows;
        GameObject ground;
        Texture2D fieldTex, occTex;
        Color32[] fieldPx;
        int fieldTick = -1, fieldModeShown = -1;
        Map map;
        World world;
        int lastTick = -1;
        float tickAt;
        float tickSeconds = 0.1f;
        int localSeat;
        readonly Dictionary<uint, Track> tracks = new Dictionary<uint, Track>();
        readonly List<uint> gone = new List<uint>();
        readonly StepLog fxIn = new StepLog();
        readonly List<FxItem> fx = new List<FxItem>();
        VisualElement labelLayer;
        readonly List<Label> labels = new List<Label>();
        int labelsUsed;

        struct Track { public Vector2 From, To; public float Born; }
        struct Spark { public Vector3 P, V; public Color C; public float T0, Life, Size; public int Shape; public float Spin; }
        readonly List<Spark> sparks = new List<Spark>();
        uint sparkSeed = 0x2545F491u;
        struct FxItem { public int Kind; public Vector3 A, B; public Color C; public float T0, Life, Size; public string Text; }

        // ------------------------------------------------------------------ setup

        public void Init(Camera cam, VisualElement labels)
        {
            Cam = cam;
            labelLayer = labels;
            quad = Meshes.FlatQuad();
            cube = Meshes.Cube();
            spriteMat = Make("Argmax/Sprite", BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha);
            glowMat = Make("Argmax/Sprite", BlendMode.SrcAlpha, BlendMode.One);
            blockMat = new Material(LoadShader("Argmax/Block")) { enableInstancing = true };
            bodies = new Batch(spriteMat, quad);
            shadows = new Batch(spriteMat, quad);
            glows = new Batch(glowMat, quad);
            rings = new Batch(glowMat, quad);
            lines = new Batch(glowMat, quad);
            blocks = new Batch(blockMat, cube);
            walls = new Batch(blockMat, cube);
            groundMat = new Material(LoadShader("Argmax/Ground"));
            ground = new GameObject("Ground");
            ground.AddComponent<MeshFilter>().sharedMesh = Meshes.FlatQuad();
            ground.AddComponent<MeshRenderer>().sharedMaterial = groundMat;
            ground.SetActive(false);
            Shader.SetGlobalFloat("_ArgmaxEmission", 1.15f);
            Shader.SetGlobalVector("_ArgmaxEdge", new Vector4(0.925f, 0.894f, 0.827f, 0.5f));
            morph = new MorphRenderer();
        }

        static Shader LoadShader(string name)
        {
            var s = Shader.Find(name);
            if (s == null) Debug.LogError("ARGMAX: shader " + name + " missing (Assets/Argmax/Resources/Argmax/Shaders)");
            return s;
        }

        static Material Make(string shader, BlendMode src, BlendMode dst)
        {
            var m = new Material(LoadShader(shader)) { enableInstancing = true };
            m.SetFloat("_Src", (float)src);
            m.SetFloat("_Dst", (float)dst);
            return m;
        }

        void Rebuild(Map m)
        {
            map = m;
            tracks.Clear();
            fx.Clear();
            sparks.Clear();
            ground.SetActive(true);
            ground.transform.position = new Vector3(m.W * 0.5f, 0f, m.H * 0.5f);
            ground.transform.localScale = new Vector3(m.W + 24, 1, m.H + 24);
            groundMat.SetVector("_MapSize", new Vector4(m.W, m.H, 0, 0));
            fieldTex = new Texture2D(m.W, m.H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            fieldPx = new Color32[m.W * m.H];
            fieldTex.SetPixels32(fieldPx);
            fieldTex.Apply();
            groundMat.SetTexture("_Field", fieldTex);
            fieldTick = -1;
            fieldDistTex = new Texture2D(m.W, m.H, TextureFormat.RHalf, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            fieldDistPx = new ushort[m.W * m.H];
            BakeOcclusion(m);
            morph?.Rebuild(m);
            walls.StaticClear();
            for (int y = 0; y < m.H; y++)
                for (int x = 0; x < m.W; x++)
                {
                    if (m.Cell[m.Idx(x, y)] != Argmax.Sim.Terrain.Wall) continue;
                    bool border = x == 0 || y == 0 || x == m.W - 1 || y == m.H - 1;
                    float h = border ? 0.5f : 0.6f + ((x * 7 + y * 13) % 5) * 0.08f;
                    float v = ((x * 31 + y * 17) % 7) / 7f;
                    var c = Palette.Wall * (0.85f + 0.3f * v);
                    walls.AddStatic(Matrix4x4.TRS(new Vector3(x + 0.5f, h * 0.5f, y + 0.5f), Quaternion.identity, new Vector3(0.96f, h, 0.96f)), c, new Vector4(0, border ? 0.35f : 0.8f + 0.4f * v, 0, 0));
                }
        }

        /// <summary>Soft contact shadows next to walls (R) and goal tiles (G), baked once per map.</summary>
        void BakeOcclusion(Map m)
        {
            occTex = new Texture2D(m.W, m.H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[m.W * m.H];
            for (int y = 0; y < m.H; y++)
                for (int x = 0; x < m.W; x++)
                {
                    int i = m.Idx(x, y);
                    if (m.Cell[i] == Argmax.Sim.Terrain.Wall) { px[i] = new Color32(255, 0, 0, 255); continue; }
                    float near = 0;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (!m.In(nx, ny) || m.Cell[m.Idx(nx, ny)] != Argmax.Sim.Terrain.Wall) continue;
                            near += 1f / (dx * dx + dy * dy + 1f);
                        }
                    px[i] = new Color32((byte)(Mathf.Clamp01(near * 0.45f) * 255), (byte)(m.Goals.Contains(i) ? 255 : 0), 0, 255);
                }
            occTex.SetPixels32(px);
            occTex.Apply(false);
            groundMat.SetTexture("_Occ", occTex);
        }

        float Rand() { sparkSeed ^= sparkSeed << 13; sparkSeed ^= sparkSeed >> 17; sparkSeed ^= sparkSeed << 5; return (sparkSeed & 0xFFFFFF) / 16777216f; }

        static readonly int[] ConfettiShapes = { 0, 1, 7 };

        /// <summary>Geometric confetti: circles, squares and triangles, mostly in the given colour, some bone.</summary>
        void Burst(Vector3 at, Color c, int n, float speed, float life, float size, float up = 0f)
        {
            for (int k = 0; k < n && sparks.Count < 2500; k++)
            {
                float a = Rand() * Mathf.PI * 2f, sp = speed * (0.35f + 0.65f * Rand());
                var col = Rand() < 0.25f ? Palette.Bone : c;
                sparks.Add(new Spark
                {
                    P = at, V = new Vector3(Mathf.Cos(a) * sp, up * (0.5f + Rand()), Mathf.Sin(a) * sp), C = col, T0 = Time.time,
                    Life = life * (0.6f + 0.4f * Rand()), Size = size * (1.4f + 1.2f * Rand()), Shape = ConfettiShapes[(int)(Rand() * 2.999f)], Spin = (Rand() - 0.5f) * 720f,
                });
            }
        }

        void DrawSparks()
        {
            float now = Time.time, dt = Mathf.Min(Time.deltaTime, 0.05f);
            int w = 0;
            for (int i = 0; i < sparks.Count; i++)
            {
                var p = sparks[i];
                float k = (now - p.T0) / p.Life;
                if (k >= 1f) continue;
                p.P += p.V * dt;
                p.V *= 1f - 3.2f * dt;
                p.V.y -= 4f * dt;
                if (p.P.y < 0.03f) { p.P.y = 0.03f; p.V.y = 0; }
                sparks[w++] = p;
                float fade = 1f - k;
                float sz = p.Size * (0.6f + 0.4f * fade);
                bodies.Add(Matrix4x4.TRS(p.P, Quaternion.Euler(0, p.Spin * (now - p.T0), 0), new Vector3(sz, 1f, sz)), WithA(p.C, Mathf.Min(1f, fade * 1.6f)), new Vector4(p.Shape, 0.06f, 0, 0));
            }
            sparks.RemoveRange(w, sparks.Count - w);
        }

        /// <summary>Acknowledge an order on the ground: white for MOVE, hot for ATTACK.</summary>
        public void PingOrder(Vector3 at, bool attack)
        {
            fx.Add(new FxItem { Kind = 1, A = new Vector3(Mathf.Floor(at.x) + 0.5f, 0.05f, Mathf.Floor(at.z) + 0.5f), C = attack ? Palette.Foe : Palette.White, T0 = Time.time, Life = 0.45f, Size = 1.6f });
            Audio.Play(attack ? Sfx.Attack : Sfx.Move);
        }

        public void Hide()
        {
            if (ground != null) ground.SetActive(false);
            world = null; map = null; lastTick = -1;
            Visible.Clear();
            HideLabels();
        }

        // ------------------------------------------------------------------ per frame

        public void Render(ISession s)
        {
            var w = s?.World;
            if (w == null) { Hide(); return; }
            localSeat = s.LocalSeat;
            if (w.Map != map) Rebuild(w.Map);
            tickSeconds = Mathf.Max(0.01f, s.TickSeconds);
            if (w != world || w.Tick != lastTick) Retarget(w);
            if (s.TakeFx(fxIn)) { SpawnFx(fxIn); fxIn.Clear(); }
            groundMat.SetFloat("_Pulse", Time.time);
            UpdateField(w);
            labelsUsed = 0;
            float a = Mathf.Clamp01((Time.time - tickAt) / tickSeconds);
            bool morphOn = Morph && MorphAvailable;
            ground.SetActive(!morphOn);
            DrawStatic(w);
            DrawMotes(w, a, morphOn);
            DrawFx();
            DrawSparks();
            if (morphOn) morph.Draw(w, Visible, Selected, localSeat, Cam, fieldTex, fieldDistTex, FieldMode != 0);
            else walls.FlushStatic();
            blocks.Flush(); shadows.Flush(); glows.Flush(); bodies.Flush(); rings.Flush(); lines.Flush();
            for (int i = labelsUsed; i < labels.Count; i++) labels[i].style.display = DisplayStyle.None;
        }

        void Retarget(World w)
        {
            float now = Time.time;
            float prevA = Mathf.Clamp01((now - tickAt) / tickSeconds);
            if (w != world) { tracks.Clear(); world = w; prevA = 1; }
            gone.Clear();
            foreach (var id in tracks.Keys) gone.Add(id);
            for (int i = 0; i < w.MoteCount; i++)
            {
                ref var m = ref w.Motes[i];
                var to = new Vector2(m.X + 0.5f, m.Y + 0.5f);
                if (tracks.TryGetValue(m.Id, out var t))
                {
                    var cur = Vector2.Lerp(t.From, t.To, Ease(prevA));
                    t.From = (cur - to).sqrMagnitude > 9f ? to : cur;
                    t.To = to;
                    tracks[m.Id] = t;
                    gone.Remove(m.Id);
                }
                else tracks[m.Id] = new Track { From = to, To = to, Born = now };
            }
            foreach (var id in gone) { tracks.Remove(id); Selected.Remove(id); }
            lastTick = w.Tick;
            tickAt = now;
        }

        static float Ease(float t) => t * t * (3f - 2f * t);

        static Vector3 Cell(int x, int y, float h = 0f) => new Vector3(x + 0.5f, h, y + 0.5f);
        Vector3 CellIdx(int c, float h = 0f) => Cell(c % map.W, c / map.W, h);

        void DrawStatic(World w)
        {
            float t = Time.time;
            // cores
            for (int s = 0; s < 2; s++)
            {
                if (!map.HasCore[s]) continue;
                var seat = w.Seats[s];
                var col = Palette.Seat(s, localSeat);
                float hp = seat.HasCore ? Mathf.Clamp01(seat.CoreHp / (float)Rules.CoreHp) : 0f;
                bool alive = seat.Alive || seat.Kind == SeatKind.Empty;
                var c = alive ? Color.Lerp(col * 0.35f, col, 0.35f + 0.65f * hp) : col * 0.15f;
                var p = Cell(map.CoreX[s], map.CoreY[s]);
                // low and wide so the core never hides the units fighting behind it
                blocks.Add(Matrix4x4.TRS(p + Vector3.up * 0.35f, Quaternion.Euler(0, t * 12f * (s == 0 ? 1 : -1), 0), new Vector3(2.5f, 0.7f, 2.5f)), c, new Vector4(alive ? 0.35f : 0f, alive ? 2.2f : 0.3f, 0, 0));
                blocks.Add(Matrix4x4.TRS(p + Vector3.up * 0.95f, Quaternion.Euler(0, 45f + t * 30f, 0), Vector3.one * (0.55f + 0.1f * Mathf.Sin(t * 3f))), Palette.White * (alive ? 1f : 0.2f), new Vector4(0.9f, 1.5f, 0, 0));
                if (alive) Ring(p + Vector3.up * 0.04f, 3.6f + 0.25f * Mathf.Sin(t * 2.4f + s), WithA(col, 0.35f * hp + 0.1f), 0.035f);
                if (alive) Sprite(glows, p + Vector3.up * 0.02f, 7.5f + 0.4f * Mathf.Sin(t * 2f), WithA(col, 0.35f * hp + 0.1f), 4);
            }
            // scrap
            foreach (var n in w.Scrap)
            {
                if (n.Amount <= 0) continue;
                float k = Mathf.Sqrt(Mathf.Min(n.Amount, 900) / 900f);
                float sz = 0.35f + 0.45f * k;
                var p = Cell(n.X, n.Y);
                blocks.Add(Matrix4x4.TRS(p + Vector3.up * sz * 0.6f, Quaternion.Euler(0, (n.X * 37 + n.Y * 11) % 90, 0), new Vector3(sz, sz * 1.2f, sz)), Palette.Scrap, new Vector4(0.7f, 0.8f, 0, 0));
                Sprite(glows, p + Vector3.up * 0.01f, 2.6f, WithA(Palette.Scrap, 0.22f), 4);
            }
            // portals and wells
            for (int i = 0; i < map.Portals.Count; i++)
            {
                var c = (i / 2) % 2 == 0 ? (i % 2 == 0 ? Palette.PortalA : Palette.PortalB) : (i % 2 == 0 ? Palette.PortalB : Palette.PortalA);
                var p = CellIdx(map.Portals[i], 0.03f);
                Ring(p, 1.25f + 0.1f * Mathf.Sin(t * 4f + i), WithA(c, 0.95f), 0.12f);
                Sprite(glows, p, 2.4f, WithA(c, 0.5f), 4);
            }
            foreach (var wl in map.Wells)
            {
                var p = CellIdx(wl, 0.03f);
                Ring(p, 1.6f + 0.3f * Mathf.Sin(t * 1.7f), WithA(Palette.Well, 0.6f), 0.08f);
                Ring(p, 0.9f + 0.2f * Mathf.Sin(t * 2.3f + 1f), WithA(Palette.Well, 0.8f), 0.1f);
                Sprite(glows, p, 3.2f, WithA(Palette.Well, 0.35f), 4);
            }
            foreach (var g in map.Goals) Sprite(glows, CellIdx(g, 0.01f), 1.0f, WithA(Palette.Goal, 0.16f + 0.06f * Mathf.Sin(t * 3f)), 1);
            // beacons: a sonar ping where the gradient starts
            for (int s = 0; s < 2; s++)
            {
                var seat = w.Seats[s];
                if (!seat.BeaconOn) continue;
                var c = Palette.Seat(s, localSeat);
                var p = Cell(seat.BeaconX, seat.BeaconY, 0.05f);
                float ph = (t * 0.8f + s * 0.5f) % 1f;
                Ring(p, 0.6f + ph * 5f, WithA(c, (1f - ph) * 0.6f), 0.04f);
                Sprite(bodies, p, 0.8f, WithA(c, 0.9f), 2, 0.4f);
                Sprite(glows, p, 2f, WithA(c, 0.5f), 4);
            }
        }

        void DrawMotes(World w, float alpha, bool morphOn)
        {
            Visible.Clear();
            float now = Time.time;
            float e = Ease(alpha);
            int orderLines = 0;
            for (int i = 0; i < w.MoteCount; i++)
            {
                ref var m = ref w.Motes[i];
                if (!tracks.TryGetValue(m.Id, out var tr)) continue;
                var p2 = Vector2.Lerp(tr.From, tr.To, e);
                float pop = Mathf.Clamp01((now - tr.Born) / 0.25f);
                float size = (0.46f + 0.06f * m.Tier + 0.022f * m.Tier * m.Tier) * (0.4f + 0.6f * pop); // a 512 should loom
                var pos = new Vector3(p2.x, 0.04f + m.Tier * 0.003f, p2.y);
                var baseCol = Palette.Seat(m.Seat, localSeat);
                var col = Palette.Tiered(baseCol, m.Tier);
                float hp = Mathf.Clamp01(m.Hp / (float)Rules.MaxHp(m.Tier));
                col *= 0.55f + 0.45f * hp;
                col.a = 1f;
                // Kandinsky: you are circles, they are squares, the Noise is triangles
                int shape = m.Seat == Rules.NoiseSeat ? 7 : m.Seat == localSeat ? 0 : 1;
                bool fired = m.Has(MoteFlags.Fired);
                if (!morphOn)
                {
                    // a hard cut-paper shadow, offset away from the light
                    shadows.Add(Matrix4x4.TRS(pos + new Vector3(size * 0.16f, -0.01f, -size * 0.16f), Quaternion.identity, new Vector3(size, 1f, size)), new Color(0, 0, 0, 0.55f), new Vector4(shape, 0.06f, 0, 0));
                    if (fired) Sprite(glows, pos - Vector3.up * 0.005f, size * 2.2f, WithA(baseCol, 0.35f), 4);
                    bodies.Add(Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(size, 1f, size)), col, new Vector4(shape, 0.06f, fired ? 0.25f : 0f, m.Tier));
                    if (m.Carry > 0) Sprite(bodies, pos + new Vector3(size * 0.38f, 0.01f, size * 0.38f), 0.16f, Palette.Scrap, 1);
                }
                else if (m.Carry > 0) Sprite(bodies, pos + new Vector3(0, 0.9f, 0), 0.18f, Palette.Scrap, 1);
                bool sel = Selected.Contains(m.Id);
                if (sel) Ring(pos, size + 0.34f, WithA(Palette.Noise, 0.95f), 0.06f);
                else if (m.Id == Hovered) Ring(pos, size + 0.3f, WithA(Palette.Bone, 0.5f), 0.045f);
                if (sel && m.Has(MoteFlags.HasOrder) && orderLines++ < 48)
                    Line(pos, Cell(m.Ox, m.Oy, 0.03f), WithA(m.Override > 0 ? Palette.White : baseCol, 0.18f), 0.05f);
                if (m.Tier >= 4) Label(pos + Vector3.up * 0.2f, Rules.Value(m.Tier).ToString(), m.Tier >= 7 ? Palette.White : col, 10 + m.Tier);
                Visible.Add(new Drawn { Id = m.Id, Pos = pos, Seat = m.Seat, Tier = m.Tier });
            }
        }

        // ------------------------------------------------------------------ effects

        void SpawnFx(StepLog l)
        {
            float t = Time.time;
            foreach (var s in l.Shots)
            {
                int from = s & 0xFFF, to = (s >> 12) & 0xFFF, seat = (s >> 24) & 3;
                bool infect = (s & (1 << 27)) != 0, core = (s & (1 << 28)) != 0, splash = (s & (1 << 26)) != 0;
                var c = infect ? Palette.Noise : Palette.Seat(seat, localSeat);
                fx.Add(new FxItem { Kind = 0, A = CellIdx(from, 0.2f), B = CellIdx(to, 0.2f), C = c, T0 = t, Life = core ? 0.22f : 0.13f, Size = core ? 0.16f : splash ? 0.12f : 0.07f });
                if (splash) { fx.Add(new FxItem { Kind = 1, A = CellIdx(to, 0.05f), C = c, T0 = t, Life = 0.25f, Size = 2.6f }); Burst(CellIdx(to, 0.15f), c, 6, 3f, 0.35f, 0.12f, 1f); }
                if (core) { Burst(CellIdx(to, 0.4f), c, 5, 4f, 0.4f, 0.12f, 2f); Shake?.Invoke(0.08f); if (Sound) Audio.Play(Sfx.CoreHit, 0.45f); }
                else if (Sound) Audio.Play(Sfx.Shot, 0.12f, 0.9f + 0.25f * Rand());
            }
            foreach (var d in l.Deaths)
            {
                int cell = d & 0xFFF, seat = (d >> 12) & 3, tier = (d >> 14) & 0xF;
                var dc = Palette.Seat(seat, localSeat);
                fx.Add(new FxItem { Kind = 1, A = CellIdx(cell, 0.05f), C = dc, T0 = t, Life = 0.4f, Size = 1.2f + tier * 0.35f });
                Burst(CellIdx(cell, 0.15f), dc, 6 + tier * 3, 2.5f + tier * 0.6f, 0.45f + tier * 0.05f, 0.08f + tier * 0.015f, 1.5f);
                if (tier >= 5) Shake?.Invoke(0.04f * tier);
                if (Sound) Audio.Play(Sfx.Death, 0.25f + 0.05f * tier, 1.15f - 0.05f * tier);
            }
            foreach (var mg in l.Merges)
            {
                int cell = mg & 0xFFF, tier = (mg >> 12) & 0xF;
                var p = CellIdx(cell, 0.05f);
                fx.Add(new FxItem { Kind = 1, A = p, C = Palette.White, T0 = t, Life = 0.55f, Size = 2.2f + tier * 0.3f });
                fx.Add(new FxItem { Kind = 3, A = p + Vector3.up * 0.4f, C = Palette.White, T0 = t, Life = 0.8f, Text = Rules.Value(tier).ToString() });
                Burst(p + Vector3.up * 0.1f, Palette.White, 8 + tier * 2, 1.6f + tier * 0.25f, 0.6f, 0.07f + tier * 0.01f, 3f);
                if (tier >= 6) Shake?.Invoke(0.03f * tier);
                if (Sound) Audio.Merge(tier);
            }
            foreach (var cv in l.Converts)
            {
                fx.Add(new FxItem { Kind = 1, A = CellIdx(cv & 0xFFF, 0.05f), C = Palette.Noise, T0 = t, Life = 0.6f, Size = 2f });
                Burst(CellIdx(cv & 0xFFF, 0.2f), Palette.Noise, 10, 2f, 0.6f, 0.08f, 2f);
                if (Sound) Audio.Play(Sfx.Convert, 0.4f);
            }
            foreach (var tp in l.Teleports)
            {
                fx.Add(new FxItem { Kind = 1, A = CellIdx(tp & 0xFFF, 0.05f), C = Palette.PortalA, T0 = t, Life = 0.35f, Size = 1.8f });
                fx.Add(new FxItem { Kind = 1, A = CellIdx((tp >> 12) & 0xFFF, 0.05f), C = Palette.PortalB, T0 = t, Life = 0.35f, Size = 1.8f });
            }
            foreach (var sp in l.Spawns)
            {
                fx.Add(new FxItem { Kind = 1, A = CellIdx(sp & 0xFFF, 0.04f), C = Palette.Seat((sp >> 12) & 3, localSeat), T0 = t, Life = 0.3f, Size = 1f });
                if (Sound && ((sp >> 12) & 3) == localSeat) Audio.Play(Sfx.Spawn, 0.2f);
            }
            if (fx.Count > 3000) fx.RemoveRange(0, fx.Count - 3000);
        }

        void DrawFx()
        {
            float now = Time.time;
            int w = 0;
            for (int i = 0; i < fx.Count; i++)
            {
                var f = fx[i];
                float k = (now - f.T0) / f.Life;
                if (k >= 1f) continue;
                fx[w++] = f;
                switch (f.Kind)
                {
                    case 0: Line(f.A, f.B, WithA(f.C, 0.9f * (1f - k)), f.Size); break;
                    case 1: Ring(f.A, 0.3f + f.Size * k, WithA(f.C, 0.8f * (1f - k)), 0.06f + 0.05f * (1f - k)); break;
                    case 3: Label(f.A + Vector3.up * k * 1.2f, f.Text, WithA(f.C, 1f - k * k), 18); break;
                }
            }
            fx.RemoveRange(w, fx.Count - w);
        }

        // ------------------------------------------------------------------ fields

        void UpdateField(World w)
        {
            if (FieldMode == fieldModeShown && (FieldMode == 0 || w.Tick == fieldTick)) return;
            fieldModeShown = FieldMode;
            fieldTick = w.Tick;
            var px = fieldPx;
            for (int i = 0; i < px.Length; i++) px[i] = default;
            int seat = localSeat < 2 ? localSeat : 0;
            if (FieldMode == 1)
            {
                var mine = w.Trails[seat]; var theirs = w.Trails[1 - seat];
                var a = Palette.Me; var b = Palette.Foe;
                for (int i = 0; i < px.Length; i++)
                {
                    float m = mine[i] / 255f, n = theirs[i] / 255f;
                    if (m + n <= 0) continue;
                    var c = a * m + b * n;
                    px[i] = new Color32((byte)(Mathf.Clamp01(c.r) * 255), (byte)(Mathf.Clamp01(c.g) * 255), (byte)(Mathf.Clamp01(c.b) * 255), (byte)(Mathf.Clamp01(m + n) * 220));
                }
            }
            else if (FieldMode >= 2)
            {
                var ch = FieldMode == 2 ? Channel.Beacon : FieldMode == 3 ? Channel.Scrap : FieldMode == 4 ? Channel.Home : Channel.Core;
                var f = Sim.Field(w, ch, seat);
                var col = FieldMode == 3 ? Palette.Noise : FieldMode == 5 ? Palette.Foe : Palette.Me;
                if (f != null && fieldDistPx != null)
                {
                    for (int i = 0; i < fieldDistPx.Length; i++) fieldDistPx[i] = Mathf.FloatToHalf(f[i] == Fields.Unreachable ? 1f : Mathf.Min(f[i], 64) / 64f);
                    fieldDistTex.SetPixelData(fieldDistPx, 0);
                    fieldDistTex.Apply(false);
                }
                if (f != null)
                    for (int i = 0; i < px.Length; i++)
                    {
                        int d = f[i];
                        if (d == Fields.Unreachable) continue;
                        float near = Mathf.Clamp01(1f - d / 48f);
                        float iso = d % 4 == 0 ? 0.35f : 0f;
                        float a = near * near * 0.75f + iso * near + 0.05f;
                        px[i] = new Color32((byte)(col.r * 255), (byte)(col.g * 255), (byte)(col.b * 255), (byte)(Mathf.Clamp01(a) * 255));
                    }
            }
            fieldTex.SetPixels32(px);
            fieldTex.Apply(false);
        }

        // ------------------------------------------------------------------ primitives

        static Color WithA(Color c, float a) { c.a = a; return c; }

        void Sprite(Batch b, Vector3 p, float size, Color c, int shape, float boost = 0f) =>
            b.Add(Matrix4x4.TRS(p, Quaternion.identity, new Vector3(size, 1f, size)), c, new Vector4(shape, 0.06f, boost, 0));

        void Ring(Vector3 p, float size, Color c, float thickness) =>
            rings.Add(Matrix4x4.TRS(p, Quaternion.identity, new Vector3(size, 1f, size)), c, new Vector4(3, thickness, 0, 0));

        void Line(Vector3 a, Vector3 b, Color c, float width)
        {
            var d = b - a;
            d.y = 0;
            float len = d.magnitude;
            if (len < 0.01f) return;
            var rot = Quaternion.FromToRotation(Vector3.right, d / len);
            lines.Add(Matrix4x4.TRS((a + b) * 0.5f, rot, new Vector3(len, 1f, width * 2f)), c, new Vector4(5, 0, 0, 0));
        }

        void Label(Vector3 world, string text, Color c, int size)
        {
            if (labelLayer == null || labelLayer.panel == null || Cam == null) return;
            var vp = Cam.WorldToViewportPoint(world);
            if (vp.z <= 0 || vp.x < -0.05f || vp.x > 1.05f || vp.y < -0.05f || vp.y > 1.05f) return;
            if (labelsUsed >= 400) return;
            Label l;
            if (labelsUsed < labels.Count) l = labels[labelsUsed];
            else
            {
                l = new Label();
                l.AddToClassList("world-label");
                l.pickingMode = PickingMode.Ignore;
                l.style.position = Position.Absolute;
                l.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
                labelLayer.Add(l);
                labels.Add(l);
            }
            labelsUsed++;
            // viewport -> panel: correct whether the camera renders to the screen or to a texture
            var root = labelLayer.panel.visualTree.layout;
            l.style.display = DisplayStyle.Flex;
            l.style.left = vp.x * root.width;
            l.style.top = (1f - vp.y) * root.height;
            l.style.color = c;
            l.style.fontSize = size;
            if (l.text != text) l.text = text;
        }

        void HideLabels() { foreach (var l in labels) l.style.display = DisplayStyle.None; }

        /// <summary>Accumulates instances and submits them in 1023-instance calls with per-instance color/params.</summary>
        sealed class Batch
        {
            const int Max = 1023;
            readonly Material mat;
            readonly Mesh mesh;
            readonly Matrix4x4[] m = new Matrix4x4[Max];
            readonly Vector4[] c = new Vector4[Max];
            readonly Vector4[] p = new Vector4[Max];
            readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            int n;
            readonly List<Matrix4x4> sm = new List<Matrix4x4>();
            readonly List<Vector4> sc = new List<Vector4>(), sp = new List<Vector4>();

            public Batch(Material mat, Mesh mesh) { this.mat = mat; this.mesh = mesh; }

            public void Add(Matrix4x4 matrix, Color color, Vector4 prm)
            {
                m[n] = matrix; c[n] = color; p[n] = prm;
                if (++n == Max) Flush();
            }

            public void Flush()
            {
                if (n == 0 || mat == null) { n = 0; return; }
                mpb.SetVectorArray("_Color", c);
                mpb.SetVectorArray("_Params", p);
                var rp = new RenderParams(mat)
                {
                    matProps = mpb,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    worldBounds = new Bounds(new Vector3(32, 0, 32), new Vector3(512, 64, 512)),
                };
                Graphics.RenderMeshInstanced(rp, mesh, 0, m, n);
                n = 0;
            }

            public void StaticClear() { sm.Clear(); sc.Clear(); sp.Clear(); }
            public void AddStatic(Matrix4x4 matrix, Color color, Vector4 prm) { sm.Add(matrix); sc.Add(color); sp.Add(prm); }
            public void FlushStatic()
            {
                for (int i = 0; i < sm.Count; i++) Add(sm[i], sc[i], sp[i]);
                Flush();
            }
        }
    }

    /// <summary>Procedural meshes so the project needs no model assets.</summary>
    public static class Meshes
    {
        public static Mesh FlatQuad()
        {
            var mesh = new Mesh { name = "FlatQuad" };
            mesh.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1, 0.1f, 1));
            return mesh;
        }

        public static Mesh Cube()
        {
            var v = new List<Vector3>(); var nrm = new List<Vector3>(); var tri = new List<int>();
            void Face(Vector3 n, Vector3 u, Vector3 r)
            {
                int b = v.Count;
                var c = n * 0.5f;
                v.Add(c - u * 0.5f - r * 0.5f); v.Add(c - u * 0.5f + r * 0.5f); v.Add(c + u * 0.5f + r * 0.5f); v.Add(c + u * 0.5f - r * 0.5f);
                for (int i = 0; i < 4; i++) nrm.Add(n);
                tri.Add(b); tri.Add(b + 2); tri.Add(b + 1); tri.Add(b); tri.Add(b + 3); tri.Add(b + 2);
            }
            Face(Vector3.up, Vector3.forward, Vector3.right);
            Face(Vector3.down, Vector3.back, Vector3.right);
            Face(Vector3.forward, Vector3.up, Vector3.left);
            Face(Vector3.back, Vector3.up, Vector3.right);
            Face(Vector3.right, Vector3.up, Vector3.forward);
            Face(Vector3.left, Vector3.up, Vector3.back);
            var mesh = new Mesh { name = "Cube" };
            mesh.SetVertices(v); mesh.SetNormals(nrm); mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
