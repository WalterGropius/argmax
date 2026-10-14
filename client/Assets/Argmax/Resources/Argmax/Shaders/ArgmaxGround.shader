// The Deadnet floor: a dark circuit grid that can show the world's gradient fields (beacon, scrap, home) and
// the stigmergic trails motes write. _Occ.r = wall proximity (soft contact shadow), _Occ.g = goal tiles.
// A scan line sweeps the map, and the floor falls into fog with view distance.
Shader "Argmax/Ground"
{
    Properties
    {
        _Field ("Field", 2D) = "black" {}
        _Occ ("Occlusion", 2D) = "black" {}
        _MapSize ("Map Size", Vector) = (64, 64, 0, 0)
        _Base ("Base", Color) = (0.062, 0.057, 0.049, 1)
        _Grid ("Grid", Color) = (0.925, 0.894, 0.827, 1)
        _FieldAmount ("Field Amount", Float) = 1
        _Pulse ("Pulse", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Geometry-10" "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _Field, _Occ;
            float4 _MapSize, _Base, _Grid, _ArgmaxEdge;
            float _FieldAmount, _Pulse, _ArgmaxEmission;

            struct v2f { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; };

            v2f vert (float4 v : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v);
                o.wp = mul(unity_ObjectToWorld, v).xyz;
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float4 frag (v2f i) : SV_Target
            {
                float2 c = i.wp.xz;
                float2 uv = c / _MapSize.xy;
                float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                float2 cell = floor(c);
                float2 f = abs(frac(c) - 0.5);
                // the grid is implied, not drawn: small registration crosses at cell corners, a rule every 8
                float2 g = abs(frac(c + 0.5) - 0.5);
                float xmark = (step(g.x, 0.012) * step(g.y, 0.09) + step(g.y, 0.012) * step(g.x, 0.09)) > 0 ? 1.0 : 0.0;
                float2 f8 = abs(frac(c / 8.0) - 0.5);
                float line8 = smoothstep(0.4985, 0.5, max(f8.x, f8.y));
                float plate = 0.92 + 0.16 * Hash(cell);
                float3 col = _Base.rgb * plate + _Grid.rgb * (xmark * 0.16 + line8 * 0.05) * inside;
                float4 occ = tex2D(_Occ, uv);
                col *= 1.0 - 0.5 * occ.r;
                // goal tiles: chrome plates, hatched
                float hatch = step(0.5, frac((c.x + c.y) * 2.5));
                col = lerp(col, float3(0.957, 0.761, 0.051) * (0.42 + 0.18 * hatch), occ.g * 0.75);
                float4 fld = tex2D(_Field, uv);
                col += fld.rgb * fld.a * _FieldAmount;
                // fog with distance from the camera, and the world fades outside the map
                float dist = distance(_WorldSpaceCameraPos.xyz, i.wp);
                float fog = saturate((dist - 30.0) / 90.0);
                col = lerp(col, _Base.rgb * 0.5, fog * 0.6);
                return float4(col * (0.3 + 0.7 * inside), 1.0);
            }
            ENDCG
        }
    }
}
