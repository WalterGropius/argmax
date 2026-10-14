// The Deadnet floor: a dark grid that can show the world's gradient fields (beacon, scrap, home) and
// the stigmergic trails motes write. Fields are what units read instead of searching; here you see them.
Shader "Argmax/Ground"
{
    Properties
    {
        _Field ("Field", 2D) = "black" {}
        _MapSize ("Map Size", Vector) = (64, 64, 0, 0)
        _Base ("Base", Color) = (0.018, 0.026, 0.042, 1)
        _Grid ("Grid", Color) = (0.055, 0.085, 0.125, 1)
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

            sampler2D _Field;
            float4 _MapSize, _Base, _Grid;
            float _FieldAmount, _Pulse;

            struct v2f { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; };

            v2f vert (float4 v : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v);
                o.wp = mul(unity_ObjectToWorld, v).xyz;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 c = i.wp.xz;
                float2 uv = c / _MapSize.xy;
                float2 f = abs(frac(c) - 0.5);
                float line1 = 1.0 - smoothstep(0.44, 0.5, max(f.x, f.y));
                float2 f8 = abs(frac(c / 8.0) - 0.5);
                float line8 = 1.0 - smoothstep(0.485, 0.5, max(f8.x, f8.y));
                float3 col = _Base.rgb + _Grid.rgb * (1.0 - line1) * 0.6 + _Grid.rgb * (1.0 - line8) * 0.9;
                float4 fld = tex2D(_Field, uv);
                col += fld.rgb * fld.a * _FieldAmount;
                // scanline shimmer, very faint
                col *= 0.97 + 0.03 * sin(i.wp.z * 6.0 + _Pulse * 2.0);
                float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                return float4(col * (0.35 + 0.65 * inside), 1.0);
            }
            ENDCG
        }
    }
}
