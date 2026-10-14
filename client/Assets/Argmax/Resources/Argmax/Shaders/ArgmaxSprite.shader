// Instanced flat quads drawn as signed-distance shapes. One draw call per 1023 motes.
// _Params.x = shape (0 circle, 1 rounded square, 2 diamond, 3 ring, 4 glow, 5 line, 6 hex)
// _Params.y = ring thickness, _Params.z = inner brightness boost
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

            float4 frag (v2f i) : SV_Target
            {
                float2 p = i.uv;
                float shape = i.prm.x;
                float d;
                if (shape < 0.5) d = length(p) - 0.78;
                else if (shape < 1.5) { float2 q = abs(p) - 0.56; d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - 0.2; }
                else if (shape < 2.5) d = (abs(p.x) + abs(p.y)) * 0.7071 - 0.6;
                else if (shape < 3.5) d = abs(length(p) - 0.8) - max(i.prm.y, 0.03);
                else if (shape < 4.5)
                {
                    float g = saturate(1.0 - length(p));
                    return float4(i.color.rgb, i.color.a * g * g);
                }
                else if (shape < 5.5)
                {
                    float a = saturate(1.0 - abs(p.y));
                    float tip = saturate((1.0 - abs(p.x)) * 6.0);
                    return float4(i.color.rgb, i.color.a * a * a * tip);
                }
                else
                {
                    float2 q = abs(p);
                    d = max(q.x * 0.866 + q.y * 0.5, q.y) - 0.72;
                }
                float aa = fwidth(d) * 1.2 + 0.001;
                float alpha = saturate(0.5 - d / aa);
                float inner = saturate(-d * 2.5);
                float3 col = i.color.rgb * (0.7 + (0.45 + i.prm.z) * inner);
                // a thin bright rim reads well against the dark ground
                float rim = saturate(1.0 - abs(d + 0.06) / 0.06);
                col += i.color.rgb * rim * 0.35;
                return float4(col, i.color.a * alpha);
            }
            ENDCG
        }
    }
}
