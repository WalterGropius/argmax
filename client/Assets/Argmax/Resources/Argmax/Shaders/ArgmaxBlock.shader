// Instanced unlit-ish boxes for ruins, cores and scrap. Fake key light + top rim so silhouettes read.
Shader "Argmax/Block"
{
    Properties { }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float4 color : COLOR; float h : TEXCOORD1; float glow : TEXCOORD2; };

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Params)
            UNITY_INSTANCING_BUFFER_END(Props)

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                o.color = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                o.glow = UNITY_ACCESS_INSTANCED_PROP(Props, _Params).x;
                o.h = v.vertex.y + 0.5;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 L = normalize(float3(0.35, 0.85, 0.25));
                float lit = 0.45 + 0.55 * saturate(dot(i.n, L));
                float top = step(0.9, i.n.y);
                float3 col = i.color.rgb * lit + i.color.rgb * top * 0.25;
                col += i.color.rgb * i.glow * (0.4 + 0.6 * i.h);
                return float4(col, 1.0);
            }
            ENDCG
        }
    }
}
