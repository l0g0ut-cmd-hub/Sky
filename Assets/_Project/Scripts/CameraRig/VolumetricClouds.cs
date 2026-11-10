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
        OrbitCamera orbit;
        const int MaxTowers = 8;
        const int Puffs = 28;
        const int Templates = 3;
        const float TemplateHeight = 2.74f;   // высота шаблона в радиусах (520 / 190)

        public Shader shader;
        public Shader impostorShader;
        [Header("Импосторы (дальние облака — снимки объёмных)")]
        public bool impostors = true;
        [Range(32, 200)] public int impostorBakeSteps = 112;
        readonly CloudImpostors imp = new CloudImpostors();
        public Light sun;
        public byte[] noiseBytes;   // CloudNoise3D.bytes (64³ RGBA8)
        [Tooltip("Запечённые шаблоны башен (SceneBuilder → Art/TowerField.asset). Пусто — запекаются при старте.")]
        public Texture3D bakedField;

        [Header("Качество")]
        [Range(1, 8)] public int resolutionDivider = 3;   // 4 — быстрее, но море облаков на весь экран выглядит мыльным
        [Range(16, 128)] public int steps = 40;
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

        [Header("Объёмное море облаков")]
        public float deckBase = -60f;
        public float deckTop = 20f;
        public float deckDensity = 0.03f;
        [Range(6, 32)] public int deckSteps = 18;
        public float deckWeatherScale = 1400f;
        [Range(0f, 0.8f)] public float deckCoverage = 0.2f;   // порог: меньше — сплошнее
        public float deckLobeScale = 160f;
        public float deckDetailScale = 55f;
        public float deckDistance = 5000f;
        public float deckAbsorption = 1.7f;
        [Tooltip("Яркость солнца на море облаков: тонкий слой освещён почти целиком — без этого пересвет")]
        public float deckSunScale = 0.22f;

        [Header("Дымка у моря облаков (не используется — заменена объёмным морем)")]
        public float hazeDensity = 0.00045f;
        public float hazeTop = 130f;
        public float hazeDistance = 1600f;
        [Range(4, 32)] public int hazeSteps = 14;

        [Header("Свет")]
        public float lightAbsorption = 1.4f;
        public Color sunGold = new Color(1f, 0.62f, 0.22f);
        public Color sunEmber = new Color(1f, 0.24f, 0.06f);
        public float sunIntensity = 4.3f;
        public Color multiScatter = new Color(0.95f, 0.38f, 0.42f);
        public float multiScatterAmount = 0.2f;
        public Color ambientTop = new Color(0.36f, 0.22f, 0.55f);
        public Color ambientBottom = new Color(0.16f, 0.1f, 0.34f);
        public float ambientIntensity = 0.60f;
        public float forwardG = 0.6f;
        public float backG = -0.2f;
        [Range(0f, 1f)] public float phaseMix = 0.65f;
        public float rim = 1.15f;
        [Range(0f, 1f)] public float aerial = 0.45f;

        [Header("Отладка")]
        public bool blurEnabled = true; // CI: bypass фильтрации для сравнения
        [Range(0, 3)] public int debugView;   // 1 — поле башен, 2 — только облака, 3 — пропускание

        Material mat;
        Camera cam;
        Texture3D noise;
        Texture3D field;
        Vector4[] puffs;
        const int FieldXZ = 40, FieldY = 72;
        const float FieldHalf = 1.5f, FieldBottom = -0.15f;
        const float FieldHeight = TemplateHeight + 0.55f;
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
            imp.Release();
            if (noise != null) SkyTideBootstrap.SafeDestroy(noise);
            if (field != null && field != bakedField) SkyTideBootstrap.SafeDestroy(field);
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
            if (puffs != null && field != null) return;
            puffs = Templates_();
            field = bakedField != null ? bakedField : BakeField(SunTowardDefault());
        }

        static Vector4[] Templates_()
        {
            Vector4[] p = new Vector4[Puffs * Templates];
            BuildTemplate(7, p, 0);
            BuildTemplate(19, p, Puffs);
            BuildTemplate(42, p, Puffs * 2);
            return p;
        }

        /// <summary>Направление НА солнце при стандартной настройке света (SkyTideBootstrap.ConfigureSun).</summary>
        public static Vector3 SunTowardDefault()
        {
            return -(Quaternion.Euler(7f, -90f, 0f) * Vector3.forward);
        }

        /// <summary>
        /// Запекает шаблоны башен в 3D-текстуру RGBA (шаблоны сложены по z):
        ///   R — поле пухлости (f + 1) / 2 с мягким объединением сфер;
        ///   G — оптическая толщина к солнцу в радиусах башни / 5 (солнце неподвижно — свет считается один раз);
        ///   B — видимость неба (заполняющий свет гаснет в глубине облака).
        /// В шейдере одна выборка даёт и форму, и свет — без марша к солнцу на каждом шаге.
        /// Башни не поворачиваются (иначе запечённый свет разошёлся бы с солнцем), разнообразие — шаблоны и масштаб.
        /// </summary>
        public static Texture3D BakeField(Vector3 sunToward)
        {
            Vector4[] sph = Templates_();
            int nx = FieldXZ, ny = FieldY, nz = FieldXZ * Templates;
            float[] f = new float[nx * ny * nz];
            const float k = 0.22f;   // мягкость объединения сфер
            float cx = 2f * FieldHalf / FieldXZ, cy = FieldHeight / ny;   // размер вокселя в радиусах
            for (int tz = 0; tz < Templates; tz++)
            {
                int off = tz * Puffs;
                for (int z = 0; z < FieldXZ; z++)
                {
                    float pz = -FieldHalf + (z + 0.5f) * cx;
                    for (int y = 0; y < ny; y++)
                    {
                        float py = FieldBottom + (y + 0.5f) * cy;
                        for (int x = 0; x < nx; x++)
                        {
                            float px = -FieldHalf + (x + 0.5f) * cx;
                            float v0 = -1f;
                            for (int s = 0; s < Puffs; s++)
                            {
                                Vector4 S = sph[off + s];
                                if (S.w < 0.01f) continue;
                                float dx = px - S.x, dy = py - S.y, dz = pz - S.z;
                                float v = 1f - Mathf.Sqrt(dx * dx + dy * dy + dz * dz) / S.w;
                                float h = Mathf.Max(k - Mathf.Abs(v0 - v), 0f) / k;
                                v0 = Mathf.Max(v0, v) + h * h * k * 0.25f;
                            }
                            f[x + y * nx + (z + tz * FieldXZ) * nx * ny] = v0;
                        }
                    }
                }
            }

            Vector3 L = sunToward.normalized;
            Vector3[] skyDirs =
            {
                new Vector3(0f, 1f, 0f),
                new Vector3(0.7f, 0.7f, 0f), new Vector3(-0.7f, 0.7f, 0f),
                new Vector3(0f, 0.7f, 0.7f), new Vector3(0f, 0.7f, -0.7f),
            };
            byte[] data = new byte[nx * ny * nz * 4];
            float[] tauA = new float[nx * ny * nz];
            float[] skyA = new float[nx * ny * nz];
            for (int tz = 0; tz < Templates; tz++)
            {
                for (int z = 0; z < FieldXZ; z++)
                {
                    for (int y = 0; y < ny; y++)
                    {
                        for (int x = 0; x < nx; x++)
                        {
                            int i = x + y * nx + (z + tz * FieldXZ) * nx * ny;
                            Vector3 p = new Vector3(-FieldHalf + (x + 0.5f) * cx, FieldBottom + (y + 0.5f) * cy, -FieldHalf + (z + 0.5f) * cx);
                            float tauSun = Optical(f, tz, p, L, 0.08f);
                            float sky = 0f;
                            for (int d = 0; d < skyDirs.Length; d++) sky += Mathf.Exp(-Optical(f, tz, p, skyDirs[d].normalized, 0.12f) * 6f);
                            sky /= skyDirs.Length;
                            tauA[i] = tauSun;
                            skyA[i] = sky;
                        }
                    }
                }
            }
            // Сглаживание запечённого света (по шаблону, [1 2 1] по трём осям, дважды): без полос по вокселям.
            for (int pass = 0; pass < 2; pass++)
            {
                Blur(tauA, nx, ny, FieldXZ, Templates);
                Blur(skyA, nx, ny, FieldXZ, Templates);
            }
            for (int i = 0; i < f.Length; i++)
            {
                data[i * 4 + 0] = (byte)Mathf.Clamp(Mathf.RoundToInt((f[i] + 1f) * 0.5f * 255f), 0, 255);
                data[i * 4 + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(tauA[i] / 5f * 255f), 0, 255);
                data[i * 4 + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(skyA[i] * 255f), 0, 255);
                data[i * 4 + 3] = 255;
            }
            Texture3D t = new Texture3D(nx, ny, nz, TextureFormat.RGBA32, false);
            t.name = "TowerField";
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.SetPixelData(data, 0);
            t.Apply(false, false);
            return t;
        }

        static void Blur(float[] a, int nx, int ny, int nzPer, int templates)
        {
            float[] b = new float[a.Length];
            int[] strides = { 1, nx, nx * ny };
            int[] sizes = { nx, ny, nzPer };
            for (int axis = 0; axis < 3; axis++)
            {
                int st = strides[axis], n = sizes[axis];
                for (int i = 0; i < a.Length; i++)
                {
                    int x = i % nx, y = (i / nx) % ny, z = (i / (nx * ny)) % nzPer;
                    int c = axis == 0 ? x : axis == 1 ? y : z;
                    float l = c > 0 ? a[i - st] : a[i];
                    float r = c < n - 1 ? a[i + st] : a[i];
                    b[i] = (l + 2f * a[i] + r) * 0.25f;
                }
                System.Array.Copy(b, a, a.Length);
            }
        }

        static float SampleField(float[] f, int tz, Vector3 q, out bool inside)
        {
            int nx = FieldXZ, ny = FieldY;
            float cx = 2f * FieldHalf / FieldXZ, cy = FieldHeight / ny;
            float gx = (q.x + FieldHalf) / cx - 0.5f, gy = (q.y - FieldBottom) / cy - 0.5f, gz = (q.z + FieldHalf) / cx - 0.5f;
            inside = gx > -0.5f && gy > -0.5f && gz > -0.5f && gx < nx - 0.5f && gy < ny - 0.5f && gz < FieldXZ - 0.5f;
            if (!inside) return -1f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(gx), 0, nx - 2), y0 = Mathf.Clamp(Mathf.FloorToInt(gy), 0, ny - 2), z0 = Mathf.Clamp(Mathf.FloorToInt(gz), 0, FieldXZ - 2);
            float fx = Mathf.Clamp01(gx - x0), fy = Mathf.Clamp01(gy - y0), fz = Mathf.Clamp01(gz - z0);
            float v = 0f;
            for (int dz = 0; dz < 2; dz++)
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        float w = (dx == 0 ? 1f - fx : fx) * (dy == 0 ? 1f - fy : fy) * (dz == 0 ? 1f - fz : fz);
                        v += w * f[(x0 + dx) + (y0 + dy) * nx + (z0 + dz + tz * FieldXZ) * nx * ny];
                    }
            return v;
        }

        /// <summary>Оптическая толщина (в радиусах башни при плотности 1) от точки до выхода из коробки шаблона.</summary>
        static float Optical(float[] f, int tz, Vector3 p, Vector3 dir, float ds)
        {
            int nx = FieldXZ, ny = FieldY;
            float cx = 2f * FieldHalf / FieldXZ, cy = FieldHeight / ny;
            float sum = 0f;
            Vector3 q = p + dir * (ds * 0.5f);
            for (int n = 0; n < 200; n++)
            {
                bool inside;
                float v = SampleField(f, tz, q, out inside);
                if (!inside) break;
                sum += Mathf.Clamp01(v * 4f) * ds;
                q += dir * ds;
            }
            return sum;
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
            U(r, 0f, 1f);   // поворот отключён: свет запечён под неподвижное солнце
            float rot = 0f;
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
            imp.Place(center, TemplateHeight);
        }

        void LateUpdate()
        {
            if (Application.isPlaying && cam != null)
            {
                Advance(cam.transform.position, Time.deltaTime);
                imp.Advance(cam.transform.position, wind, Time.deltaTime, TemplateHeight);
            }
        }

        /// <summary>Лист снимков импосторов для проверки в CI (через кадр по азимуту, все варианты).</summary>
        public Texture2D ImpostorSheet() { return imp.Sheet(); }

        float ClearDistance()
        {
            if (orbit == null) orbit = GetComponent<OrbitCamera>();
            return orbit != null ? orbit.distance : 0f;
        }

        void SetupMaterial(Texture3D n)
        {
            float t = Application.isPlaying ? Time.time : 0f;
            float sunScale = sun != null ? sun.intensity / 1.45f : 1f;
            mat.SetTexture("_Noise3D", n);
            mat.SetMatrix("_InvProj", cam.projectionMatrix.inverse);
            mat.SetMatrix("_CamToWorld", cam.cameraToWorldMatrix);
            mat.SetTexture("_TowerField", field);
            mat.SetVector("_FieldBox", new Vector4(FieldHalf, FieldBottom, FieldHeight, Templates));
            mat.SetVector("_Haze", new Vector4(hazeDensity, hazeTop, hazeDistance, hazeSteps));
            mat.SetVector("_Deck", new Vector4(deckBase, deckTop, deckDensity, deckSteps));
            mat.SetVector("_Deck2", new Vector4(deckWeatherScale, deckCoverage, deckLobeScale, deckDetailScale));
            mat.SetVector("_Deck3", new Vector4(deckDistance, 1600f, deckAbsorption, deckSunScale));
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
            // Чистая зона до корабля: облако не встаёт между камерой и кораблём.
            float clear = ClearDistance();
            mat.SetVector("_NearClear", new Vector4(Mathf.Max(30f, clear * 0.9f), Mathf.Max(140f, clear + 70f), 0f, 0f));
            Transform shipT = orbit != null ? orbit.target : null;
            mat.SetVector("_ShipClear", shipT != null ? new Vector4(shipT.position.x, shipT.position.y + 8f, shipT.position.z, 30f) : Vector4.zero);

            mat.SetFloat("_Bake", 0f);
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

            SetupMaterial(n);
            if (impostors && debugView == 0 && imp.NeedsBake)
            {
                if (impostorShader == null) impostorShader = Shader.Find("Hidden/SkyTide/CloudImpostor");
                if (impostorShader != null && impostorShader.isSupported)
                {
                    float bt = Time.realtimeSinceStartup;
                    bool ok = imp.Bake(mat, field, TemplateHeight, new Vector4(FieldHalf, FieldBottom, FieldHeight, Templates), impostorBakeSteps);
                    Debug.Log("SkyTide: импосторы облаков " + (ok ? "запечены" : "не поддерживаются") + " за " + Mathf.RoundToInt((Time.realtimeSinceStartup - bt) * 1000f) + " мс");
                    if (!ok) impostors = false;
                    SetupMaterial(n);
                }
                else impostors = false;
            }

            int w = Mathf.Max(16, src.width / resolutionDivider);
            int h = Mathf.Max(16, src.height / resolutionDivider);
            mat.SetVector("_LowRes", new Vector4(w, h, 1f / w, 1f / h));
            RenderTexture clouds = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBHalf);
            RenderTextureFormat depthFmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf) ? RenderTextureFormat.RHalf : RenderTextureFormat.ARGBHalf;
            RenderTexture lowDepth = RenderTexture.GetTemporary(w, h, 0, depthFmt);
            clouds.filterMode = FilterMode.Point;
            lowDepth.filterMode = FilterMode.Point;
            // Нельзя размывать до получения глубины: важны границы облаков и корабля.
            Graphics.Blit(src, clouds, mat, 0);
            Graphics.Blit(src, lowDepth, mat, 2);
            mat.SetTexture("_LowDepthTex", lowDepth);
            RenderTexture blurred = null;
            RenderTexture filtered = clouds;
            if (blurEnabled && debugView == 0)
            {
                blurred = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBHalf);
                blurred.filterMode = FilterMode.Point;
                Graphics.Blit(clouds, blurred, mat, 3);
                filtered = blurred;
            }
            mat.SetTexture("_CloudTex", filtered);
            Graphics.Blit(src, dst, mat, 1);
            if (impostors && debugView == 0)
            {
                float clear = ClearDistance();
                imp.deckTop = deckTop;
                imp.Draw(cam, dst, impostorShader, filtered, towers, towerRot, towerCount, TemplateHeight, aerial,
                         new Vector2(Mathf.Max(30f, clear * 0.9f), Mathf.Max(140f, clear + 70f)));
            }
            if (debugView == 0 && FxSystem.Instance != null) FxSystem.Instance.DrawExternal(cam, dst);
            if (blurred != null) RenderTexture.ReleaseTemporary(blurred);
            RenderTexture.ReleaseTemporary(lowDepth);
            RenderTexture.ReleaseTemporary(clouds);
        }
    }
}
