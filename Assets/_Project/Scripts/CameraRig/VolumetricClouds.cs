using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Объёмные облака: марш луча в пониженном разрешении (1/divider экрана) и сведение с кадром.
    /// Стоит на камере ПЕРЕД SkyTidePost, чтобы блум и тонмаппинг легли уже на облака.
    /// Слой облаков — на высоте полёта: сквозь облака можно пролетать, корабль заслоняет облака за собой.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public class VolumetricClouds : MonoBehaviour
    {
        public Shader shader;
        public Light sun;
        public byte[] noiseBytes;   // CloudNoise3D.bytes (64³ RGBA8)

        [Header("Качество")]
        [Range(1, 8)] public int resolutionDivider = 4;
        [Range(16, 96)] public int steps = 40;
        public float marchDistance = 3500f;

        [Header("Форма")]
        public float layerBottom = 25f;
        public float layerTop = 175f;
        public float shapeScale = 1100f;
        public float detailScale = 170f;
        [Range(0f, 1f)] public float coverage = 0.45f;
        public float coverageScale = 5200f;
        public float density = 0.022f;
        public float erosion = 0.6f;
        public Vector3 wind = new Vector3(-6f, 0f, 2.6f);

        [Header("Свет")]
        public float lightAbsorption = 1.1f;
        public float powder = 1.0f;
        public Color sunColor = new Color(1f, 0.62f, 0.34f);
        public float sunIntensity = 3.2f;
        public Color ambientTop = new Color(0.78f, 0.46f, 0.42f);
        public Color ambientBottom = new Color(0.36f, 0.24f, 0.36f);
        public float ambientIntensity = 0.75f;
        public float forwardG = 0.62f;
        public float backG = -0.25f;
        [Range(0f, 1f)] public float phaseMix = 0.75f;
        public float silverLining = 2.2f;

        [Header("Отладка")]
        [Range(0, 3)] public int debugView;   // 1 — поля шума, 2 — только облака, 3 — пропускание

        Material mat;
        Camera cam;
        Texture3D noise;

        void OnEnable()
        {
            cam = GetComponent<Camera>();
            cam.depthTextureMode |= DepthTextureMode.Depth;
        }

        void OnDisable()
        {
            if (mat != null) SkyTideBootstrap.SafeDestroy(mat);
            mat = null;
        }

        void OnDestroy()
        {
            if (noise != null) SkyTideBootstrap.SafeDestroy(noise);
        }

        public string Probe()
        {
            Texture3D n = Noise();
            Texture g = Shader.GetGlobalTexture("_SkyTideNoise");
            return "noiseBytes=" + (noiseBytes != null ? noiseBytes.Length.ToString() : "null")
                + " tex3D=" + (n != null ? n.width + "x" + n.height + "x" + n.depth + " " + n.format : "null")
                + " supports3D=" + SystemInfo.supports3DTextures
                + " globalNoise=" + (g != null ? g.name + " " + g.width + "x" + g.height : "null")
                + " shader=" + (shader != null ? shader.name + " supported=" + shader.isSupported : "null");
        }

        Texture3D Noise()
        {
            if (noise != null) return noise;
            if (noiseBytes == null || noiseBytes.Length != 64 * 64 * 64 * 4) return null;
            noise = new Texture3D(64, 64, 64, TextureFormat.RGBA32, true);
            noise.name = "CloudNoise3D";
            noise.wrapMode = TextureWrapMode.Repeat;
            noise.filterMode = FilterMode.Trilinear;
            noise.SetPixelData(noiseBytes, 0);
            noise.Apply(true, true);   // мипмапы + выгрузка копии из памяти CPU
            return noise;
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            Texture3D n = Noise();
            if (shader == null) shader = Shader.Find("Hidden/SkyTide/VolumetricClouds");
            if (shader == null || !shader.isSupported || n == null || !SystemInfo.supports3DTextures)
            {
                Graphics.Blit(src, dst);
                return;
            }
            if (mat == null)
            {
                mat = new Material(shader);
                mat.hideFlags = HideFlags.HideAndDontSave;
            }

            float t = Application.isPlaying ? Time.time : 0f;
            mat.SetTexture("_Noise3D", n);
            mat.SetMatrix("_InvProj", cam.projectionMatrix.inverse);
            mat.SetMatrix("_CamToWorld", cam.cameraToWorldMatrix);
            mat.SetVector("_Layer", new Vector4(layerBottom, layerTop, marchDistance, steps));
            mat.SetVector("_Shape", new Vector4(shapeScale, detailScale, coverage, coverageScale));
            mat.SetVector("_DensityParams", new Vector4(density, erosion, lightAbsorption, powder));
            mat.SetVector("_Wind", new Vector4(wind.x, wind.y, wind.z, t));
            mat.SetColor("_SunColor", sunColor * sunIntensity * (sun != null ? sun.intensity / 1.45f : 1f));
            mat.SetColor("_AmbientTop", ambientTop * ambientIntensity);
            mat.SetColor("_AmbientBottom", ambientBottom * ambientIntensity);
            mat.SetVector("_Phase", new Vector4(forwardG, backG, phaseMix, silverLining));
            mat.SetFloat("_Debug", debugView);

            int w = Mathf.Max(16, src.width / resolutionDivider);
            int h = Mathf.Max(16, src.height / resolutionDivider);
            RenderTexture clouds = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBHalf);
            clouds.filterMode = FilterMode.Bilinear;
            Graphics.Blit(src, clouds, mat, 0);
            mat.SetTexture("_CloudTex", clouds);
            Graphics.Blit(src, dst, mat, 1);
            RenderTexture.ReleaseTemporary(clouds);
        }
    }
}
