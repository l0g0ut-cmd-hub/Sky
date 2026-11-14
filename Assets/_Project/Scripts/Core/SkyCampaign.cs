using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Ход боя: волны. Каждый потопленный враг — следующая волна: новый корабль прочнее, перезаряжается быстрее
    /// и стреляет кучнее (рост останавливается к ~8-й волне). Потопление — короткое замедление времени:
    /// взрыв и крушение видны во всей красе. Счёт волн и побед — на плашке HUD, лучшая волна сохраняется.
    /// </summary>
    public class SkyCampaign : MonoBehaviour
    {
        public static SkyCampaign Instance { get; private set; }
        public static int Wave { get { return Instance != null ? Instance.wave : 1; } }
        public static int Kills { get { return Instance != null ? Instance.kills : 0; } }
        public static int Best { get { return Instance != null ? Instance.best : 1; } }
        /// <summary>Когда объявлена текущая волна (для оповещения HUD).</summary>
        public static float WaveAnnouncedAt = -100f;

        [Tooltip("Замедление времени при потоплении врага: множитель и длительность (с, реального времени).")]
        public float slowScale = 0.3f, slowDuration = 1.6f;

        int wave = 1, kills, losses, best = 1, trophies;
        float slowUntil = -1f;
        bool haveBase; int baseHits; float baseCooldown, baseSpread, baseRange, baseSide;
        const string BestKey = "skytide_best_wave";

        void Awake()
        {
            Instance = this;
            best = Mathf.Max(1, PlayerPrefs.GetInt(BestKey, 1));
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Time.timeScale = 1f;
        }

        /// <summary>Враг потоплен.</summary>
        public static void EnemySunk()
        {
            SkyCampaign c = Instance;
            if (c == null) return;
            c.kills++;
            c.trophies++;
            c.slowUntil = Time.unscaledTime + c.slowDuration;
        }

        /// <summary>На смену потопленному выходит новый корабль: следующая волна, враг сильнее.</summary>
        public static void EnemyReturned(TargetShip t)
        {
            SkyCampaign c = Instance;
            if (c == null || t == null) return;
            EnemyGunner g = t.GetComponent<EnemyGunner>();
            if (!c.haveBase)
            {
                c.haveBase = true; c.baseHits = t.hitsToSink;
                if (g != null) { c.baseCooldown = g.cooldown; c.baseSpread = g.spreadDeg; c.baseRange = g.rangeError; c.baseSide = g.sideError; }
            }
            c.wave++;
            if (c.wave > c.best) { c.best = c.wave; PlayerPrefs.SetInt(BestKey, c.best); }
            int n = c.wave - 1;
            t.hitsToSink = Mathf.Min(c.baseHits + 5 * n, c.baseHits * 2);
            if (g != null)
            {
                g.cooldown = Mathf.Max(3.2f, c.baseCooldown - 0.35f * n);
                g.spreadDeg = Mathf.Max(1.3f, c.baseSpread - 0.14f * n);
                g.rangeError = Mathf.Max(0.035f, c.baseRange - 0.006f * n);
                g.sideError = Mathf.Max(0.012f, c.baseSide - 0.002f * n);
            }
            WaveAnnouncedAt = Time.time;
        }

        /// <summary>Трофеи с потопленных врагов: крепость у причала меняет их на усиление корабля. Возвращает и обнуляет.</summary>
        public static int TakeTrophies()
        {
            SkyCampaign c = Instance;
            if (c == null || c.trophies <= 0) return 0;
            int n = c.trophies; c.trophies = 0;
            return n;
        }
        public static int Trophies { get { return Instance != null ? Instance.trophies : 0; } }

        /// <summary>Корабль игрока потоплен.</summary>
        public static void PlayerLost()
        {
            if (Instance != null) Instance.losses++;
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            // Замедление — по реальному времени: быстро вниз, плавно обратно.
            float want = Time.unscaledTime < slowUntil ? slowScale : 1f;
            float rate = want < Time.timeScale ? 6f : 1.2f;
            Time.timeScale = Mathf.MoveTowards(Time.timeScale, want, Time.unscaledDeltaTime * rate);
        }
    }
}
