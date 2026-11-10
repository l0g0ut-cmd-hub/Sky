using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Объёмные облака — «героические» кучевые башни вокруг корабля (до 8 штук).
    /// Каждая башня — шаблон из 28 сфер (3 шаблона) с поворотом и масштабом; поверхность лепит 3D-шум.
    /// Башни дрейфуют по ветру и переносятся на другую сторону от камеры, когда уходят далеко.
    /// Марш луча — в 1/divider разрешения, сведение с кадром с учётом глубины. Стоит на камере ПЕРЕД SkyTidePost.
    /// Параметры подобраны в офлайн-лаборатории tools/cloudlab (тот же алгоритм на Python).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public class VolumetricClouds : MonoBehaviour
    {
        const int MaxTowers = 8;
        const int Puffs = 28;
        const int Templates = 3;
        const float TemplateHeight = 2.74f;   // высота шаблона в радиусах (520 / 190)

        public Shader shader;
        public Light sun;
        public byte[] noiseBytes;   // CloudNoise3D.bytes (64³ RGBA8)

        [Header("Качество")]
        [Range(1, 8)] public int resolutionDivider = 4;
        [Range(16, 128)] public int steps = 64;
        public float marchDistance = 2600f;

        [Header("Башни")]
        [Range(1, MaxTowers)] public int towerCount = 7;
        public float minRadius = 150f;
        public float maxRadius = 250f;
        public float fieldRadius = 1700f;
        public float minDistance = 480f;
        public float baseHeight = -30f;
        public Vector3 wind = new Vector3(-4f, 0f, 1.8f);

        [Header("Форма")]
        public float lobeScale = 300f;
        public float detailScale = 90f;
        public float lobeAmount = 0.55f;
        public float erosion = 0.5f;
        public float density = 0.035f;

        [Header("Свет")]
        public float lightAbsorption = 1.5f;
        public Color sunGold = new Color(1f, 0.62f, 0.22f);
        public Color sunEmber = new Color(1f, 0.24f, 0.06f);
        public float sunIntensity = 5.2f;
        public Color multiScatter = new Color(0.95f, 0.38f, 0.42f);
        public float multiScatterAmount = 0.2f;
        public Color ambientTop = new Color(0.36f, 0.22f, 0.55f);
        public Color ambientBottom = new Color(0.16f, 0.1f, 0.34f);
        public float ambientIntensity = 0.65f;
        public float forwardG = 0.6f;
        public float backG = -0.2f;
        [Range(0f, 1f)] public float phaseMix = 0.65f;
        public float rim = 1.5f;
        [Range(0f, 1f)] public float aerial = 0.45f;

        [Header("Отладка")]
        [Range(0, 3)] public int debugView;   // 1 — поле башен, 2 — только облака, 3 — пропускание

        Material mat;
        Camera cam;
        Texture3D noise;
        Vector4[] puffs;
        Vector4[] towers = new Vector4[MaxTowers];
        Vector4[] towerRot = new Vector4[MaxTowers];
        bool placed;

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
            return "noiseBytes=" + (noiseBytes != null ? noiseBytes.Length.ToString() : "null")
                + " tex3D=" + (n != null ? n.width + "x" + n.height + "x" + n.depth : "null")
                + " supports3D=" + SystemInfo.supports3DTextures
                + " shader=" + (shader != null ? shader.name + " supported=" + shader.isSupported : "null")
                + " towers=" + towerCount;
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
            noise.Apply(true, true);
            return noise;
        }

        // ---------- шаблоны башен (тот же алгоритм, что tools/cloudlab/puffs.py) ----------

        static float U(System.Random r, float a, float b) { return a + (float)r.NextDouble() * (b - a); }

        static void BuildTemplate(int seed, Vector4[] dst, int offset)
        {
            System.Random r = new System.Random(seed);
            const float H = TemplateHeight;
            int k = offset;
            for (int i = 0; i < 7; i++)   // широкое основание
            {
                float a = i / 7f * Mathf.PI * 2f + U(r, -0.3f, 0.3f);
                float d = U(r, 0.35f, 0.7f);
                dst[k++] = new Vector4(Mathf.Cos(a) * d, H * 0.16f, Mathf.Sin(a) * d, U(r, 0.42f, 0.55f));
            }
            dst[k++] = new Vector4(0f, H * 0.2f, 0f, 0.6f);
            for (int i = 0; i < 6; i++)   // средний ярус
            {
                float a = i / 6f * Mathf.PI * 2f + U(r, -0.4f, 0.4f);
                float d = U(r, 0.2f, 0.45f);
                dst[k++] = new Vector4(Mathf.Cos(a) * d, H * U(r, 0.38f, 0.52f), Mathf.Sin(a) * d, U(r, 0.36f, 0.46f));
            }
            dst[k++] = new Vector4(U(r, -0.1f, 0.1f), H * 0.5f, 0f, 0.48f);
            int heads = 3 + r.Next(3);
            for (int i = 0; i < 5; i++)   // головы (лишние — вырожденные, без вклада)
            {
                float a = U(r, 0f, Mathf.PI * 2f);
                float d = U(r, 0.05f, 0.32f);
                float y = H * U(r, 0.66f, 0.86f);
                float rad = U(r, 0.26f, 0.36f);
                dst[k++] = i < heads ? new Vector4(Mathf.Cos(a) * d, y, Mathf.Sin(a) * d, rad) : new Vector4(0f, -10f, 0f, 0.001f);
            }
            for (int i = 0; i < 7; i++)   // наплывы по бокам
            {
                float a = U(r, 0f, Mathf.PI * 2f);
                float y = H * U(r, 0.25f, 0.7f);
                float d = 0.75f - 0.5f * y / H;
                dst[k++] = new Vector4(Mathf.Cos(a) * d, y, Mathf.Sin(a) * d, U(r, 0.16f, 0.24f));
            }
            while (k < offset + Puffs) dst[k++] = new Vector4(0f, -10f, 0f, 0.001f);   // пустые слоты
        }

        void EnsureTemplates()
        {
            if (puffs != null) return;
            puffs = new Vector4[Puffs * Templates];
            BuildTemplate(7, puffs, 0);
            BuildTemplate(19, puffs, Puffs);
            BuildTemplate(42, puffs, Puffs * 2);
        }

        // ---------- расстановка и дрейф ----------

        void Place(Vector3 center)
        {
            System.Random r = new System.Random(1234);
            for (int i = 0; i < MaxTowers; i++)
            {
                float a = i / (float)MaxTowers * Mathf.PI * 2f + U(r, -0.35f, 0.35f);
                float d = U(r, minDistance, fieldRadius);
                SetTower(i, center + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d), r);
            }
            // Две «витринные» башни на старте: впереди между курсом и солнцем и справа по борту.
            SetTower(0, center + Dir(70f) * 950f, r);
            SetTower(1, center + Dir(150f) * 820f, r);
            placed = true;
        }

        static Vector3 Dir(float yaw)
        {
            return new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
        }

        void SetTower(int i, Vector3 pos, System.Random r)
        {
            float rad = U(r, minRadius, maxRadius);
            float rot = U(r, 0f, Mathf.PI * 2f);
            float hScale = U(r, 0.85f, 1.15f);
            towers[i] = new Vector4(pos.x, baseHeight, pos.z, rad);
            towerRot[i] = new Vector4(Mathf.Cos(rot), Mathf.Sin(rot), r.Next(Templates), TemplateHeight * hScale);
        }

        System.Random wrapRnd = new System.Random(99);

        void Advance(Vector3 camPos, float dt)
        {
            if (!placed) Place(camPos);
            for (int i = 0; i < MaxTowers; i++)
            {
                Vector3 p = new Vector3(towers[i].x, 0f, towers[i].z) + wind * dt;
                Vector3 d = p - new Vector3(camPos.x, 0f, camPos.z);
                if (d.magnitude > fieldRadius * 1.15f)
                {
                    // ушла далеко — переносим на противоположную сторону (в дымке горизонта)
                    Vector3 back = -d.normalized * fieldRadius;
                    SetTower(i, new Vector3(camPos.x, 0f, camPos.z) + back, wrapRnd);
                }
                else
                {
                    towers[i].x = p.x;
                    towers[i].z = p.z;
                }
            }
        }

        /// <summary>Расставить башни вокруг точки заново (кадры для CI).</summary>
        public void ResetTowers(Vector3 center)
        {
            Place(center);
        }

        void LateUpdate()
        {
            if (Application.isPlaying && cam != null) Advance(cam.transform.position, Time.deltaTime);
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
            EnsureTemplates();
            if (!placed) Place(cam.transform.position);

            float t = Application.isPlaying ? Time.time : 0f;
            float sunScale = sun != null ? sun.intensity / 1.45f : 1f;
            mat.SetTexture("_Noise3D", n);
            mat.SetMatrix("_InvProj", cam.projectionMatrix.inverse);
            mat.SetMatrix("_CamToWorld", cam.cameraToWorldMatrix);
            mat.SetVectorArray("_Puffs", puffs);
            mat.SetVectorArray("_Towers", towers);
            mat.SetVectorArray("_TowerRot", towerRot);
            mat.SetFloat("_TowerCount", towerCount);
            mat.SetVector("_Shape", new Vector4(lobeScale, detailScale, lobeAmount, erosion));
            mat.SetVector("_DensityParams", new Vector4(density, lightAbsorption, steps, marchDistance));
            mat.SetVector("_Wind", new Vector4(wind.x, wind.y, wind.z, t));
            mat.SetColor("_SunGold", sunGold * sunIntensity * sunScale);
            mat.SetColor("_SunEmber", sunEmber * sunIntensity * sunScale);
            Color ms = multiScatter * sunIntensity * 0.6f * sunScale;
            ms.a = multiScatterAmount;
            mat.SetColor("_MultiScatter", ms);
            mat.SetColor("_AmbientTop", ambientTop * ambientIntensity);
            mat.SetColor("_AmbientBottom", ambientBottom * ambientIntensity);
            mat.SetVector("_Phase", new Vector4(forwardG, backG, phaseMix, rim));
            mat.SetVector("_Aerial", new Vector4(aerial, 0f, 0f, 0f));
            mat.SetFloat("_Debug", debugView);

            int w = Mathf.Max(16, src.width / resolutionDivider);
            int h = Mathf.Max(16, src.height / resolutionDivider);
            mat.SetVector("_LowRes", new Vector4(w, h, 1f / w, 1f / h));
            RenderTexture clouds = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBHalf);
            RenderTextureFormat depthFmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf) ? RenderTextureFormat.RHalf : RenderTextureFormat.ARGBHalf;
            RenderTexture lowDepth = RenderTexture.GetTemporary(w, h, 0, depthFmt);
            clouds.filterMode = FilterMode.Point;
            lowDepth.filterMode = FilterMode.Point;
            Graphics.Blit(src, clouds, mat, 0);
            Graphics.Blit(src, lowDepth, mat, 2);
            mat.SetTexture("_CloudTex", clouds);
            mat.SetTexture("_LowDepthTex", lowDepth);
            Graphics.Blit(src, dst, mat, 1);
            RenderTexture.ReleaseTemporary(lowDepth);
            RenderTexture.ReleaseTemporary(clouds);
        }
    }
}
