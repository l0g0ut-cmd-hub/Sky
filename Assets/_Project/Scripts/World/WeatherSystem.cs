using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Погода (только в игре): ЯСНО (5–8 мин) → ДЫМКА (2–3 мин) → ГРОЗА (2–3 мин) → ЯСНО … Переходы плавные (40 с).
    /// • Дымка: даль быстрее тонет в цвете неба (глобальная воздушная перспектива), чуть меньше насыщенности.
    /// • Гроза: темнее и холоднее, солнце слабее, ветер сильнее; молнии — вспышка всего неба и короткий удар света
    ///   (картинка самой молнии — атласом позже, см. промты). Звук не трогается.
    /// Первые 4 минуты игры — всегда ясно.
    /// </summary>
    public class WeatherSystem : MonoBehaviour
    {
        public enum Kind { Clear, Haze, Storm }
        public static WeatherSystem Instance { get; private set; }
        public Kind Current { get; private set; } = Kind.Clear;
        public static readonly string[] Names = { "ЯСНО", "ДЫМКА", "ГРОЗА" };
        float until, haze, storm, flash, nextBolt = -1f;
        [System.NonSerialized] public float clearMin = 300f, clearMax = 480f;
        /// <summary>Тест: сразу перейти к погоде.</summary>
        public void Force(Kind k) { Current = k; until = Time.time + (k == Kind.Clear ? Random.Range(clearMin, clearMax) : Random.Range(120f, 180f)); HudNotice.Show("ТЕСТ: " + Names[(int)k], 0, 2f); }
        SkyTidePost post; Light sun;
        bool saved; float baseExposure, baseSaturation, baseSun; Vector4 baseAerial;

        void Awake() { Instance = this; until = Time.time + 240f; }

        void Update()
        {
            if (!Application.isPlaying) return;
            float dt = Time.deltaTime;
            if (!saved)
            {
                Camera cam = Camera.main;
                post = cam != null ? cam.GetComponent<SkyTidePost>() : null;
                sun = RenderSettings.sun;
                if (sun == null) foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
                if (post == null || sun == null) return;
                saved = true;
                baseExposure = post.exposure; baseSaturation = post.saturation; baseSun = sun.intensity;
                baseAerial = Shader.GetGlobalVector("_SkyTideAerial");
            }
            if (Time.time >= until) Next();
            haze = Mathf.MoveTowards(haze, Current == Kind.Clear ? 0f : 1f, dt / 40f);
            storm = Mathf.MoveTowards(storm, Current == Kind.Storm ? 1f : 0f, dt / 40f);
            // Молнии: в грозу раз в 5–14 с — двойная вспышка.
            if (storm > 0.6f)
            {
                if (nextBolt < 0f) nextBolt = Time.time + Random.Range(5f, 14f);
                if (Time.time >= nextBolt) { flash = 1f; nextBolt = Time.time + Random.Range(5f, 14f); Invoke(nameof(Afterflash), 0.12f); }
            }
            else nextBolt = -1f;
            flash = Mathf.MoveTowards(flash, 0f, dt * 6f);

            post.exposure = baseExposure * Mathf.Lerp(1f, 0.78f, storm) * (1f + 0.9f * flash);
            post.saturation = baseSaturation * Mathf.Lerp(1f, 0.9f, haze) * Mathf.Lerp(1f, 0.8f, storm);
            sun.intensity = baseSun * Mathf.Lerp(1f, 0.55f, storm) + 2.5f * flash;
            // Воздушная перспектива: дымка начинается ближе и густеет быстрее.
            Shader.SetGlobalVector("_SkyTideAerial", new Vector4(Mathf.Lerp(baseAerial.x, 160f, haze), Mathf.Lerp(baseAerial.y, 1200f, haze), Mathf.Lerp(baseAerial.z, 0.99f, haze), baseAerial.w));
        }

        void Afterflash() { flash = Mathf.Max(flash, 0.6f); }

        void Next()
        {
            Current = Current == Kind.Clear ? Kind.Haze : Current == Kind.Haze ? Kind.Storm : Kind.Clear;
            until = Time.time + (Current == Kind.Clear ? Random.Range(clearMin, clearMax) : Random.Range(120f, 180f));
            HudNotice.Show(Current == Kind.Storm ? "НАДВИГАЕТСЯ ГРОЗА" : Current == Kind.Haze ? "ДЫМКА · ВИДИМОСТЬ ПАДАЕТ" : "ПРОЯСНЯЕТСЯ", 0, 3f);
        }

        void OnDisable()
        {
            if (!saved) return;
            if (post != null) { post.exposure = baseExposure; post.saturation = baseSaturation; }
            if (sun != null) sun.intensity = baseSun;
            Shader.SetGlobalVector("_SkyTideAerial", baseAerial);
        }
    }
}
