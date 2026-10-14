// Morphogenesis on the floor: a Gray-Scott reaction-diffusion (Turing, 1952) running on the GPU.
// R = activator-substrate A, G = inhibitor-pattern B. Swarms seed B where they stand, and each faction's
// territory gets its own feed/kill rates: cobalt grows spots, vermilion grows labyrinths, the Noise grows worms.
Shader "Hidden/Argmax/RD"
{
    Properties { _MainTex ("", 2D) = "white" {} }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex, _Cells, _Static;
    float4 _MainTex_TexelSize, _MapSize;
    float _Seed;

    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
    v2f vert (appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

    float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7)) + _Seed) * 43758.5453); }

    float4 FragInit (v2f i) : SV_Target
    {
        float n = Hash(floor(i.uv * _MainTex_TexelSize.zw / 3.0));
        return float4(1.0, n > 0.985 ? 0.6 : 0.0, 0, 1);
    }

    float4 FragStep (v2f i) : SV_Target
    {
        float2 t = _MainTex_TexelSize.xy;
        float2 c = tex2D(_MainTex, i.uv).rg;
        float2 lap = -c;
        lap += 0.2 * (tex2D(_MainTex, i.uv + float2(t.x, 0)).rg + tex2D(_MainTex, i.uv - float2(t.x, 0)).rg
                    + tex2D(_MainTex, i.uv + float2(0, t.y)).rg + tex2D(_MainTex, i.uv - float2(0, t.y)).rg);
        lap += 0.05 * (tex2D(_MainTex, i.uv + t).rg + tex2D(_MainTex, i.uv - t).rg
                     + tex2D(_MainTex, i.uv + float2(t.x, -t.y)).rg + tex2D(_MainTex, i.uv + float2(-t.x, t.y)).rg);
        // territory: the faction standing on this cell (or none)
        float4 cell = tex2D(_Cells, i.uv);
        float faction = cell.r;
        float f = 0.0545, k = 0.062;                       // coral, where nobody stands
        if (faction > 0.5 && faction < 1.5) { f = 0.0367; k = 0.0649; }   // cobalt: mitosis spots
        else if (faction > 1.5 && faction < 2.5) { f = 0.029; k = 0.057; } // vermilion: labyrinth
        else if (faction > 2.5) { f = 0.078; k = 0.061; }                  // noise: worms
        float a = c.r, b = c.g;
        float abb = a * b * b;
        a += (1.0 * lap.r - abb + f * (1.0 - a));
        b += (0.5 * lap.g + abb - (k + f) * b);
        // a unit is a seed: the pattern starts where the swarm stands
        float2 cellUv = frac(i.uv * _MapSize.xy) - 0.5;
        if (faction > 0.5 && dot(cellUv, cellUv) < 0.04) b = max(b, 0.45);
        // nothing grows inside walls
        float wall = tex2D(_Static, i.uv).r;
        if (wall > 0.5) { a = 1.0; b = 0.0; }
        return float4(saturate(a), saturate(b), 0, 1);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragInit
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment FragStep
            ENDCG }
    }
}
