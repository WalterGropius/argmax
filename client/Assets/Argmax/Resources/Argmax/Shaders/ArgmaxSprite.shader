// Instanced flat quads drawn as signed-distance shapes, one draw call per 1023 motes. GRID mode's look:
// flat Bauhaus primaries with a thin ink outline, and Kandinsky rings inside as the value grows.
// _Params.x = shape (0 circle, 1 square, 2 diamond, 3 ring, 4 soft disc, 5 line, 6 hex, 7 triangle)
// _Params.y = ring thickness, _Params.z = inner boost, _Params.w = tier (bodies only)
Shader "Argmax/Sprite"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.BlendMode)] _Src ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _Dst ("Dst Blend", Float) = 10
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend [_Src] [_Dst]
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float _ArgmaxEmission;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float4 prm : TEXCOORD1; };

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Params)
            UNITY_INSTANCING_BUFFER_END(Props)

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * 2.0 - 1.0;
                o.color = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                o.prm = UNITY_ACCESS_INSTANCED_PROP(Props, _Params);
                return o;
            }

            float Shape(float s, float2 p)
            {
                if (s < 0.5) return length(p) - 0.78;
                if (s < 1.5) { float2 q = abs(p) - 0.6; return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - 0.08; }
                if (s < 2.5) return (abs(p.x) + abs(p.y)) * 0.7071 - 0.6;
                if (s < 6.5) { float2 q = abs(p); return max(q.x * 0.866 + q.y * 0.5, q.y) - 0.72; }
                // equilateral triangle, point up (+v is north on the ground)
                float2 q = float2(abs(p.x), p.y + 0.2);
                const float k = 1.7320508;
                q.x -= 0.82; q.y += 0.82 / k;
                if (q.x + k * q.y > 0.0) q = float2(q.x - k * q.y, -k * q.x - q.y) / 2.0;
                q.x -= clamp(q.x, -1.64, 0.0);
                return -length(q) * sign(q.y);
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 p = i.uv;
                float shape = i.prm.x;
                float emis = max(_ArgmaxEmission, 1.0);
                if (shape > 3.5 && shape < 4.5)
                {
                    float g = saturate(1.0 - length(p));
                    return float4(i.color.rgb * emis, i.color.a * g * g);
                }
                if (shape > 4.5 && shape < 5.5)
                {
                    float a = saturate(1.0 - abs(p.y));
                    float tip = saturate((1.0 - abs(p.x)) * 6.0);
                    return float4(i.color.rgb * emis, i.color.a * a * tip);
                }
                if (shape > 2.5 && shape < 3.5)
                {
                    float dr = abs(length(p) - 0.8) - max(i.prm.y, 0.03);
                    float aar = fwidth(dr) * 1.2 + 0.001;
                    return float4(i.color.rgb * emis, i.color.a * saturate(0.5 - dr / aar));
                }
                float d = Shape(shape, p);
                float aa = fwidth(d) * 1.2 + 0.001;
                float alpha = saturate(0.5 - d / aa);
                float3 col = i.color.rgb;
                // thin ink outline
                float edge = saturate(1.0 - abs(d + 0.05) / 0.05);
                col = lerp(col, col * 0.32, edge * 0.85);
                // Kandinsky rings: inner echoes of the shape as the value grows
                float tier = i.prm.w;
                if (tier >= 3.0)
                {
                    float d2 = Shape(shape, p / 0.55) * 0.55;
                    float ringA = saturate(1.0 - abs(d2 + 0.03) / 0.03);
                    col = lerp(col, float3(0.925, 0.894, 0.827), ringA * 0.85);
                }
                if (tier >= 6.0)
                {
                    float d3 = Shape(shape, p / 0.26) * 0.26;
                    col = lerp(col, float3(0.055, 0.051, 0.043), saturate(0.5 - d3 / aa));
                }
                col *= 1.0 + i.prm.z;
                return float4(col, i.color.a * alpha);
            }
            ENDCG
        }
    }
}
