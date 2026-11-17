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
        /// <summary>Флот по классам (ShipClass: катер … адмиральский) — модели Meshy Models/Fleet/Ship_&lt;id&gt;. Нет модели — Корсар.</summary>
        public ShipVariant[] fleet;
        [Header("Строения на карте (Models/Props, tools/convert_props.py)")]
        public PropAsset[] props;
        public bool HasFleet { get { return fleet != null && fleet.Length == ShipClasses.Count && fleet[(int)ShipClass.Brig] != null && fleet[(int)ShipClass.Brig].IsValid; } }
        /// <summary>Модель класса или Корсар, если модели класса нет.</summary>
        public ShipVariant ClassVariant(ShipClass c)
        {
            if (fleet != null && (int)c >= 0 && (int)c < fleet.Length && fleet[(int)c] != null && fleet[(int)c].IsValid) return fleet[(int)c];
            return targetShip;
        }
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
            if (!Application.isPlaying) { Init(); return; }
            // Сборка мира — 6–7 с на телефоне в одном кадре. Раньше она шла здесь, в Awake, до первого кадра, и весь
            // этот срок между заставкой Unity и заставкой студии висел серый экран (владелец 15.11). Теперь сначала
            // показывается заставка студии, а мир собирается под ней: камера и эффекты до сборки выключены.
            Camera c0 = Camera.main; if (c0 != null) c0.enabled = false;
            FxSystem f0 = GetComponent<FxSystem>(); if (f0 != null) f0.enabled = false;
            StartCoroutine(DeferredInit(c0, f0));
        }

        System.Collections.IEnumerator DeferredInit(Camera c0, FxSystem f0)
        {
            float t0 = Time.realtimeSinceStartup;
            yield return null;
            // Ждём, пока заставка студии проявится полностью (проявление 0,25 с), не дольше 1,5 с.
            while (StudioSplash.Active && StudioSplash.Elapsed < 0.35f && Time.realtimeSinceStartup - t0 < 1.5f) yield return null;
            yield return null;
            if (c0 != null) c0.enabled = true;
            if (f0 != null) f0.enabled = true;
            Init();
        }

        // Замер этапов сборки мира (серый экран при запуске, владелец 15.11) — строкой в журнал событий.
        float initT0, markAt; readonly System.Text.StringBuilder marks = new System.Text.StringBuilder();
        void Mark(string what) { float now = Time.realtimeSinceStartup; marks.Append(' ').Append(what).Append(' ').Append((now - markAt).ToString("F1")).Append(';'); markAt = now; }

        /// <summary>Собирает всё. Вызывается из Awake, а в редакторе — для кадров в CI (там Awake не срабатывает).</summary>
        public void Init()
        {
            if (initialized) return;
            initialized = true;
            initT0 = Time.realtimeSinceStartup; markAt = initT0; marks.Length = 0;
            Instance = this;
            materials.EnsureAll();
            Mark("материалы");

            // Потолок 30 кадров: телефон владельца (Mali-G76) 60 не держит ни в одном режиме, а без потолка греется и
            // сбрасывает частоты — те же кадры становятся в 1.5–2 раза дороже (автозамеры 89/92/105). Ровные 30 лучше
            // плавающих 20–40. Переключатель «60 кадров» — в панели FPS, запоминается.
            Application.targetFrameRate = Application.isPlaying && PlayerPrefs.GetInt(Fps60Key, 0) == 0 ? 30 : 60;
            // Облака в игре по умолчанию рисованные: владелец по видео сборки 105 — «со всем выключенным нравится больше»
            // (объёмные на телефоне — мягкая серая вата в ¼ разрешения) и они же стоят 8–18 мс видеокарты.
            // Объёмные — строкой «ОБЛАКА» в меню паузы, выбор запоминается. В кадрах CI — как задано в сцене.
            if (Application.isPlaying) volumetricClouds = PlayerPrefs.GetInt(VolumetricKey, 0) == 1;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            if (Application.isPlaying) { DebugReport.Hook(); if (GetComponent<DebugRecorder>() == null) gameObject.AddComponent<DebugRecorder>(); if (GetComponent<ReportUploader>() == null) gameObject.AddComponent<ReportUploader>(); if (GetComponent<RemoteConfig>() == null) gameObject.AddComponent<RemoteConfig>(); AddOnce<RemoteCommands>(); AddOnce<ControlBanner>(); AddOnce<CrashWatch>(); AddOnce<UpdateCheck>(); AddOnce<ReplayRecorder>(); WorldLog.Boot(); if (CiBattle.Active) AddOnce<CiBattle>(); }
            if (Application.isPlaying)
            {
                // Физики в игре нет (столкновения и попадания считаются своим кодом) — её шаг каждый кадр не нужен.
                Physics.simulationMode = SimulationMode.Script;
                Physics2D.simulationMode = SimulationMode2D.Script;
                // Системе не хватает памяти — отдаём всё незанятое, а не ждём, пока она закроет игру.
                Application.lowMemory -= FreeMemory; Application.lowMemory += FreeMemory;
            }

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
            if (Application.isPlaying && GetComponent<ShipWake>() == null) gameObject.AddComponent<ShipWake>();   // след за кормой
            fx.shader = fxShader;
            fx.volumetric = vol;
            fx.deckTop = vol.deckTop;
            fx.wind = vol.wind;
            fx.ConfigureSurface(keepNoise as Texture2D, vol.noiseBytes);
            Mark("эффекты/облака-шум");
            Fx = fx;
            FxSystem.Instance = fx;

            World = gameObject.AddComponent<CloudSea>();
            World.Build(materials);
            Mark("море облаков");
            SetVolumetric(volumetricClouds);

            Ship = ShipFactory.Build("Player Ship", spawnPosition, spawnHeading, materials, CurrentShip);
            Mark("флагман");
            PlayerHull hull = Ship.GetComponent<PlayerHull>();
            if (hull == null) hull = Ship.gameObject.AddComponent<PlayerHull>();
            if (Application.isPlaying)
            {
                // Свой корабль — флагман, вдвое-втрое сильнее рядового врага (просьба владельца): ход 40 против 20 м/с,
                // поворот 50 против 24 °/с, корпус 90 попаданий против 30, ядро 125 против 85 м/с (предел ~1.2 км против
                // 420 м у врага), наводка без разброса против пристрелки врага с ошибкой.
                Ship.maxSpeed = 40f; Ship.maxYawRate = 50f; Ship.acceleration = 9f; Ship.deceleration = 11f;
                hull.maxHealth = 540f;
                Broadside flag = Ship.GetComponent<Broadside>();
                if (flag != null) { flag.muzzleSpeed = 125f; flag.maxElevationDeg = 14f; }   // ядро живёт 7 с: при 14° предел ~750 м
            }
            hull.Setup(Ship);

            if (targetShip != null && targetShip.IsValid)
            {
                ShipVariant tv = ClassVariant(ShipClass.Brig);
                ShipController t = ShipFactory.Build("Target Ship", spawnPosition + new Vector3(260f, 0f, 220f), spawnHeading + 140f, materials, tv);
                Target = t.gameObject.AddComponent<TargetShip>();
                Target.Setup(t, Ship.transform, materials.darkWood,tv.mesh,tv.lights);
                if (Application.isPlaying)
                {
                    EnemyGunner eg = t.gameObject.AddComponent<EnemyGunner>();
                    eg.Setup(Target, t);
                    t.maxSpeed = 20f; t.maxYawRate = 24f;
                    eg.maxRange = 420f; eg.rangeError = 0.11f; eg.sideError = 0.04f;
                    Target.retreatRange = 380f;
                }
                if (tv != targetShip) Target.SetClass(ShipClass.Brig);
                // Экипаж на палубе врага — та же модель матроса, что у своего корабля (в кадрах CI тоже: виден на 211–213).
                ShipVariant own = CurrentShip;
                if (own != null && own.crew != null && own.crew.IsValid) t.gameObject.AddComponent<EnemyCrew>().Setup(Target, own.crew);
            }

            if (fortressMesh != null && fortressMaterials != null && fortressMaterials.Length > 0)
            {
                Mesh fm = ShipMeshLoader.Load(fortressMesh);
                if (fm != null)
                {
                    Fortress = new GameObject("Sky Fortress").AddComponent<SkyFortress>();
                    Fortress.Setup(fm, fortressMaterials, fortressData, spawnPosition + fortressOffset, spawnPosition, fortressYaw, spawnPosition.y + 1.5f, fortressScale);
                }
                // Маяк, башни, склад, мачта, обломки, буи, логова — по крепости и границам вод.
                if (Fortress != null && props != null && props.Length > 0) WorldProps.Create(props, Fortress, spawnPosition);
            }

            // Второй враг — разведчик (только в игре): тот же корпус в 0.72 размера, быстрее и слабее рядового, держится
            // ближе и уклоняется чаще. Двое на одного — флагман сильнее любого втрое, так бой не превращается в избиение.
            if (Application.isPlaying && Target != null && targetShip != null && targetShip.IsValid)
            {
                ShipVariant crewFrom2 = CurrentShip;
                ShipVariant sv = ClassVariant(ShipClass.Sloop);
                ShipController s = ShipFactory.Build("Scout Ship", spawnPosition + new Vector3(-420f, 0f, 520f), spawnHeading - 120f, materials, sv);
                if (sv == targetShip) s.transform.localScale = Vector3.one * 0.72f;
                Scout = s.gameObject.AddComponent<TargetShip>();
                Scout.lampSlot = -1;
                Scout.Setup(s, Ship.transform, materials.darkWood, sv.mesh, sv.lights);
                Scout.hitsToSink = 16; Scout.spawnRange = 1500f;
                s.maxSpeed = 27f; s.maxYawRate = 34f;
                EnemyGunner sg = s.gameObject.AddComponent<EnemyGunner>();
                sg.Setup(Scout, s);
                sg.gunsPerSide = 3; sg.maxRange = 360f; sg.cooldown = 5f; sg.rangeError = 0.12f; sg.sideError = 0.045f;
                Scout.retreatRange = 340f;
                if (sv != targetShip) Scout.SetClass(ShipClass.Sloop);
                // Второй рядовой — заходит с другой стороны; иногда (при возрождении) любой рядовой выходит тяжёлым.
                ShipVariant rv = ClassVariant(ShipClass.Corvette);
                ShipController e2 = ShipFactory.Build("Raider Ship", spawnPosition + new Vector3(640f, 0f, -380f), spawnHeading + 60f, materials, rv);
                Raider = e2.gameObject.AddComponent<TargetShip>();
                Raider.lampSlot = -1;
                Raider.Setup(e2, Ship.transform, materials.darkWood, rv.mesh, rv.lights);
                e2.maxSpeed = 20f; e2.maxYawRate = 24f;
                EnemyGunner rg = e2.gameObject.AddComponent<EnemyGunner>();
                rg.Setup(Raider, e2);
                rg.maxRange = 420f; rg.rangeError = 0.11f; rg.sideError = 0.04f;
                Raider.retreatRange = 380f; Raider.canBeHeavy = true; Target.canBeHeavy = true;
                if (rv != targetShip) Raider.SetClass(ShipClass.Corvette);
                if (crewFrom2 != null && crewFrom2.crew != null && crewFrom2.crew.IsValid) e2.gameObject.AddComponent<EnemyCrew>().Setup(Raider, crewFrom2.crew);
                // Ещё трое по углам карты (2 рядовых, 1 разведчик): бродят своими курсами, в бой — когда увидят.
                Vector3[] extra = { new Vector3(-2200f, 0f, -1600f), new Vector3(2400f, 0f, 1500f), new Vector3(-600f, 0f, -2600f) };
                ShipClass[] extraClass = { ShipClass.Brig, ShipClass.Frigate, ShipClass.Sloop };
                for (int k = 0; k < extra.Length; k++) SpawnEnemy("Enemy " + (k + 4), spawnPosition + extra[k], Random.Range(0f, 360f), extraClass[k], crewFrom2);
                EnsureEnemies(RemoteConfig.Current != null ? RemoteConfig.Current.enemyCount : 32);
                ShipVariant crewFrom = CurrentShip;
                if (crewFrom != null && crewFrom.crew != null && crewFrom.crew.IsValid) s.gameObject.AddComponent<EnemyCrew>().Setup(Scout, crewFrom.crew);
            }

            if (Application.isPlaying && GetComponent<WeatherSystem>() == null) gameObject.AddComponent<WeatherSystem>();

            // Союзник — сторожевой корабль крепости (только в игре; в кадрах CI его нет): та же модель, что у врага,
            // в светлом тоне, без пушек игрока и экипажа. Выходит в дозор у края безопасной зоны.
            if (Application.isPlaying && Fortress != null && targetShip != null && targetShip.IsValid)
            {
                // Свои: два сторожевых по разные стороны зоны, ведомый у флагмана, торговец идёт к причалу издалека.
                Vector3 toSpawn = (spawnPosition - Fortress.Center); toSpawn.y = 0f; toSpawn.Normalize();
                for (int g = 0; g < 2; g++)
                {
                    Vector3 guard = Fortress.Center + Quaternion.Euler(0f, g == 0 ? 0f : 180f, 0f) * toSpawn * (Fortress.safeRadius - 180f); guard.y = spawnPosition.y;
                    ShipVariant gv = ClassVariant(ShipClass.Brig);
                    ShipController a = ShipFactory.Build("Guard Ship " + g, guard, spawnHeading + 90f + g * 180f, materials, gv);
                    AllyShip al = a.gameObject.AddComponent<AllyShip>();
                    al.Setup(a, AllyShip.Role.Guard, gv.mesh, materials.darkWood);
                    if (gv != targetShip) al.SetClass(ShipClass.Brig);
                    if (g == 0) Ally = al;
                }
                ShipVariant wv = ClassVariant(ShipClass.Frigate);
                ShipController w = ShipFactory.Build("Wingman", spawnPosition + new Vector3(130f, 0f, -90f), spawnHeading, materials, wv);
                AllyShip wa = w.gameObject.AddComponent<AllyShip>(); wa.Setup(w, AllyShip.Role.Wingman, wv.mesh, materials.darkWood);
                if (wv != targetShip) wa.SetClass(ShipClass.Frigate);
                // Торговца нет (владелец 16.11: «торговца вырезать вообще»). Жизнь гарнизона: два таможенных катера у края вод
                // гарнизона (выпроваживают чужих) и два курьера по делам (бриг и фрегат) — всего свои 7 с ведомым.
                // Владелец 17.11: «немного больше кораблей» — по третьему таможеннику и курьеру (свои 13).
                AllyShip.Role[] roles = { AllyShip.Role.Customs, AllyShip.Role.Customs, AllyShip.Role.Customs, AllyShip.Role.Courier, AllyShip.Role.Courier, AllyShip.Role.Courier };
                ShipClass[] rc = { ShipClass.Cutter, ShipClass.Cutter, ShipClass.Cutter, ShipClass.Clipper, ShipClass.Brig, ShipClass.Corvette };
                for (int r = 0; r < roles.Length; r++)
                {
                    bool customs = roles[r] == AllyShip.Role.Customs;
                    float ang = (r * 60f + 30f) * Mathf.Deg2Rad;
                    Vector3 at = Fortress.Center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (customs ? Relations.GarrisonRadius - 250f : Fortress.safeRadius + 300f); at.y = spawnPosition.y;
                    ShipVariant v = ClassVariant(rc[r]);
                    ShipController c = ShipFactory.Build((customs ? "Customs " : "Courier ") + (r % 3 + 1), at, r * 60f, materials, v);
                    if (v == targetShip && rc[r] == ShipClass.Cutter) c.transform.localScale = Vector3.one * 0.6f;
                    AllyShip al2 = c.gameObject.AddComponent<AllyShip>(); al2.Setup(c, roles[r], v.mesh, materials.darkWood);
                    if (v != targetShip) al2.SetClass(rc[r]);
                }
                if (HasFleet) SpawnHarbor();
            }

            // Ход боя: волны, счёт, замедление при потоплении (только в игре; в кадрах CI — значения по умолчанию).

            Mark("враги/крепость");
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
            if (Application.isPlaying) DebugReport.Event("мир собран за " + (Time.realtimeSinceStartup - initT0).ToString("F1") + " с (с запуска приложения " + Time.realtimeSinceStartup.ToString("F1") + " с):" + marks);
        }

        void Update(){long __pt=PerfTimers.Start();UpdateTimed();PerfTimers.Stop("Мир",__pt);}
        void UpdateTimed()
        {
            if (Application.isPlaying) SetClothWind(Time.time, 1f);
            if (Application.isPlaying) { Relations.Tick(); Traffic.Tick(); }
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
        public AllyShip Ally { get; private set; }
        public TargetShip Scout { get; private set; }
        public TargetShip Raider { get; private set; }

        /// <summary>Довести число врагов до total (конфиг enemyCount; по умолчанию 18 — владелец 15.11: «врагов ×3»).
        /// Новые — по кольцу 1.8–3.8 км от старта, не ближе 900 м к флагману, каждый третий — разведчик. Команда экипажа —
        /// только у первых 9 (палубные фигуры дальше 280 м всё равно не рисуются).</summary>
        public int EnsureEnemies(int total)
        {
            if (!Application.isPlaying || Ship == null || targetShip == null) return 0;
            TargetShip.Created.RemoveAll(x => x == null);
            int have = TargetShip.Created.Count;
            int want = Mathf.Min(total, 40) - have;
            if (want <= 0) return 0;
            // Дальние враги (1,8–3,8 км) рождаются по одному за кадр уже после старта: 12 кораблей разом в Awake давали
            // ~15 с серого экрана при каждом запуске (владелец 15.11).
            StartCoroutine(SpawnSpread(have, Mathf.Min(total, 40)));
            return want;
        }
        System.Collections.IEnumerator SpawnSpread(int from, int to)
        {
            float t0 = Time.realtimeSinceStartup;
            for (int k = from; k < to; k++)
            {
                yield return null;
                if (Ship == null || targetShip == null) yield break;
                TargetShip.Created.RemoveAll(x => x == null);
                if (TargetShip.Created.Count > k) continue;   // уже кто-то добавил (повторный конфиг)
                Vector3 at = spawnPosition;
                for (int tries = 0; tries < 12; tries++)
                {
                    // Кольцом вокруг крепости со всех сторон, в водах корсаров (владелец 17.11: «заполняй пустоту»).
                    Vector3 fc = Fortress != null ? Fortress.Center : spawnPosition;
                    float a = Random.Range(0f, Mathf.PI * 2f), r = Random.Range(Relations.CorsairRadius + 150f, Relations.CorsairRadius + 3200f);
                    at = new Vector3(fc.x + Mathf.Cos(a) * r, spawnPosition.y, fc.z + Mathf.Sin(a) * r);
                    Waters wt = Relations.At(at);
                    if (new Vector2(at.x, at.z).magnitude > Relations.FieldRadius || wt == Waters.Garrison || wt == Waters.Safe) { if (tries < 11) continue; at = Relations.CorsairPoint(spawnPosition.y); }
                    if ((at - Ship.transform.position).sqrMagnitude > 900f * 900f) break;
                }
                SpawnEnemy("Enemy " + (k + 1), at, Random.Range(0f, 360f), ShipClasses.ForEnemy(k), k < 9 ? CurrentShip : null);
            }
            DebugReport.Event("дальние враги догружены за " + (Time.realtimeSinceStartup - t0).ToString("F1") + " с");
        }

        /// <summary>Стоянка у причала (владелец 16.11): адмиральский, линейный, фрегат и бриг гарнизона стоят носом от крепости
        /// по обе стороны от места флагмана, крупные — дальше; место сдвигается наружу, пока под корпусом нет камня крепости.
        /// Иногда корабль выходит по делам или в дозор и возвращается на своё место (AllyShip.Role.Harbor).</summary>
        void SpawnHarbor()
        {
            Vector3 o = Fortress.BerthWorld - Fortress.Center; o.y = 0f; o = o.sqrMagnitude > 1f ? o.normalized : Vector3.forward;
            Vector3 across = Vector3.Cross(Vector3.up, o);
            Quaternion rot = Quaternion.LookRotation(o, Vector3.up);
            ShipClass[] cls = { ShipClass.Brig, ShipClass.Frigate, ShipClass.ShipOfLine, ShipClass.Admiral };
            float[] side = { 1f, 1f, -1f, -1f };
            float right = SkyFortress.RepairAlong + 25f, left = SkyFortress.RepairAlong + 25f;   // овал ремонта (±170 м) свободен
            for (int i = 0; i < cls.Length; i++)
            {
                ShipClassInfo k = ShipClasses.Get(cls[i]);
                float half = k.length * 0.5f, halfW = Mathf.Max(10f, k.length * 0.15f);
                float off = side[i] > 0f ? right + halfW : left + halfW;
                if (side[i] > 0f) right += halfW * 2f + 30f; else left += halfW * 2f + 30f;
                Vector3 slot = Fortress.BerthWorld + across * (off * side[i]) + o * (half + 20f);
                for (int push = 0; push < 20; push++)
                {
                    bool clear = true;
                    for (int q = -2; q <= 2 && clear; q++)
                        for (int w = -1; w <= 1 && clear; w++)
                            if (Fortress.TopAt(slot + o * (half * 0.5f * q) + across * (halfW * w)) > spawnPosition.y - 6f) clear = false;
                    if (clear) break;
                    slot += o * 25f;
                }
                slot.y = spawnPosition.y;
                ShipVariant v = ClassVariant(cls[i]);
                ShipController c = ShipFactory.Build("Harbor " + k.id, slot, rot.eulerAngles.y, materials, v);
                AllyShip al = c.gameObject.AddComponent<AllyShip>(); al.Setup(c, AllyShip.Role.Harbor, v.mesh, materials.darkWood);
                al.SetClass(cls[i]); al.SetHome(slot, rot);
            }
        }

        /// <summary>Враг на карте (только в игре) своего класса: модель, размер и огневая мощь класса (ShipClasses).
        /// Без моделей флота — прежний Корсар: рядовой (может выйти тяжёлым) или разведчик (корпус ×0.72).</summary>
        TargetShip SpawnEnemy(string name, Vector3 at, float heading, ShipClass cls, ShipVariant crewFrom)
        {
            ShipVariant v = ClassVariant(cls);
            bool classed = v != targetShip;
            bool scout = !classed && (cls <= ShipClass.Corvette || cls == ShipClass.Clipper);
            ShipController s = ShipFactory.Build(name, at, heading, materials, v);
            if (scout) s.transform.localScale = Vector3.one * 0.72f;
            TargetShip t = s.gameObject.AddComponent<TargetShip>();
            t.lampSlot = -1;
            t.Setup(s, Ship.transform, materials.darkWood, v.mesh, v.lights);
            EnemyGunner g = s.gameObject.AddComponent<EnemyGunner>();
            g.Setup(t, s);
            if (scout) { t.hitsToSink = 16; t.spawnRange = 1500f; s.maxSpeed = 27f; s.maxYawRate = 34f; g.gunsPerSide = 3; g.maxRange = 360f; g.cooldown = 5f; g.rangeError = 0.12f; g.sideError = 0.045f; t.retreatRange = 340f; }
            else { s.maxSpeed = 20f; s.maxYawRate = 24f; g.maxRange = 420f; g.rangeError = 0.11f; g.sideError = 0.04f; t.retreatRange = 380f; t.canBeHeavy = true; }
            if (classed) t.SetClass(cls);
            if (crewFrom != null && crewFrom.crew != null && crewFrom.crew.IsValid) s.gameObject.AddComponent<EnemyCrew>().Setup(t, crewFrom.crew);
            return t;
        }

        /// <summary>Объёмные облака вкл/выкл: спрайты на высоте полёта показываются, только когда объёма нет.</summary>
        static void FreeMemory() { Resources.UnloadUnusedAssets(); System.GC.Collect(); }
        void AddOnce<T>() where T : Component { if (GetComponent<T>() == null) gameObject.AddComponent<T>(); }
        public const string VolumetricKey = "SkyTide.Volumetric", Fps60Key = "SkyTide.Fps60";
        /// <summary>Выбор игрока (меню паузы, панель FPS): переключает и запоминает между запусками.</summary>
        public void ChooseVolumetric(bool on)
        {
            SetVolumetric(on);
            PlayerPrefs.SetInt(VolumetricKey, on ? 1 : 0); PlayerPrefs.Save();
        }
        public static bool Fps60 { get { return Application.targetFrameRate > 30; } }
        public static void ChooseFps60(bool on)
        {
            Application.targetFrameRate = on ? 60 : 30;
            PlayerPrefs.SetInt(Fps60Key, on ? 1 : 0); PlayerPrefs.Save();
        }
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
            QualitySettings.pixelLightCount = 0;   // кроме солнца, попиксельных источников нет — никаких добавочных проходов по геометрии
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
            c.farClipPlane = 10000f;   // крепость (в 2 км от центра, поле 7,2 км — до 9,2 км от флагмана) не обрезается дальней плоскостью
            c.fieldOfView = 55f;
        }
    }
}
