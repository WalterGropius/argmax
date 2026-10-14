using UnityEngine;

namespace ArgmaxClient
{
    /// <summary>
    /// Post-processing for the built-in pipeline without any package: HDR bloom from the additive glows,
    /// ACES tonemapping and a light grade. Everything the swarm emits above 1.0 blooms; the dark floor doesn't.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class NeonPost : MonoBehaviour
    {
        public bool On = true;
        public float Threshold = 1.05f, Knee = 0.4f, Bloom = 0.18f, Exposure = 1.05f;
        public float Vignette = 0.42f, Aberration = 1.1f, Grain = 0.06f, Scan = 0f, Saturation = 1.04f;

        const int MaxLevels = 7;
        readonly RenderTexture[] down = new RenderTexture[MaxLevels];
        Material mat;

        static readonly int ThresholdId = Shader.PropertyToID("_Threshold"), KneeId = Shader.PropertyToID("_Knee"),
            BloomId = Shader.PropertyToID("_BloomIntensity"), ExposureId = Shader.PropertyToID("_Exposure"),
            VignetteId = Shader.PropertyToID("_Vignette"), AberrationId = Shader.PropertyToID("_Aberration"),
            GrainId = Shader.PropertyToID("_Grain"), ScanId = Shader.PropertyToID("_Scan"), TimeId = Shader.PropertyToID("_Time2"),
            SatId = Shader.PropertyToID("_Saturation"), BaseId = Shader.PropertyToID("_BaseTex"), BloomTexId = Shader.PropertyToID("_BloomTex");

        void Awake()
        {
            var s = Shader.Find("Hidden/Argmax/Post");
            if (s != null && s.isSupported) mat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
            else Debug.LogWarning("ARGMAX: post shader unavailable; rendering without bloom");
        }

        void OnDestroy() { if (mat != null) Destroy(mat); }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (!On || mat == null) { Graphics.Blit(src, dst); return; }
            mat.SetFloat(ThresholdId, Threshold);
            mat.SetFloat(KneeId, Knee);
            mat.SetFloat(BloomId, Bloom);
            mat.SetFloat(ExposureId, Exposure);
            mat.SetFloat(VignetteId, Vignette);
            mat.SetFloat(AberrationId, Aberration);
            mat.SetFloat(GrainId, Grain);
            mat.SetFloat(ScanId, Scan);
            mat.SetFloat(SatId, Saturation);
            mat.SetFloat(TimeId, Time.unscaledTime % 100f);

            var fmt = src.format == RenderTextureFormat.ARGBHalf || src.format == RenderTextureFormat.DefaultHDR ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
            int w = src.width / 2, h = src.height / 2, n = 0;
            down[0] = RenderTexture.GetTemporary(w, h, 0, fmt);
            Graphics.Blit(src, down[0], mat, 0);
            for (n = 1; n < MaxLevels; n++)
            {
                w /= 2; h /= 2;
                if (w < 4 || h < 4) break;
                down[n] = RenderTexture.GetTemporary(w, h, 0, fmt);
                Graphics.Blit(down[n - 1], down[n], mat, 1);
            }
            var cur = down[n - 1];
            for (int i = n - 2; i >= 0; i--)
            {
                var up = RenderTexture.GetTemporary(down[i].width, down[i].height, 0, fmt);
                mat.SetTexture(BaseId, down[i]);
                Graphics.Blit(cur, up, mat, 2);
                if (cur != down[n - 1]) RenderTexture.ReleaseTemporary(cur);
                cur = up;
            }
            mat.SetTexture(BloomTexId, cur);
            Graphics.Blit(src, dst, mat, 3);
            if (cur != down[n - 1]) RenderTexture.ReleaseTemporary(cur);
            for (int i = 0; i < n; i++) { RenderTexture.ReleaseTemporary(down[i]); down[i] = null; }
        }
    }
}
