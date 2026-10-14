// The finish: a gentle dual-filter bloom for wet highlights, then a print composite. Colours below the
// shoulder pass through untouched (the palette is exact), highlights roll off softly, the ink and colour
// plates sit a hair out of register like a risograph, and the paper has grain. Driven by NeonPost.cs.
Shader "Hidden/Argmax/Post"
{
    Properties { _MainTex ("", 2D) = "black" {} }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex, _BaseTex, _BloomTex;
    float4 _MainTex_TexelSize;
    float _Threshold, _Knee, _BloomIntensity, _Exposure, _Vignette, _Aberration, _Grain, _Scan, _Time2, _Saturation;

    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
    v2f vert (appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

    half3 Prefilter(half3 c)
    {
        half br = max(c.r, max(c.g, c.b));
        half soft = clamp(br - _Threshold + _Knee, 0, 2 * _Knee);
        soft = soft * soft / (4 * _Knee + 1e-4);
        half contrib = max(soft, br - _Threshold) / max(br, 1e-4);
        return c * contrib;
    }

    half3 Down4(float2 uv)
    {
        float4 o = _MainTex_TexelSize.xyxy * float4(-1, -1, 1, 1);
        half3 s = tex2D(_MainTex, uv + o.xy).rgb + tex2D(_MainTex, uv + o.zy).rgb + tex2D(_MainTex, uv + o.xw).rgb + tex2D(_MainTex, uv + o.zw).rgb;
        return s * 0.25;
    }

    half3 Tent(float2 uv)
    {
        float4 d = _MainTex_TexelSize.xyxy * float4(1, 1, -1, 0);
        half3 s = tex2D(_MainTex, uv - d.xy).rgb;
        s += tex2D(_MainTex, uv - d.wy).rgb * 2;
        s += tex2D(_MainTex, uv - d.zy).rgb;
        s += tex2D(_MainTex, uv + d.zw).rgb * 2;
        s += tex2D(_MainTex, uv).rgb * 4;
        s += tex2D(_MainTex, uv + d.xw).rgb * 2;
        s += tex2D(_MainTex, uv + d.zy).rgb;
        s += tex2D(_MainTex, uv + d.wy).rgb * 2;
        s += tex2D(_MainTex, uv + d.xy).rgb;
        return s / 16;
    }

    half3 ACES(half3 x)
    {
        const half a = 2.51, b = 0.03, c = 2.43, d = 0.59, e = 0.14;
        return saturate((x * (a * x + b)) / (x * (c * x + d) + e));
    }

    float Hash(float2 p) { p = frac(p * float2(443.897, 441.423)); p += dot(p, p.yx + 19.19); return frac((p.x + p.y) * p.x); }

    half4 FragPrefilter (v2f i) : SV_Target { return half4(Prefilter(Down4(i.uv)), 1); }
    half4 FragDown (v2f i) : SV_Target { return half4(Down4(i.uv), 1); }
    half4 FragUp (v2f i) : SV_Target { return half4(tex2D(_BaseTex, i.uv).rgb + Tent(i.uv), 1); }

    half4 FragComposite (v2f i) : SV_Target
    {
        float2 uv = i.uv;
        float2 c = uv - 0.5;
        float r2 = dot(c, c);
        // chromatic aberration grows toward the corners
        // risograph misregistration: one plate nudged a fraction of a pixel, the same way everywhere
        float2 off = float2(0.7, -0.45) * _Aberration * _MainTex_TexelSize.xy;
        half3 col;
        col.r = tex2D(_MainTex, uv + off).r;
        col.g = tex2D(_MainTex, uv).g;
        col.b = tex2D(_MainTex, uv - off).b;
        half3 bloom = tex2D(_BloomTex, uv).rgb;
        col += bloom * _BloomIntensity;
        col *= _Exposure;
        // soft shoulder: exact below 0.8, rolls off above
        half3 over = max(col - 0.8, 0.0);
        col = min(col, 0.8) + 0.2 * (1.0 - exp(-over * 5.0));
        half luma = dot(col, half3(0.2126, 0.7152, 0.0722));
        col = lerp(luma.xxx, col, _Saturation);
        // warm vignette, like light falling off a print
        half vig = lerp(1.0, smoothstep(1.0, 0.3, sqrt(r2) * 1.2), _Vignette);
        col *= vig;
        // paper: fine grain plus a slower fibre, strongest in the mid-tones
        half g = Hash(uv * _ScreenParams.xy + _Time2 * 61.7) - 0.5;
        half fibre = Hash(floor(uv * _ScreenParams.xy / 3.0) + 7.0) - 0.5;
        col += (g * 0.7 + fibre * 0.3) * _Grain * (0.4 + luma * (1.0 - luma) * 2.4);
        return half4(col, 1);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragPrefilter
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragDown
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragUp
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragComposite
            ENDCG }
    }
}
