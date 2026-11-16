using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace SkyTide
{
    /// <summary>
    /// Баланс без пересборки: при запуске игра читает Sky-reports/config/config.json (тем же токеном отчётов) и
    /// применяет числа к своему кораблю, врагам, своим, автобою, погоде; переключатели фич. Последний удачный конфиг
    /// хранится на телефоне (PlayerPrefs) и применяется сразу при следующем запуске, даже без сети.
    /// В отчёте строка «конфиг: версия N, применён …». Нет токена/файла — всё как в коде.
    /// </summary>
    public class RemoteConfig : MonoBehaviour
    {
        [System.Serializable]
        public class Data
        {
            public int version;
            public string note = "";
            // свой корабль
            public float playerSpeed = 40f, playerYaw = 50f, playerHealth = 540f, playerShellSpeed = 125f, playerElevation = 14f, playerCooldown = 2.2f;
            // враги
            public int enemyCount = 26;
            public float cullDistance = 2200f;
            public float enemySpeed = 20f, enemyYaw = 24f, enemyRange = 420f, enemyError = 0.11f, enemySideError = 0.04f, enemyCooldown = 6f;
            public int enemyHits = 30, scoutHits = 16;
            public float scoutSpeed = 27f, scoutRange = 360f, heavyChance = 0.22f, aggroRange = 1300f, enemyDamage = 6f;
            // свои
            public int allyCount = 11;
            // отношения (Core/Relations): пояса вод от центра крепости, предупреждение, прицел, остывание, злоба и набеги
            public float garrisonRadius = 2300f, corsairRadius = 3300f, warnSeconds = 30f, aimSeconds = 6f, aimRange = 400f, calmAfter = 45f;
            public float playerDamage = 2f, autoAssist = 7f;   // урон ядра флагмана (×), доводка автобоя (м от борта)
            public float raidGrudge = 40f, raidEveryMin = 10f, grudgeCoolPerMin = 3f, outlawMin = 5f;
            public int guardHits = 40, wingmanHits = 34, merchantHits = 20;
            public float allyEngage = 750f;
            // автобой
            public float autoRepairBelow = 0.35f;
            // мир
            public bool weather = true, boarding = true, wind = true;
            public float windMul = 1f, clearMinutesMin = 5f, clearMinutesMax = 8f;
        }

        public static RemoteConfig Instance { get; private set; }
        public static Data Current = new Data();
        public static string Status = "конфиг: по умолчанию (код)";
        const string CacheKey = "SkyTide.RemoteConfig";

        void Awake()
        {
            Instance = this;
            string cached = PlayerPrefs.GetString(CacheKey, "");
            if (cached.Length > 2 && TryParse(cached, out Data d)) { Current = d; Status = "конфиг: версия " + d.version + " (с телефона, ждём сеть)"; }
        }

        IEnumerator Start()
        {
            yield return null; yield return null;   // корабли уже созданы
            Apply();
            yield return Fetch();
        }

        /// <summary>Перечитать config.json с GitHub (меню тестера, удалённая команда config).</summary>
        public void Reload() { StartCoroutine(Fetch()); }

        IEnumerator Fetch()
        {
            TextAsset t = Resources.Load<TextAsset>("ReportToken");
            string token = t != null ? t.text.Trim() : null;
            if (string.IsNullOrEmpty(token)) yield break;
            using (UnityWebRequest req = UnityWebRequest.Get("https://api.github.com/repos/" + ReportUploader.Repo + "/contents/config/config.json"))
            {
                req.SetRequestHeader("Authorization", "Bearer " + token);
                req.SetRequestHeader("Accept", "application/vnd.github.raw+json");
                req.SetRequestHeader("User-Agent", "SkyTide");
                req.timeout = 15;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { Status += " · сеть: ошибка " + req.responseCode; yield break; }
                string json = req.downloadHandler.text;
                if (!TryParse(json, out Data d)) { Status = "конфиг: файл не разобран"; yield break; }
                Current = d;
                PlayerPrefs.SetString(CacheKey, json); PlayerPrefs.Save();
                Status = "конфиг: версия " + d.version + " (с GitHub) " + d.note;
                Apply();
                DebugReport.Event("конфиг v" + d.version + " применён");
            }
        }

        static bool TryParse(string json, out Data d)
        {
            d = null;
            try { d = JsonUtility.FromJson<Data>(json); } catch (System.Exception) { d = null; }
            return d != null;
        }

        /// <summary>Применить текущий конфиг ко всему, что уже есть в мире.</summary>
        public static void Apply()
        {
            Data c = Current;
            SkyTideBootstrap g = SkyTideBootstrap.Instance;
            if (g == null || g.Ship == null) return;
            g.Ship.maxSpeed = c.playerSpeed; g.Ship.maxYawRate = c.playerYaw;
            PlayerHull ph = PlayerHull.Instance;
            if (ph != null) ph.SetMaxHealth(c.playerHealth);
            Broadside b = g.Ship.GetComponent<Broadside>();
            if (b != null) { b.muzzleSpeed = c.playerShellSpeed; b.maxElevationDeg = c.playerElevation; b.cooldown = c.playerCooldown; }
            ShipLod.CullDistance = Mathf.Max(600f, c.cullDistance);
            SkyTideBootstrap boot = SkyTideBootstrap.Instance;
            if (boot != null) boot.EnsureEnemies(c.enemyCount);
            int enemies = 0;
            TargetShip.Created.RemoveAll(x => x == null);
            foreach (TargetShip t in TargetShip.Created.ToArray())
            {
                if (t == null) continue;
                bool scout = t.transform.lossyScale.x < 0.9f;
                EnemyGunner eg = t.GetComponent<EnemyGunner>();
                if (eg != null) { eg.rangeError = c.enemyError; eg.sideError = c.enemySideError; }
                if (t.Class != ShipClass.None)
                    // Классы: числа конфига — множители к базе класса относительно брига (20 м/с, 24°/с, 30 попаданий, 420 м, 6 с).
                    t.TuneClass(c.enemySpeed / 20f, c.enemyYaw / 24f, c.enemyHits / 30f, c.enemyRange / 420f, c.enemyCooldown / 6f);
                else
                {
                    if (eg != null) { eg.cooldown = c.enemyCooldown; eg.maxRange = scout ? c.scoutRange : c.enemyRange; }
                    t.Tune(scout ? c.scoutSpeed : c.enemySpeed, scout ? c.enemyYaw * 1.4f : c.enemyYaw, scout ? c.scoutHits : c.enemyHits, eg != null ? eg.maxRange : 420f, c.enemyCooldown);
                }
                t.aggroRange = c.aggroRange;
                t.heavyChance = c.heavyChance;
                enemies++;
                t.gameObject.SetActive(enemies <= Mathf.Max(1, c.enemyCount));
            }
            int allies = 0;
            foreach (AllyShip a in Object.FindObjectsByType<AllyShip>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (a.Class == ShipClass.None) a.hitsToSink = a.role == AllyShip.Role.Merchant ? c.merchantHits : a.role == AllyShip.Role.Wingman ? c.wingmanHits : c.guardHits;
                a.engageRange = c.allyEngage;
                allies++;
                a.gameObject.SetActive(allies <= c.allyCount);
            }
            if (ph != null) ph.damagePerHit = c.enemyDamage;
            Relations.GarrisonRadius = c.garrisonRadius; Relations.CorsairRadius = Mathf.Max(c.garrisonRadius + 300f, c.corsairRadius);
            Relations.WarnSeconds = c.warnSeconds; Relations.AimSeconds = c.aimSeconds; Relations.AimRange = c.aimRange; Relations.CalmAfter = c.calmAfter;
            FxSystem.PlayerDamageMul = c.playerDamage; FxSystem.AssistMargin = c.autoAssist;
            Relations.RaidGrudge = c.raidGrudge; Relations.RaidEvery = c.raidEveryMin * 60f; Relations.GrudgeCoolPerMin = c.grudgeCoolPerMin; Relations.OutlawSeconds = c.outlawMin * 60f;
            WeatherSystem w = WeatherSystem.Instance;
            if (w != null) { w.enabled = c.weather; w.clearMin = c.clearMinutesMin * 60f; w.clearMax = c.clearMinutesMax * 60f; }
            WindField.Mul = c.wind ? c.windMul : 0f;
            PlayerBoarding.Allowed = c.boarding;
            PlayerAutopilot.RepairBelowDefault = c.autoRepairBelow;
        }
    }
}
