using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Постобработка на камере (встроенный рендер): HDR → лучи солнца сквозь облака, блум от солнца, окон
    /// и раскалённых краёв облаков → экспозиция, контраст, насыщенность → ACES → тёплые света / холодные тени, виньетка.
    /// Шейдер Hidden/SkyTide/Post. Работает и в редакторе (кадры для CI).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public class SkyTidePost : MonoBehaviour
    {
        public Shader shader;
        public Light sun;
        public TouchControls controls;

        [Header("Тон")]
        public float exposure = 1.02f;
        public float contrast = 1.08f;
        public float saturation = 1f;
        public Color shadowTone = new Color(0.44f, 0.46f, 0.6f);
        public Color highlightTone = new Color(0.6f, 0.5f, 0.42f);
        [Range(0f, 1f)] public float toneStrength = 0.22f;
        [Range(0f, 1f)] public float vignette = 0.38f;
        public Color vignetteColor = new Color(0.35f, 0.18f, 0.22f);

        [Header("Блум")]
        public float bloomThreshold = 1.2f;
        public float bloomKnee = 0.45f;
        public float bloomIntensity = 0.35f;
        public Color bloomTint = new Color(1f, 0.82f, 0.62f);
        [Range(2, 7)] public int bloomIterations = 6;

        [Header("Лучи солнца")]
        public float shaftLength = 0.85f;
        public float shaftDecay = 0.9f;
        public float shaftWeight = 0.12f;
        public float shaftIntensity = 0.75f;
        public Color shaftColor = new Color(1f, 0.62f, 0.3f);

        Material mat;
        Camera cam;
        readonly RenderTexture[] mips = new RenderTexture[8];

        static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
        static readonly int SampleScaleId = Shader.PropertyToID("_SampleScale");
        static readonly int BloomTexId = Shader.PropertyToID("_BloomTex");
        static readonly int ShaftTexId = Shader.PropertyToID("_ShaftTex");
        static readonly int SunScreenId = Shader.PropertyToID("_SunScreen");
        static readonly int ShaftParamsId = Shader.PropertyToID("_ShaftParams");

        void OnEnable()
        {
            cam = GetComponent<Camera>();
            cam.allowHDR = true;
            cam.allowMSAA = false;   // с MSAA картинка в OnRenderImage на части платформ перевёрнута — сглаживание сделаем своим проходом
            cam.depthTextureMode |= DepthTextureMode.Depth;
        }

        void OnDisable()
        {
            if (mat != null) SkyTideBootstrap.SafeDestroy(mat);
            mat = null;
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst){long __pt=PerfTimers.Start();OnRenderImageTimed(src,dst);PerfTimers.Stop("Пост-обработка и HUD: команды",__pt);}
        void OnRenderImageTimed(RenderTexture src, RenderTexture dst)
        {
            RenderScene(src, dst);
            // При пониженном разрешении сцены HUD рисует SceneScaler — в родном разрешении экрана.
            if (controls != null && !SceneScaler.Active) controls.DrawHud(dst, src.width, src.height);
        }

        void RenderScene(RenderTexture src, RenderTexture dst)
        {
            if (shader == null) shader = Shader.Find("Hidden/SkyTide/Post");
            if (shader == null || !shader.isSupported)
            {
                Graphics.Blit(src, dst);
                return;
            }
            if (mat == null)
            {
                mat = new Material(shader);
                mat.hideFlags = HideFlags.HideAndDontSave;
            }

            RenderTextureFormat fmt = src.format;
            int w = src.width / 2, h = src.height / 2;

            // ---------- блум ----------
            mat.SetVector(ThresholdId, new Vector4(bloomThreshold, bloomKnee, 12f, 0f));
            int n = 0;
            mips[0] = RenderTexture.GetTemporary(w, h, 0, fmt);
            Graphics.Blit(src, mips[0], mat, 0);
            n = 1;
            for (int i = 1; i < bloomIterations; i++)
            {
                w = Mathf.Max(1, w / 2);
                h = Mathf.Max(1, h / 2);
                if (w < 4 || h < 4) break;
                mips[i] = RenderTexture.GetTemporary(w, h, 0, fmt);
                Graphics.Blit(mips[i - 1], mips[i], mat, 1);
                n = i + 1;
            }
            mat.SetFloat(SampleScaleId, 1f);
            for (int i = n - 1; i > 0; i--) Graphics.Blit(mips[i], mips[i - 1], mat, 2);

            // ---------- лучи солнца ----------
            Vector4 sunScreen = SunOnScreen();
            RenderTexture shaftA = null;
            if (sunScreen.z > 0.001f)
            {
                int sw = src.width / 4, sh = src.height / 4;
                shaftA = RenderTexture.GetTemporary(sw, sh, 0, fmt);
                RenderTexture shaftB = RenderTexture.GetTemporary(sw, sh, 0, fmt);
                mat.SetVector(SunScreenId, sunScreen);
                Graphics.Blit(src, shaftA, mat, 3);
                mat.SetVector(ShaftParamsId, new Vector4(shaftLength, shaftDecay, shaftWeight, shaftIntensity));
                Graphics.Blit(shaftA, shaftB, mat, 4);
                mat.SetVector(ShaftParamsId, new Vector4(shaftLength * 0.35f, shaftDecay, shaftWeight * 1.6f, shaftIntensity));
                Graphics.Blit(shaftB, shaftA, mat, 4);
                RenderTexture.ReleaseTemporary(shaftB);
            }
            mat.SetVector(SunScreenId, sunScreen);
            mat.SetVector(ShaftParamsId, new Vector4(shaftLength, shaftDecay, shaftWeight, shaftIntensity));
            mat.SetTexture(ShaftTexId, shaftA != null ? (Texture)shaftA : Texture2D.blackTexture);
            mat.SetColor("_ShaftColor", shaftColor);

            // ---------- итог ----------
            mat.SetTexture(BloomTexId, mips[0]);
            mat.SetFloat("_BloomIntensity", bloomIntensity);
            mat.SetColor("_BloomTint", bloomTint);
            mat.SetFloat("_Exposure", exposure);
            mat.SetFloat("_Contrast", contrast);
            mat.SetFloat("_Saturation", saturation);
            mat.SetColor("_ShadowTone", shadowTone);
            mat.SetColor("_HighlightTone", highlightTone);
            mat.SetFloat("_ToneStrength", toneStrength);
            mat.SetFloat("_Vignette", vignette);
            mat.SetColor("_VignetteColor", vignetteColor);
            Graphics.Blit(src, dst, mat, 5);

            for (int i = 0; i < n; i++)
            {
                RenderTexture.ReleaseTemporary(mips[i]);
                mips[i] = null;
            }
            if (shaftA != null) RenderTexture.ReleaseTemporary(shaftA);
        }

        /// <summary>Солнце в UV экрана (xy), видимость с плавным угасанием за краем кадра (z), соотношение сторон (w).</summary>
        Vector4 SunOnScreen()
        {
            Vector3 dir = sun != null ? -sun.transform.forward : Vector3.right;
            Vector3 vp = cam.WorldToViewportPoint(cam.transform.position + dir * 1000f);
            float vis = 0f;
            if (vp.z > 0f)
            {
                float outX = Mathf.Max(0f, Mathf.Abs(vp.x - 0.5f) - 0.5f);
                float outY = Mathf.Max(0f, Mathf.Abs(vp.y - 0.5f) - 0.5f);
                vis = Mathf.Clamp01(1f - Mathf.Max(outX, outY) * 2.5f);
            }
            return new Vector4(vp.x, vp.y, vis, cam.aspect);
        }
    }
}
