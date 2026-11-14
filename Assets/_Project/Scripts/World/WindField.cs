using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Ветер над облаками: направление медленно поворачивается (полный круг ~16 мин), сила дышит 0.3–1.
    /// Корабли по ветру идут быстрее (до +14%), на ветер — медленнее (до −14%); дым и частицы сносит туда же.
    /// В кадрах CI ветер стоит (направление исходного FxSystem.wind, сила 0.6).
    /// </summary>
    public static class WindField
    {
        static readonly Vector3 Base = new Vector3(-4f, 0f, 1.8f).normalized;
        public static Vector3 Dir
        {
            get
            {
                if (!Application.isPlaying) return Base;
                float a = Time.time * (Mathf.PI * 2f / 960f);
                return Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f) * Base;
            }
        }
        public static float Strength
        {
            get
            {
                if (!Application.isPlaying) return 0.6f;
                float s = 0.65f + 0.35f * Mathf.Sin(Time.time * 0.011f + 1.3f);
                // В грозу ветер крепче.
                return WeatherSystem.Instance != null && WeatherSystem.Instance.Current == WeatherSystem.Kind.Storm ? Mathf.Min(1.4f, s * 1.5f) : s;
            }
        }
        /// <summary>Множитель хода для корабля, идущего курсом forward.</summary>
        public static float SpeedFactor(Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) return 1f;
            return 1f + 0.14f * Strength * Vector3.Dot(forward.normalized, Dir);
        }
        /// <summary>Снос частиц, м/с.</summary>
        public static Vector3 Drift { get { return Dir * (4.4f * Strength); } }
    }
}
