// Instanced boxes for ruins, cores and scrap. Fake key light, a baked bottom-up occlusion gradient and
// Tron-style emissive edges (_Params.y) so the ruins read as circuitry. _Params.x = body glow.
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

            float4 _ArgmaxEdge;      // rgb edge color, a = global edge strength
            float _ArgmaxEmission;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float4 color : COLOR; float3 op : TEXCOORD1; float4 prm : TEXCOORD2; float3 wp : TEXCOORD3; };

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
                o.prm = UNITY_ACCESS_INSTANCED_PROP(Props, _Params);
                o.op = v.vertex.xyz;
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 L = normalize(float3(0.35, 0.85, 0.25));
                float lit = 0.4 + 0.6 * saturate(dot(i.n, L));
                float h = i.op.y + 0.5;
                float top = step(0.9, i.n.y);
                float occ = lerp(0.45, 1.0, saturate(h * 1.4)) * (top > 0.5 ? 1.0 : 1.0);
                float3 col = i.color.rgb * lit * occ + i.color.rgb * top * 0.18;
                float emis = max(_ArgmaxEmission, 1.0);
                col += i.color.rgb * i.prm.x * (0.4 + 0.6 * h) * emis;
                // edges: object space is [-0.5, 0.5]^3, so an edge is where two coordinates hit the boundary
                float3 a = abs(i.op) * 2.0;
                float3 e = smoothstep(0.86, 0.985, a);
                float edge = max(max(e.x * e.y, e.y * e.z), e.x * e.z);
                float edgeStrength = i.prm.y * _ArgmaxEdge.a;
                float3 edgeCol = lerp(_ArgmaxEdge.rgb, i.color.rgb * 2.0, 0.35);
                // a slow pulse travels along the circuitry
                float pulse = 0.65 + 0.35 * sin(i.wp.x * 0.7 + i.wp.z * 0.5 - _Time.y * 2.0);
                col += edgeCol * edge * edgeStrength * pulse * emis;
                return float4(col, 1.0);
            }
            ENDCG
        }
    }
}
