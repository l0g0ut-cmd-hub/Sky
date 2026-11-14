using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Ход боя: трофеи за потопленных врагов (крепость меняет их у причала на усиление корабля) и короткое
    /// замедление времени при потоплении. Волн и роста силы врага нет — владелец их убрал (сборка 99).
    /// </summary>
    public class SkyCampaign : MonoBehaviour
    {
        public static SkyCampaign Instance { get; private set; }
        public static int Kills { get { return Instance != null ? Instance.kills : 0; } }

        [Tooltip("Замедление времени при потоплении врага: множитель и длительность (с, реального времени).")]
        public float slowScale = 0.3f, slowDuration = 1.6f;

        int kills, losses, trophies;
        float slowUntil = -1f;

        void Awake()
        {
            Instance = this;
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
