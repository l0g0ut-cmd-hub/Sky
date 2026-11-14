using UnityEngine;
using UnityEngine.Rendering;

namespace SkyTide
{
    /// <summary>
    /// Точка входа прототипа. Создаёт свет, камеру, мир облаков, корабль и управление.
    /// В сцене лежит только этот объект (его ставит SceneBuilder) — остальное собирается при запуске.
    /// </summary>
    public class SkyTideBootstrap : MonoBehaviour
    {
        public GameMaterials materials = new GameMaterials();

        [Header("Модели корабля (переключатель)")]
        [Tooltip("Варианты модели: меш из tools/convert_glb*.py и материалы. Пусто — заглушка из примитивов.")]
        public ShipVariant[] ships;
        public ShipVariant targetShip;   // корабль-мишень (модель Meshy)
        [Header("Небесная крепость-причал (Models/SkyFortress*, tools/convert_glb.py + tools/fortress_data.py)")]
        public TextAsset fortressMesh, fortressData;
        public Material[] fortressMaterials;
        public Vector3 fortressOffset = new Vector3(1000f, 0f, 1732f);   // от точки появления: ~30° левее носа, 2 км (вне зоны 1400 м)
        [Tooltip("Во сколько раз крепость крупнее исходной модели (496×333×460 м). 3 → 1490×1000×1380 м, башни на 730 м выше причала.")]
        public float fortressScale = 3f;
        [Tooltip("Доворот крепости (°) от положения «пирс прямо на точку появления»: башни видны в три четверти.")]
        public float fortressYaw = 20f;
        public SkyFortress Fortress { get; private set; }
        public int shipIndex;

        [Tooltip("Бесшовный шум RG для анимации облаков (Art/Noise.png). Пусто — генерируется.")]
        public Texture2D noiseTexture;

        [Tooltip("Шейдер постобработки (Hidden/SkyTide/Post) — ссылка нужна, чтобы он попал в сборку.")]
        public Shader postShader;

        [Tooltip("Шейдер объёмных облаков (Hidden/SkyTide/VolumetricClouds).")]
        public Shader volumetricShader;
        public Shader impostorShader;
        public Shader fxShader;
        public Shader decalShader;   // Hidden/SkyTide/HullDecal — reference keeps it in the build
        public Shader hudShader;
        public Font hudFont;
        [Tooltip("3D-шум облаков (Art/CloudNoise3D.bytes, tools/gen_cloud_noise3d.py).")]
        public TextAsset cloudNoise3D;
        [Tooltip("Запечённые шаблоны объёмных башен: форма + свет солнца + видимость неба (SceneBuilder).")]
        public Texture3D towerField;

        [Tooltip("Объёмные облака на высоте полёта (иначе — спрайты). Переключается кнопкой на экране.")]
        public bool volumetricClouds = true;   // объёмный режим — по умолчанию (красивее и уже достаточно быстрый)

        [Header("Старт")]
        public Vector3 spawnPosition = new Vector3(0f, 60f, 0f);
        [Tooltip("Курс на старте. Солнце на панораме — в направлении +X (курс 90°).")]
        public float spawnHeading = 55f;

        public static SkyTideBootstrap Instance { get; private set; }

        public ShipController Ship { get; private set; }
        public OrbitCamera Cam { get; private set; }
        public CloudSea World { get; private set; }

        bool initialized;

        void Awake()
        {
            Init();
        }

        /// <summary>Собирает всё. Вызывается из Awake, а в редакторе — для кадров в CI (там Awake не срабатывает).</summary>
        public void Init()
        {
            if (initialized) return;
            initialized = true;
            Instance = this;
            materials.EnsureAll();

            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            ApplyEnvironment(materials);
            Light sun = SetupSun();
            RenderSettings.sun = sun;
            SetShaderGlobals(materials, sun, noiseTexture);
            OrbitCamera cam = SetupCamera();
            Cam = cam;
            // Порядок важен: сначала объёмные облака, потом постобработка поверх них.
            VolumetricClouds vol = cam.GetComponent<VolumetricClouds>();
            if (vol == null) vol = cam.gameObject.AddComponent<VolumetricClouds>();
            vol.shader = volumetricShader;
            vol.impostorShader = impostorShader;
            vol.sun = sun;
            vol.noiseBytes = cloudNoise3D != null ? cloudNoise3D.bytes : null;
            vol.bakedField = towerField;
            Volumetric = vol;
            SkyTidePost post = cam.GetComponent<SkyTidePost>();
            if (post == null) post = cam.gameObject.AddComponent<SkyTidePost>();
            post.shader = postShader;
            post.sun = sun;

            FxSystem fx = GetComponent<FxSystem>();
            if (fx == null) fx = gameObject.AddComponent<FxSystem>();
            fx.shader = fxShader;
            fx.volumetric = vol;
            fx.deckTop = vol.deckTop;
            fx.wind = vol.wind;
            fx.ConfigureSurface(keepNoise as Texture2D, vol.noiseBytes);
            Fx = fx;
            FxSystem.Instance = fx;

            World = gameObject.AddComponent<CloudSea>();
            World.Build(materials);
            SetVolumetric(volumetricClouds);

            Ship = ShipFactory.Build("Player Ship", spawnPosition, spawnHeading, materials, CurrentShip);
            PlayerHull hull = Ship.GetComponent<PlayerHull>();
            if (hull == null) hull = Ship.gameObject.AddComponent<PlayerHull>();
            hull.Setup(Ship);

            if (targetShip != null && targetShip.IsValid)
            {
                ShipController t = ShipFactory.Build("Target Ship", spawnPosition + new Vector3(260f, 0f, 220f), spawnHeading + 140f, materials, targetShip);
                Target = t.gameObject.AddComponent<TargetShip>();
                Target.Setup(t, Ship.transform, materials.darkWood,targetShip.mesh);
                if (Application.isPlaying) t.gameObject.AddComponent<EnemyGunner>().Setup(Target, t);
            }

            if (fortressMesh != null && fortressMaterials != null && fortressMaterials.Length > 0)
            {
                Mesh fm = ShipMeshLoader.Load(fortressMesh);
                if (fm != null)
                {
                    Fortress = new GameObject("Sky Fortress").AddComponent<SkyFortress>();
                    Fortress.Setup(fm, fortressMaterials, fortressData, spawnPosition + fortressOffset, spawnPosition, fortressYaw, spawnPosition.y + 1.5f, fortressScale);
                }
            }

            // Ход боя: волны, счёт, замедление при потоплении (только в игре; в кадрах CI — значения по умолчанию).

            cam.target = Ship.transform;
            cam.Snap();

            TouchControls controls = gameObject.AddComponent<TouchControls>();
            controls.ship = Ship;
            controls.cam = cam;
            controls.game = this;
            controls.hudShader = hudShader;
            controls.hudFont = hudFont;
            post.controls = controls;
            SetClothWind(Application.isPlaying ? Time.time : 0f, Application.isPlaying ? 1f : 0f);
        }

        void Update(){long __pt=PerfTimers.Start();UpdateTimed();PerfTimers.Stop("Мир",__pt);}
        void UpdateTimed()
        {
            if (Application.isPlaying) SetClothWind(Time.time, 1f);
        }

        void SetClothWind(float time, float amount)
        {
            Vector3 flow = Volumetric != null ? Volumetric.wind : new Vector3(-4f,0f,1.8f);
            if (Ship != null) flow -= Ship.transform.forward * Ship.Speed;
            Shader.SetGlobalVector("_ShipClothWind", new Vector4(flow.x,flow.y,flow.z,time));
            Shader.SetGlobalFloat("_ShipClothAmount", Mathf.Clamp01(amount));
        }

        void OnDisable() { Shader.SetGlobalFloat("_ShipClothAmount", 0f); }

#if UNITY_EDITOR
        public void SetWindCapture(float time, float amount) { SetClothWind(time, amount); }
#endif

        public VolumetricClouds Volumetric { get; private set; }
        public FxSystem Fx { get; private set; }
        public TargetShip Target { get; private set; }

        /// <summary>Объёмные облака вкл/выкл: спрайты на высоте полёта показываются, только когда объёма нет.</summary>
        public void SetVolumetric(bool on)
        {
            volumetricClouds = on;
            if (Volumetric != null) Volumetric.enabled = on;
            // В объёмном режиме кучевые спрайты заменяют импосторы — снимки настоящих объёмных облаков.
            if (World != null) World.SetSpriteCloudsVisible(!on || Volumetric == null || !Volumetric.impostors);
            // Объёмные башни дополняют рисованные облака, а не заменяют их — средний ярус спрайтов остаётся.
        }

        public ShipVariant CurrentShip
        {
            get { return ships != null && ships.Length > 0 ? ships[Mathf.Clamp(shipIndex, 0, ships.Length - 1)] : null; }
        }

        public string CurrentShipName
        {
            get { ShipVariant v = CurrentShip; return v != null && !string.IsNullOrEmpty(v.name) ? v.name : "Заглушка"; }
        }

        /// <summary>Следующая модель корабля (кнопка на экране).</summary>
        public void NextShip()
        {
            if (ships == null || ships.Length < 2 || Ship == null) return;
            shipIndex = (shipIndex + 1) % ships.Length;
            ShipFactory.SetModel(Ship, materials, CurrentShip);
        }

        /// <summary>Небо, туман и фоновый свет заката. Вызывается и редактором, чтобы настройки сохранились в сцене
        /// (иначе при сборке вырежутся варианты шейдеров с туманом).</summary>
        public static void ApplyEnvironment(GameMaterials m)
        {
            if (m.skybox != null) RenderSettings.skybox = m.skybox;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            // Цвета сняты с панорамы неба: туман — горизонт, фон — верх неба / горизонт / море облаков.
            RenderSettings.fogColor = new Color(0.69f, 0.29f, 0.25f);
            RenderSettings.fogStartDistance = 300f;
            RenderSettings.fogEndDistance = 2300f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.43f, 0.49f, 0.62f);
            RenderSettings.ambientEquatorColor = new Color(0.68f, 0.46f, 0.35f);
            RenderSettings.ambientGroundColor = new Color(0.34f, 0.29f, 0.32f);
            if (m.skyReflection != null)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = m.skyReflection;
                RenderSettings.reflectionIntensity = 1f;
            }
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
        }

        /// <summary>Общие параметры для шейдеров облаков (SkyTide.cginc).</summary>
        static Texture keepNoise;   // держим ссылку, чтобы созданная на лету текстура не выгрузилась

        static void SetShaderGlobals(GameMaterials m, Light sun, Texture2D noise)
        {
            keepNoise = noise != null ? (Texture)noise : SkyNoise.Flow(128);
            Shader.SetGlobalTexture("_SkyTideNoise", keepNoise);
            Shader.SetGlobalVector("_SkyTideSunDir", -sun.transform.forward);
            Shader.SetGlobalFloat("_SkyTideRim", 1f);   // контровой свет заката на кораблях и крепости (SkyTide.cginc)
            Texture pano = null;
            if (m.skybox != null && m.skybox.HasProperty("_MainTex")) pano = m.skybox.GetTexture("_MainTex");
            if (pano == null)
            {
                Texture2D t = new Texture2D(1, 1);
                t.SetPixel(0, 0, RenderSettings.fogColor);
                t.Apply();
                pano = t;
            }
            Shader.SetGlobalTexture("_SkyTidePanorama", pano);
            // Воздушная перспектива: с 450 м облака начинают растворяться в небе, к 2200 м — почти полностью.
            Shader.SetGlobalVector("_SkyTideAerial", new Vector4(450f, 2200f, 0.96f, 0f));
        }

        /// <summary>Destroy в игре, DestroyImmediate в редакторе (кадры для CI собираются вне Play Mode).</summary>
        public static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        public static void ConfigureSun(Light l)
        {
            l.type = LightType.Directional;
            l.color = new Color(1f, 0.6f, 0.34f);
            l.intensity = 1.45f;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.75f;
            l.transform.rotation = Quaternion.Euler(7f, -90f, 0f);   // свет идёт из +X — туда, где солнце на панораме
        }

        static Light SetupSun()
        {
            Light found = null;
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional) { found = l; break; }
            }
            if (found == null) found = new GameObject("Sun").AddComponent<Light>();
            ConfigureSun(found);
            QualitySettings.shadowDistance = 160f;
            QualitySettings.shadows = ShadowQuality.All;
            if ((int)QualitySettings.shadowResolution < (int)ShadowResolution.High)
                QualitySettings.shadowResolution = ShadowResolution.High;
            if (QualitySettings.shadowCascades < 2) QualitySettings.shadowCascades = 2;
            Debug.Log("SkyTide: тени корабля " + QualitySettings.shadows + ", " + QualitySettings.shadowResolution
                + ", каскадов=" + QualitySettings.shadowCascades + "; анизотропия=" + QualitySettings.anisotropicFiltering);
            return found;
        }

        static OrbitCamera SetupCamera()
        {
            Camera c = Camera.main;
            if (c == null)
            {
                GameObject go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                c = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            ConfigureCamera(c);
            OrbitCamera oc = c.GetComponent<OrbitCamera>();
            if (oc == null) oc = c.gameObject.AddComponent<OrbitCamera>();
            return oc;
        }

        public static void ConfigureCamera(Camera c)
        {
            c.clearFlags = CameraClearFlags.Skybox;
            c.nearClipPlane = 0.5f;
            c.farClipPlane = 6500f;   // крепость (до 3 км от старта, поле 3.6 км) не должна обрезаться дальней плоскостью
            c.fieldOfView = 55f;
        }
    }
}
