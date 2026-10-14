using System.Collections.Generic;
using Argmax.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArgmaxClient
{
    /// <summary>
    /// MORPH mode: the world as signed distance fields, raymarched in one full-screen pass (Argmax/Morph).
    /// The CPU only writes a few tiny textures per frame: which faction stands on each cell, how big it is and
    /// where it is mid-move; the GPU turns that into matter. The floor runs a Gray-Scott reaction-diffusion
    /// seeded by the swarms. Toggle with G; GRID mode is the classic instanced renderer.
    /// </summary>
    public sealed class MorphRenderer
    {
        const int WallRes = 6;     // wall SDF texels per cell
        const int RdRes = 8;       // reaction-diffusion texels per cell

        Material mat, rdMat;
        Mesh quad;
        Texture2D cells, aux, statics, wallSdf;
        RenderTexture rdA, rdB;
        ushort[] cellPx;
        Color32[] auxPx;
        Map map;
        int W, H;
        bool rdReady;
        public float Steps = 80f;
        bool logged;
        public bool Ready => mat != null && rdMat != null;
        static readonly Dictionary<string, Texture2D> sdfCache = new Dictionary<string, Texture2D>();

        public MorphRenderer()
        {
            var s = Shader.Find("Hidden/Argmax/Morph");
            var r = Shader.Find("Hidden/Argmax/RD");
            if (s != null && s.isSupported) mat = new Material(s);
            if (r != null && r.isSupported) rdMat = new Material(r);
            if (!Ready) Debug.LogWarning("ARGMAX: MORPH shaders unavailable on this GPU; GRID mode only");
            quad = Meshes.FlatQuad();
        }

        public void Rebuild(Map m)
        {
            if (!Ready) return;
            map = m; W = m.W; H = m.H;
            cells = new Texture2D(W, H, TextureFormat.RGBAHalf, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            aux = new Texture2D(W, H, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            statics = new Texture2D(W, H, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            cellPx = new ushort[W * H * 4];
            auxPx = new Color32[W * H];
            var st = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = m.Idx(x, y);
                    bool wall = m.Cell[i] == Argmax.Sim.Terrain.Wall;
                    byte h = (byte)(((x * 7 + y * 13) % 5) * 60);
                    bool border = x == 0 || y == 0 || x == W - 1 || y == H - 1;
                    st[i] = new Color32((byte)(wall ? 255 : 0), (byte)(m.Goals.Contains(i) ? 255 : 0), (byte)(border ? 0 : h), 255);
                }
            statics.SetPixels32(st);
            statics.Apply(false);
            wallSdf = WallSdf(m);
            if (rdA != null) { rdA.Release(); rdB.Release(); }
            var fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGHalf) ? RenderTextureFormat.RGHalf : RenderTextureFormat.ARGBHalf;
            rdA = new RenderTexture(W * RdRes, H * RdRes, 0, fmt) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            rdB = new RenderTexture(W * RdRes, H * RdRes, 0, fmt) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            rdA.Create(); rdB.Create();
            rdReady = false;
            mat.SetTexture("_Static", statics);
            mat.SetTexture("_WallSdf", wallSdf);
            mat.SetVector("_MapSize", new Vector4(W, H, 1f / W, 1f / H));
            rdMat.SetTexture("_Static", statics);
            rdMat.SetVector("_MapSize", new Vector4(W, H, 1f / W, 1f / H));
        }

        /// <summary>2D signed distance (in cells) to the union of wall squares; negative inside. Cached per layout.</summary>
        static Texture2D WallSdf(Map m)
        {
            var key = new System.Text.StringBuilder(m.W + "x" + m.H + ":");
            for (int i = 0; i < m.Cell.Length; i++) key.Append(m.Cell[i] == Argmax.Sim.Terrain.Wall ? '1' : '0');
            var k = key.ToString();
            if (sdfCache.TryGetValue(k, out var cached) && cached != null) return cached;
            int tw = m.W * WallRes, th = m.H * WallRes;
            var px = new ushort[tw * th];
            const int R = 3;
            for (int ty = 0; ty < th; ty++)
                for (int tx = 0; tx < tw; tx++)
                {
                    float px0 = (tx + 0.5f) / WallRes, py0 = (ty + 0.5f) / WallRes;
                    int cx = Mathf.Min(m.W - 1, (int)px0), cy = Mathf.Min(m.H - 1, (int)py0);
                    bool inside = m.Cell[m.Idx(cx, cy)] == Argmax.Sim.Terrain.Wall;
                    float best = R;
                    for (int dy = -R; dy <= R; dy++)
                        for (int dx = -R; dx <= R; dx++)
                        {
                            int nx = cx + dx, ny = cy + dy;
                            bool wall = nx < 0 || ny < 0 || nx >= m.W || ny >= m.H ? false : m.Cell[m.Idx(nx, ny)] == Argmax.Sim.Terrain.Wall;
                            if (wall == inside) continue;
                            // distance from the point to that cell's square
                            float qx = Mathf.Max(Mathf.Abs(px0 - (nx + 0.5f)) - 0.5f, 0f), qy = Mathf.Max(Mathf.Abs(py0 - (ny + 0.5f)) - 0.5f, 0f);
                            float d = Mathf.Sqrt(qx * qx + qy * qy);
                            if (d < best) best = d;
                        }
                    px[ty * tw + tx] = Mathf.FloatToHalf(inside ? -best : best);
                }
            var tex = new Texture2D(tw, th, TextureFormat.RHalf, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixelData(px, 0);
            tex.Apply(false);
            if (sdfCache.Count > 24) sdfCache.Clear();
            sdfCache[k] = tex;
            return tex;
        }

        /// <summary>Write this frame's units, step the floor's chemistry, and draw.</summary>
        public void Draw(World w, List<WorldView.Drawn> visible, HashSet<uint> selected, int localSeat, Camera cam, Texture field, Texture fieldDist, bool fieldOn)
        {
            if (!Ready || map == null || cam == null) return;
            System.Array.Clear(cellPx, 0, cellPx.Length);
            for (int i = 0; i < auxPx.Length; i++) auxPx[i] = default;
            ushort zero = Mathf.FloatToHalf(0f);
            for (int i = 0; i < cellPx.Length; i++) cellPx[i] = zero;
            foreach (var d in visible)
            {
                int j = w.FindMote(d.Id);
                if (j < 0) continue;
                ref var m = ref w.Motes[j];
                if (!map.In(m.X, m.Y)) continue;
                int c = m.Y * W + m.X;
                float faction = m.Seat == Rules.NoiseSeat ? 3f : m.Seat == localSeat ? 1f : 2f;
                float radius = Mathf.Min(0.84f, 0.30f + 0.052f * m.Tier);
                cellPx[c * 4 + 0] = Mathf.FloatToHalf(faction);
                cellPx[c * 4 + 1] = Mathf.FloatToHalf(radius);
                cellPx[c * 4 + 2] = Mathf.FloatToHalf(d.Pos.x - (m.X + 0.5f));
                cellPx[c * 4 + 3] = Mathf.FloatToHalf(d.Pos.z - (m.Y + 0.5f));
                float hp = Mathf.Clamp01(m.Hp / (float)Rules.MaxHp(m.Tier));
                auxPx[c] = new Color32((byte)(hp * 255), (byte)(m.Has(MoteFlags.Fired) ? 255 : 0), (byte)(selected.Contains(m.Id) ? 255 : 0), (byte)(m.Tier * 23));
            }
            cells.SetPixelData(cellPx, 0);
            cells.Apply(false);
            aux.SetPixels32(auxPx);
            aux.Apply(false);

            // morphogenesis: a few Gray-Scott steps per frame
            rdMat.SetTexture("_Cells", cells);
            rdMat.SetFloat("_Seed", W * 0.37f + H);
            if (!rdReady) { Graphics.Blit(null, rdA, rdMat, 0); rdReady = true; }
            for (int k = 0; k < 12; k++)
            {
                Graphics.Blit(rdA, rdB, rdMat, 1);
                var t = rdA; rdA = rdB; rdB = t;
            }

            var vp = cam.projectionMatrix * cam.worldToCameraMatrix;
            mat.SetMatrix("_InvVP", vp.inverse);
            mat.SetVector("_CamPos", cam.transform.position);
            mat.SetTexture("_Cells", cells);
            mat.SetTexture("_Aux", aux);
            mat.SetTexture("_RD", rdA);
            mat.SetTexture("_Field", field);
            mat.SetTexture("_FieldDist", fieldDist);
            mat.SetFloat("_FieldOn", fieldOn ? 1f : 0f);
            mat.SetFloat("_MTime", Time.time);
            mat.SetFloat("_Steps", Steps);
            var dbg = System.Environment.GetEnvironmentVariable("ARGMAX_MORPH_DEBUG");
            mat.SetFloat("_Debug", dbg == null ? 0f : dbg == "2" ? 2f : dbg == "3" ? 3f : 1f);
            mat.SetColor("_ColMe", Palette.Me);
            mat.SetColor("_ColFoe", Palette.Foe);
            mat.SetColor("_ColNoise", Palette.Noise);
            mat.SetColor("_ColInk", Palette.Ink);
            mat.SetColor("_ColBone", Palette.Bone);
            mat.SetColor("_ColWall", new Color(0.21f, 0.195f, 0.175f));
            mat.SetColor("_ColGoal", Palette.Goal);
            var rp = new RenderParams(mat)
            {
                worldBounds = new Bounds(new Vector3(W * 0.5f, 0, H * 0.5f), new Vector3(100000, 1000, 100000)),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
            };
            // the lid: map plus a wide margin so the floor runs on toward the horizon
            const float margin = 48f;
            var lid = Matrix4x4.TRS(new Vector3(W * 0.5f, 1.4f, H * 0.5f), Quaternion.identity, new Vector3(W + margin * 2, 1f, H + margin * 2));
            if (!logged) { logged = true; Debug.Log("ARGMAX MORPH: drawing " + W + "x" + H + " lid, steps " + Steps); }
            Graphics.RenderMesh(rp, quad, 0, lid);
        }
    }
}
