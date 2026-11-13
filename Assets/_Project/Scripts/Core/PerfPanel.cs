using UnityEngine;

namespace SkyTide
{
    /// <summary>
    /// Отладочная панель производительности: касание плашки FPS (или F1) — открыть/закрыть.
    /// Время кадра процессора и видеокарты (FrameTimingManager), средний FPS, и переключатели крупных частей
    /// картинки — чтобы на телефоне увидеть, что сколько стоит. Переключатели только для замера, в сохранение не идут.
    /// </summary>
    public class PerfPanel : MonoBehaviour
    {
        public static bool Open { get; private set; }
        /// <summary>Касание плашки FPS в HUD.</summary>
        public static void Toggle() { Open = !Open; }
        /// <summary>Область панели на экране (пиксели, начало сверху-слева) — касания внутри не уходят кораблю.</summary>
        public static Rect Area { get; private set; }
        SkyTideBootstrap game;
        readonly FrameTiming[] timings = new FrameTiming[1];
        float cpu, gpu, fps, acc; int frames;
        // Замер за последние 10 с: средний FPS, худший 1% кадров (99-й процентиль времени кадра), самый долгий кадр.
        readonly float[] window = new float[1200]; int wHead, wCount; float wSum;
        float avgFps, lowFps, worstMs, statClock;
        readonly float[] sorted = new float[1200];
        GUIStyle label, button;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Application.isPlaying) return;
            GameObject go = new GameObject("PerfPanel");
            DontDestroyOnLoad(go);
            go.AddComponent<PerfPanel>();
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1)) Open = !Open;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
            {
                cpu = Mathf.Lerp(cpu, (float)timings[0].cpuFrameTime, 0.1f);
                gpu = Mathf.Lerp(gpu, (float)timings[0].gpuFrameTime, 0.1f);
            }
            float dt = Time.unscaledDeltaTime;
            if (wCount == window.Length) { wSum -= window[wHead]; } else wCount++;
            window[wHead] = dt; wSum += dt; wHead = (wHead + 1) % window.Length;
            while (wCount > 1 && wSum - window[(wHead - wCount + window.Length) % window.Length] > 10f)
            { wSum -= window[(wHead - wCount + window.Length) % window.Length]; wCount--; }
            statClock += dt;
            if (Open && statClock >= 0.5f && wCount > 10)
            {
                statClock = 0f;
                for (int i = 0; i < wCount; i++) sorted[i] = window[(wHead - wCount + i + window.Length) % window.Length];
                System.Array.Sort(sorted, 0, wCount);
                avgFps = wCount / Mathf.Max(wSum, 1e-4f);
                lowFps = 1f / Mathf.Max(sorted[Mathf.Min(wCount - 1, Mathf.FloorToInt(wCount * 0.99f))], 1e-4f);
                worstMs = sorted[wCount - 1] * 1000f;
            }
            acc += dt; frames++;
            if (acc >= 0.5f) { fps = frames / acc; acc = 0f; frames = 0; }
        }

        void OnGUI()
        {
            if (!Open) return;
            GUI.depth = -800;
            if (game == null) game = FindFirstObjectByType<SkyTideBootstrap>();
            float u = Mathf.Min(Screen.width, Screen.height);
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(u * 0.032f), alignment = TextAnchor.MiddleLeft };
                label.normal.textColor = new Color(0.96f, 0.9f, 0.77f);
                button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(u * 0.032f) };
            }
            Rect r = new Rect(Screen.width * 0.27f, u * 0.05f, Screen.width * 0.46f, u * 0.86f);
            Area = r;
            GUI.color = new Color(0f, 0f, 0f, 0.8f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
            float y = r.y + u * 0.02f, h = u * 0.075f, x = r.x + u * 0.03f, w = r.width - u * 0.06f;
            GUI.Label(new Rect(x, y, w, h), string.Format("FPS {0:F0}   ЦП {1:F1} мс   ВК {2}", fps, cpu, gpu > 0.01f ? gpu.ToString("F1") + " мс" : "нет данных"), label); y += h;
            GUI.Label(new Rect(x, y, w, h), string.Format("10 с: средний {0:F0}   худший 1% {1:F0}   макс. кадр {2:F0} мс", avgFps, lowFps, worstMs), label); y += h;
            if (game == null) return;
            VolumetricClouds v = game.Volumetric;
            if (v != null)
            {
                if (Toggle(x, ref y, w, h, "Объёмные облака целиком", v.enabled)) game.SetVolumetric(!v.enabled);
                if (Toggle(x, ref y, w, h, "Башни", v.debugTowers)) v.debugTowers = !v.debugTowers;
                if (Toggle(x, ref y, w, h, "Море облаков", v.debugDeck)) v.debugDeck = !v.debugDeck;
                if (Toggle(x, ref y, w, h, "Импосторы (дальние облака)", v.impostors)) v.impostors = !v.impostors;
            }
            ShipCrew crew = game.Ship != null ? game.Ship.GetComponent<ShipCrew>() : null;
            if (crew != null && Toggle(x, ref y, w, h, "Экипаж", crew.Visible)) crew.SetVisible(!crew.Visible);
            SkyTidePost post = Camera.main != null ? Camera.main.GetComponent<SkyTidePost>() : null;
            if (post != null && Toggle(x, ref y, w, h, "Пост-обработка", post.enabled)) post.enabled = !post.enabled;
            if (game.Fx != null && Toggle(x, ref y, w, h, "Дым, вспышки, всплески", !game.Fx.debugHidden)) game.Fx.debugHidden = !game.Fx.debugHidden;
            if (GUI.Button(new Rect(x, y + h * 0.2f, w, h), "Закрыть", button)) Open = false;
        }

        bool Toggle(float x, ref float y, float w, float h, string name, bool on)
        {
            bool hit = GUI.Button(new Rect(x, y, w, h * 0.9f), (on ? "ВКЛ   " : "выкл   ") + name, button);
            y += h;
            return hit;
        }
    }
}
