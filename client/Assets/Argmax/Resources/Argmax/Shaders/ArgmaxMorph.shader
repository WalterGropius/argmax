// MORPH: the world raymarched as signed distance fields. The grid is implicit: it survives only in where
// things are. Units are blobs that smooth-union with their own faction (cobalt spheres, vermilion rounded
// cubes, chrome octahedra), walls are rounded stone extruded from a 2D distance field, and the floor wears
// the reaction-diffusion pattern the swarms grow. Writes depth, so overlays (rings, orders, effects) compose.
//
// Per-cell data: _Cells (RGBAHalf, point) r = faction 0..3, g = radius, b/a = offset of the unit from the
// cell center (it is drawn mid-move). _Aux (RGBA8, point) r = hp, g = fired, b = selected.
// _Static (RGBA8, point) r = wall, g = goal, b = wall height. _WallSdf (RHalf, bilinear): 2D distance to walls.
Shader "Hidden/Argmax/Morph"
{
    Properties { }
    SubShader
    {
        Tags { "Queue"="Geometry-5" "RenderType"="Opaque" }
        Cull Off ZWrite On ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _Cells, _Aux, _Static, _WallSdf, _RD, _Field, _FieldDist;
            float4x4 _InvVP;
            float4 _MapSize;         // W, H, 1/W, 1/H
            float4 _CamPos;
            float4 _ColMe, _ColFoe, _ColNoise, _ColInk, _ColBone, _ColWall, _ColGoal;
            float _MTime, _FieldOn, _Steps, _Debug;

            // drawn as a horizontal lid over the map at the top of the slab; each pixel marches from the camera
            // through its point on the lid
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; float2 sp : TEXCOORD1; };

            v2f vert (appdata v)
            {
                v2f o;
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.sp = o.pos.xy / o.pos.w;
                return o;
            }

            float smin(float a, float b, float k) { float h = max(k - abs(a - b), 0.0) / k; return min(a, b) - h * h * k * 0.25; }
            float sdRoundBox(float3 p, float3 b, float r) { float3 q = abs(p) - b + r; return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0) - r; }
            float sdOcta(float3 p, float s) { p = abs(p); return (p.x + p.y + p.z - s) * 0.57735027; }

            // material ids: 0 ground, 1 wall, 2 me, 3 foe, 4 noise
            float Map(float3 p, out float mat, out float3 unitData)
            {
                float2 cell = floor(p.xz);
                float2 uv = (cell + 0.5) * _MapSize.zw;
                float rd = tex2Dlod(_RD, float4(p.xz * _MapSize.zw, 0, 0)).g;
                float d = p.y + rd * 0.03;
                mat = 0; unitData = 0;
                // walls: extruded 2D distance field with rounded tops
                float w2 = tex2Dlod(_WallSdf, float4(p.xz * _MapSize.zw, 0, 0)).r;
                // the clamped texture would extrude the border forever; walls stop at the map's edge
                w2 = max(w2, max(max(-p.x, p.x - _MapSize.x), max(-p.z, p.z - _MapSize.y)));
                float wh = 0.45 + tex2Dlod(_Static, float4(uv, 0, 0)).b * 0.55;
                float2 wq = float2(w2 + 0.06, p.y - wh + 0.06);
                float dw = length(max(wq, 0.0)) + min(max(wq.x, wq.y), 0.0) - 0.06;
                if (dw < d) { d = dw; mat = 1; }
                // units live below ~1.0; skip the neighbourhood lookups above that
                if (p.y > 1.05) return d;
                float dm = 1e5, df = 1e5, dn = 1e5;
                float3 bestData = 0;
                float bestD = 1e5;
                float wob = _MTime;
                [unroll] for (int j = -1; j <= 1; j++)
                [unroll] for (int i = -1; i <= 1; i++)
                {
                    float2 nc = cell + float2(i, j);
                    float4 c = tex2Dlod(_Cells, float4((nc + 0.5) * _MapSize.zw, 0, 0));
                    if (c.r < 0.5) continue;
                    float r = c.g;
                    float3 ctr = float3(nc.x + 0.5 + c.b, r * 0.86, nc.y + 0.5 + c.a);
                    float3 q = p - ctr;
                    float du;
                    if (c.r < 1.5) { du = length(q) - r; dm = smin(dm, du, 0.42); }
                    else if (c.r < 2.5) { du = sdRoundBox(q, float3(r, r, r) * 0.78, r * 0.28); df = smin(df, du, 0.3); }
                    else
                    {
                        float a = wob * 0.7 + nc.x * 1.7 + nc.y * 2.3;
                        float cs = cos(a), sn = sin(a);
                        q.xz = float2(cs * q.x - sn * q.z, sn * q.x + cs * q.z);
                        du = sdOcta(q, r * 1.3) - r * 0.08;
                        dn = smin(dn, du, 0.5);
                    }
                    if (du < bestD) { bestD = du; bestData = float3((nc + 0.5) * _MapSize.zw, c.r); }
                }
                // living surface: slow standing waves on the blobs, gunkier for the Noise
                float3 wp = p * 6.0;
                float ripple = sin(wp.x + wob * 1.3) * sin(wp.z * 1.1 - wob) * sin(wp.y * 1.7 + wob * 0.6);
                dm += ripple * 0.018;
                dn += ripple * 0.05;
                float du = min(dm, min(df, dn));
                if (du < d)
                {
                    mat = du == dm ? 2 : du == df ? 3 : 4;
                    unitData = bestData;
                    // units touching the floor melt into it a little: organic out of digital
                    d = smin(d, du, 0.08);
                }
                return d;
            }

            float MapD(float3 p) { float m; float3 u; return Map(p, m, u); }

            float3 Normal(float3 p)
            {
                const float2 k = float2(1, -1) * 0.0025;
                return normalize(k.xyy * MapD(p + k.xyy) + k.yyx * MapD(p + k.yyx) + k.yxy * MapD(p + k.yxy) + k.xxx * MapD(p + k.xxx));
            }

            float Shadow(float3 ro, float3 rd)
            {
                float res = 1.0, t = 0.04;
                [loop] for (int i = 0; i < 14; i++)
                {
                    float h = MapD(ro + rd * t);
                    res = min(res, 9.0 * h / t);
                    t += clamp(h, 0.05, 0.35);
                    if (res < 0.02 || t > 4.0) break;
                }
                return saturate(res);
            }

            float AO(float3 p, float3 n)
            {
                float occ = 0.0, sca = 1.0;
                [unroll] for (int i = 1; i <= 3; i++)
                {
                    float h = 0.06 * i;
                    occ += (h - MapD(p + n * h)) * sca;
                    sca *= 0.7;
                }
                return saturate(1.0 - 2.2 * occ);
            }

            struct FragOut { float4 col : SV_Target; float depth : SV_Depth; };

            FragOut frag (v2f i)
            {
                FragOut o;
                if (_Debug > 1.5) { o.col = float4(1, 0, 1, 1); o.depth = 0.5; return o; }
                float3 ro = _WorldSpaceCameraPos.xyz;
                float3 rd = normalize(i.wp - ro);
                float3 bg = _ColInk.rgb;
                o.col = float4(bg, 1);
                #if defined(UNITY_REVERSED_Z)
                o.depth = 0;
                #else
                o.depth = 1;
                #endif

                // slab y in [-0.1, 1.4]
                if (_Debug > 0.5) { o.col = float4(frac(i.wp * 0.1), 1); return o; }
                if (rd.y > -1e-4) return o;
                float tEnter = max(0.0, length(i.wp - ro) - 0.01);
                float tExit = (-0.1 - ro.y) / rd.y;
                float t = tEnter, mat = 0;
                float3 unitData = 0;
                bool hit = false;
                int steps = (int)_Steps;
                [loop] for (int s = 0; s < 96; s++)
                {
                    if (s >= steps) break;
                    float3 p = ro + rd * t;
                    float d = Map(p, mat, unitData);
                    if (d < 0.0008 * t + 0.001) { hit = true; break; }
                    t += d * 0.85;
                    if (t > tExit) break;
                }
                if (!hit) { t = tExit; mat = 0; }
                float3 p = ro + rd * t;
                float3 n = hit ? Normal(p) : float3(0, 1, 0);
                float2 muv = p.xz * _MapSize.zw;
                float inside = step(0, muv.x) * step(muv.x, 1) * step(0, muv.y) * step(muv.y, 1);

                float3 L = normalize(float3(-0.45, 0.82, -0.36));
                float3 keyCol = float3(1.0, 0.94, 0.84) * 1.15;
                float3 sky = float3(0.42, 0.47, 0.62) * 0.32;
                float sh = Shadow(p + n * 0.01, L);
                float ao = AO(p, n);
                float dif = saturate(dot(n, L));
                float3 H = normalize(L - rd);
                float3 col;
                if (mat < 0.5)
                {
                    // floor: ink, with the Turing pattern as a darker-and-lighter skin, an implicit grid only in light
                    float2 rdv = tex2Dlod(_RD, float4(muv, 0, 0)).rg;
                    if (_Debug > 2.5) { o.col = float4(rdv.g * 2.5, rdv.r * 0.5, 0, 1); o.depth = 0.5; return o; }
                    // engraved: both phases stay ink (one a shade deeper); the pattern's contour is a bone hairline
                    float pat = smoothstep(0.14, 0.24, rdv.g);
                    float contour = saturate(1.0 - abs(rdv.g - 0.19) / 0.035);
                    float3 baseC = _ColInk.rgb * (1.25 - 0.4 * pat) + _ColBone.rgb * 0.16 * contour;
                    float4 st = tex2Dlod(_Static, float4((floor(p.xz) + 0.5) * _MapSize.zw, 0, 0));
                    // goal plates: chrome, hatched
                    float hatch = step(0.5, frac((p.x + p.z) * 2.5));
                    baseC = lerp(baseC, _ColGoal.rgb * (0.55 + 0.25 * hatch), st.g * 0.8);
                    // fields: the gradient flows toward its source like water (Schwenk)
                    if (_FieldOn > 0.5)
                    {
                        float4 fc = tex2Dlod(_Field, float4(muv, 0, 0));
                        float fd = tex2Dlod(_FieldDist, float4(muv, 0, 0)).r * 64.0;
                        float warp = sin(p.x * 1.7 + _MTime * 0.6) * 0.35 + sin(p.z * 1.3 - _MTime * 0.4) * 0.35;
                        float wave = sin((fd + warp) * 1.6 + _MTime * 3.0);
                        float crest = smoothstep(0.55, 1.0, wave);
                        baseC += fc.rgb * (0.12 * fc.a + crest * 0.55 * saturate(1.0 - fd / 48.0)) * step(fd, 63.0);
                    }
                    col = baseC * (sky * 0.9 * ao + keyCol * (0.25 + 0.55 * dif) * sh);
                    col = lerp(bg * 0.8, col, inside * 0.85 + 0.15);
                }
                else if (mat < 1.5)
                {
                    // walls: structural, matte, bone-etched rims on the top edges
                    float w2 = tex2Dlod(_WallSdf, float4(muv, 0, 0)).r;
                    float top = smoothstep(0.6, 0.9, n.y);
                    float rim = top * smoothstep(-0.12, -0.02, w2);
                    float3 baseC = _ColWall.rgb * (0.9 + 0.25 * top);
                    col = baseC * (sky * 1.4 * ao + keyCol * dif * sh) + _ColBone.rgb * rim * 0.55;
                }
                else
                {
                    // living matter: glossy, wet, a little translucent
                    float3 baseC = mat < 2.5 ? _ColMe.rgb : mat < 3.5 ? _ColFoe.rgb : _ColNoise.rgb;
                    float4 aux = tex2Dlod(_Aux, float4(unitData.xy, 0, 0));
                    baseC *= 0.55 + 0.45 * aux.r;
                    float wrap = saturate((dot(n, L) + 0.4) / 1.4);
                    float fres = pow(1.0 - saturate(dot(n, -rd)), 3.0);
                    float spec = pow(saturate(dot(n, H)), 70.0) * (0.35 + 0.65 * sh);
                    float sss = pow(saturate(dot(-rd, -L) * 0.5 + 0.5), 2.0) * 0.25;
                    col = baseC * (sky * 1.2 * ao + keyCol * wrap * (0.35 + 0.65 * sh)) + baseC * sss;
                    col += keyCol * spec * 1.1 + _ColBone.rgb * fres * 0.3;
                    // a cool back light from the far side, so the matter separates from the floor
                    col += float3(0.45, 0.55, 0.85) * pow(saturate(dot(n, normalize(float3(0.3, 0.4, 0.9)))), 3.0) * 0.18;
                    col += baseC * aux.g * 0.6;                         // firing flare
                    col = lerp(col, _ColBone.rgb, aux.b * fres * 1.2);  // selected: a bone rim
                }
                // atmosphere: fade with distance into the ink
                float fog = saturate((t - 25.0) / 110.0);
                col = lerp(col, bg, fog * 0.7);
                o.col = float4(col, 1);
                float4 clip = mul(UNITY_MATRIX_VP, float4(p, 1));
                #if defined(UNITY_REVERSED_Z)
                o.depth = clip.z / clip.w;
                #else
                o.depth = clip.z / clip.w * 0.5 + 0.5;
                #endif
                return o;
            }
            ENDCG
        }
    }
}
