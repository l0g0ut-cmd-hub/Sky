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

        [Header("Модель корабля (необязательно)")]
        [Tooltip("FBX/префаб корабля. Пусто — заглушка из примитивов.")]
        public GameObject shipModel;
        [Tooltip("Поворот модели, чтобы нос смотрел вперёд (+Z) и палуба была сверху.")]
        public Vector3 shipModelEuler;
        [Tooltip("Длина корабля в метрах, под неё масштабируется модель.")]
        public float shipModelLength = 38f;

        [Header("Старт")]
        public Vector3 spawnPosition = new Vector3(0f, 60f, 0f);

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

            Ship = ShipFactory.Build("Player Ship", spawnPosition, 0f, materials, shipModel, shipModelEuler, shipModelLength);

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
            RenderSettings.fogColor = new Color(0.86f, 0.6f, 0.5f);
            RenderSettings.fogStartDistance = 350f;
            RenderSettings.fogEndDistance = 2400f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.6f, 0.5f, 0.58f);
            RenderSettings.ambientEquatorColor = new Color(0.72f, 0.5f, 0.4f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.17f, 0.2f);
        }

        public static void ConfigureSun(Light l)
        {
            l.type = LightType.Directional;
            l.color = new Color(1f, 0.66f, 0.4f);
            l.intensity = 1.35f;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.75f;
            l.transform.rotation = Quaternion.Euler(9f, 150f, 0f);   // солнце низко впереди от стартового курса, как на концепте
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
