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

        [Header("Модель корабля")]
        [Tooltip("Меш из tools/convert_glb.py (Corsair.bytes). Пусто — заглушка из примитивов.")]
        public TextAsset shipMesh;

        [Header("Старт")]
        public Vector3 spawnPosition = new Vector3(0f, 60f, 0f);
        [Tooltip("Курс на старте. Солнце на панораме — в направлении +X (курс 90°).")]
        public float spawnHeading = 55f;

        public static SkyTideBootstrap Instance { get; private set; }

        public ShipController Ship { get; private set; }

        void Awake()
        {
            Instance = this;
            materials.EnsureAll();

            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            ApplyEnvironment(materials);
            Light sun = SetupSun();
            RenderSettings.sun = sun;
            OrbitCamera cam = SetupCamera();

            gameObject.AddComponent<CloudSea>().Build(materials);

            Ship = ShipFactory.Build("Player Ship", spawnPosition, spawnHeading, materials, ShipMeshLoader.Load(shipMesh));

            cam.target = Ship.transform;
            cam.Snap();

            TouchControls controls = gameObject.AddComponent<TouchControls>();
            controls.ship = Ship;
            controls.cam = cam;
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
            RenderSettings.ambientSkyColor = new Color(0.46f, 0.3f, 0.38f);
            RenderSettings.ambientEquatorColor = new Color(0.8f, 0.42f, 0.3f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.2f, 0.2f);
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
            c.farClipPlane = 4000f;
            c.fieldOfView = 55f;
        }
    }
}
